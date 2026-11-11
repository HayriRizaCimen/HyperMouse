using System.Runtime.InteropServices;
using System.Text;
using HyperMouse.Core.Common;
using HyperMouse.Core.Config;

namespace HyperMouse.Core.Input.RawInput;

/// <summary>
/// Where a side-button signal originated. Many cheap/gaming mice (e.g. Rampage SMX-R44, VID_1BCF)
/// are composite HID devices: the side buttons are NOT reported in the mouse collection but in a
/// separate Consumer Control (AC Back 0x224 / AC Forward 0x225) or Keyboard collection.
/// </summary>
public enum RawSource
{
    Mouse,
    Keyboard,
    ConsumerControl,
    AppCommand
}

public sealed record RawDeviceInfo(IntPtr Handle, string Name, ushort Vid, ushort Pid, ushort UsagePage, ushort Usage, uint Type)
{
    public string Describe()
    {
        string kind = (UsagePage, Usage) switch
        {
            (0x01, 0x02) => "Mouse",
            (0x01, 0x06) => "Keyboard",
            (0x01, 0x80) => "System Control",
            (0x0C, 0x01) => "Consumer Control",
            (0x0D, _) => "Touch",
            _ when UsagePage >= 0xFF00 => "Vendor",
            _ => "HID"
        };
        return $"{Vid:X4}:{Pid:X4} {kind} (Page 0x{UsagePage:X2}, Usage 0x{Usage:X2})";
    }
}

/// <summary>A physical mouse as identified from its HID descriptor strings.</summary>
public sealed record MouseDevice(IntPtr Handle, ushort Vid, ushort Pid, string Product, string Manufacturer, string Connection, string Path)
{
    public string DisplayName
    {
        get
        {
            string vendor = MouseVendors.Lookup(Vid);
            string product = Product.Trim();
            string brand = !string.IsNullOrWhiteSpace(Manufacturer) ? Manufacturer.Trim() : vendor;
            if (product.Length > 0)
            {
                if (brand.Length > 0 && !product.Contains(brand, StringComparison.OrdinalIgnoreCase))
                    return $"{brand} {product}";
                return product;
            }
            if (brand.Length > 0) return $"{brand} Fare";
            return Vid == 0 ? "Standart / Sanal Fare" : "Bilinmeyen Fare";
        }
    }

    public string VidPid => $"{Vid:X4}:{Pid:X4}";
}

/// <summary>USB vendor ids of common mouse makers and the OEM chipsets white-label mice are built on.</summary>
public static class MouseVendors
{
    private static readonly Dictionary<ushort, string> Map = new()
    {
        [0x046D] = "Logitech", [0x045E] = "Microsoft", [0x1532] = "Razer", [0x1038] = "SteelSeries",
        [0x1B1C] = "Corsair", [0x0951] = "HyperX", [0x03F0] = "HP", [0x0471] = "Philips",
        [0x17EF] = "Lenovo", [0x413C] = "Dell", [0x0B05] = "ASUS", [0x1044] = "Gigabyte",
        [0x3434] = "Keychron", [0x1E7D] = "ROCCAT", [0x2516] = "Cooler Master", [0x0738] = "Mad Catz",
        [0x05AC] = "Apple", [0x04CA] = "Lite-On", [0x04F2] = "Chicony", [0x062A] = "MosArt",
        [0x1BCF] = "Sunplus", [0x093A] = "PixArt", [0x258A] = "SINO WEALTH", [0x248A] = "Telink",
        [0x25A7] = "Areson", [0x3554] = "Compx", [0x0458] = "Genius", [0x04D9] = "Holtek",
        [0x3367] = "Endgame Gear", [0x361D] = "Finalmouse", [0x3710] = "Pulsar", [0x04F3] = "ELAN", [0x06CB] = "Synaptics",
    };

    public static string Lookup(ushort vid) => Map.TryGetValue(vid, out var n) ? n : "";
}

public sealed class RawInputReceiver : IDisposable
{
    public const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint RIDEV_DEVNOTIFY = 0x00002000;
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDI_DEVICEINFO = 0x2000000b;
    private const uint RIDI_PREPARSEDDATA = 0x20000005;
    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIM_TYPEHID = 2;
    private const ushort RI_KEY_BREAK = 0x01;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;

    // Shell hook (global WM_APPCOMMAND)
    private const int HSHELL_APPCOMMAND = 12;
    private const int APPCOMMAND_BROWSER_BACKWARD = 1;
    private const int APPCOMMAND_BROWSER_FORWARD = 2;

    public const ushort RI_MOUSE_BUTTON_1_DOWN = 0x0001;
    public const ushort RI_MOUSE_BUTTON_1_UP   = 0x0002;
    public const ushort RI_MOUSE_BUTTON_2_DOWN = 0x0004;
    public const ushort RI_MOUSE_BUTTON_2_UP   = 0x0008;
    public const ushort RI_MOUSE_BUTTON_3_DOWN = 0x0010;
    public const ushort RI_MOUSE_BUTTON_3_UP   = 0x0020;
    public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
    public const ushort RI_MOUSE_BUTTON_4_UP   = 0x0080;
    public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
    public const ushort RI_MOUSE_BUTTON_5_UP   = 0x0200;
    public const ushort RI_MOUSE_WHEEL         = 0x0400;
    public const ushort RI_MOUSE_HWHEEL        = 0x0800;

    private const ushort USAGE_AC_BACK = 0x0224;
    private const ushort USAGE_AC_FORWARD = 0x0225;
    private const ushort VK_BROWSER_BACK = 0xA6;
    private const ushort VK_BROWSER_FORWARD = 0xA7;

    #region Native
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeregisterShellHookWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessageW(string lpString);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection,
        [Out] ushort[] usageList, ref uint usageLength, IntPtr preparsedData, byte[] report, uint reportLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetProductString(IntPtr hidDeviceObject, byte[] buffer, uint bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetManufacturerString(IntPtr hidDeviceObject, byte[] buffer, uint bufferLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort usFlags;
        [FieldOffset(4)] public uint ulButtons;
        [FieldOffset(4)] public ushort usButtonFlags;
        [FieldOffset(6)] public ushort usButtonData;
        [FieldOffset(8)] public uint ulRawButtons;
        [FieldOffset(12)] public int lLastX;
        [FieldOffset(16)] public int lLastY;
        [FieldOffset(20)] public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    // RID_DEVICE_INFO (cbSize + dwType + 24-byte union)
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct RID_DEVICE_INFO
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint dwType;
        // HID branch
        [FieldOffset(8)] public uint hid_dwVendorId;
        [FieldOffset(12)] public uint hid_dwProductId;
        [FieldOffset(16)] public uint hid_dwVersionNumber;
        [FieldOffset(20)] public ushort hid_usUsagePage;
        [FieldOffset(22)] public ushort hid_usUsage;
    }
    #endregion

    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();

    private IntPtr _hwnd = IntPtr.Zero;
    private int _shellHookMsg;
    private bool _shellHookRegistered;
    private readonly List<RAWINPUTDEVICE> _registered = new();
    private readonly Dictionary<IntPtr, RawDeviceInfo> _deviceCache = new();
    private readonly Dictionary<IntPtr, IntPtr> _preparsedCache = new();
    private readonly Dictionary<IntPtr, HashSet<ushort>> _consumerPressed = new();
    private readonly HashSet<string> _composeMouseVidPids = new();
    private readonly Dictionary<IntPtr, byte[]> _lastHidReport = new();
    private readonly List<MouseDevice> _mice = new();
    private IntPtr _activeMouseHandle = IntPtr.Zero;
    private uint _lastRawButtons;

    // ---- learn mode / learned signals ----
    private bool _learning;
    private LearnedSignal? _learnCandidate;
    private readonly Dictionary<MouseButton, LearnedSignal> _bindings = new();
    private readonly HashSet<MouseButton> _bindingDown = new();

    /// <summary>All physical mice currently attached (real devices first).</summary>
    public IReadOnlyList<MouseDevice> Mice => _mice;

    /// <summary>The mouse that produced the most recent input.</summary>
    public MouseDevice? ActiveMouse => _mice.FirstOrDefault(m => m.Handle == _activeMouseHandle);

    /// <summary>Raised when mice are plugged/unplugged or the device list is refreshed.</summary>
    public event Action? MiceChanged;

    /// <summary>A device was plugged/unplugged. Host should debounce and call <see cref="RefreshDevices"/>.</summary>
    public event Action? DevicesChanged;

    /// <summary>Raised when input arrives from a different physical mouse than before.</summary>
    public event Action<MouseDevice>? ActiveMouseChanged;

    /// <summary>Learn mode: first non-standard signal arrived (a release may still follow).</summary>
    public event Action<LearnedSignal>? LearnCandidate;

    /// <summary>Learn mode finished with the final signal (HasRelease set).</summary>
    public event Action<LearnedSignal>? SignalLearned;

    public bool IsLearning => _learning;

    /// <summary>Side/standard button events decoded from any source.</summary>
    public event Action<RawMouseEvent, RawSource>? RawMouseEventReceived;

    /// <summary>Human readable diagnostic lines (device list, raw reports, unknown keys).</summary>
    public event Action<string>? Diagnostic;

    /// <summary>When true, every raw report from mouse-composite devices is hex dumped to <see cref="Diagnostic"/>.</summary>
    public bool VerboseDiagnostics { get; set; } = false;

    public IReadOnlyCollection<RawDeviceInfo> Devices => _deviceCache.Values;

    public bool Register(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        _hwnd = hwnd;

        EnumerateDevices();

        // Standard collections: Mouse, Keyboard, Consumer Control, System Control
        var pairs = new List<(ushort Page, ushort Usage)>
        {
            (0x01, 0x02),
            (0x01, 0x06),
            (0x0C, 0x01),
            (0x01, 0x80),
        };

        // Vendor-defined collections belonging to mouse composites (side buttons on some firmwares)
        foreach (var dev in _deviceCache.Values)
        {
            if (dev.Type == RIM_TYPEHID && dev.UsagePage >= 0xFF00 && IsMouseComposite(dev))
            {
                if (!pairs.Contains((dev.UsagePage, dev.Usage)))
                    pairs.Add((dev.UsagePage, dev.Usage));
            }
        }

        bool anyOk = false;
        foreach (var (page, usage) in pairs)
        {
            var rid = new RAWINPUTDEVICE
            {
                usUsagePage = page,
                usUsage = usage,
                dwFlags = RIDEV_INPUTSINK | (page == 0x01 && usage == 0x02 ? RIDEV_DEVNOTIFY : 0),
                hwndTarget = _hwnd
            };
            bool ok = RegisterRawInputDevices(new[] { rid }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            if (ok)
            {
                _registered.Add(rid);
                anyOk = true;
            }
            Diagnostic?.Invoke($"[RawInput] Register Page 0x{page:X2} Usage 0x{usage:X2}: {(ok ? "OK" : "Error " + Marshal.GetLastWin32Error())}");
        }

        // Global WM_APPCOMMAND via shell hook (catches AC Back/Forward translated by Windows)
        _shellHookMsg = RegisterWindowMessageW("SHELLHOOK");
        _shellHookRegistered = RegisterShellHookWindow(_hwnd);
        Diagnostic?.Invoke($"[Shell Hook] WM_APPCOMMAND: {(_shellHookRegistered ? "OK" : "Error")}");

        return anyOk;
    }

    private void EnumerateDevices()
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref count, size);
        if (count == 0) return;

        var list = new RAWINPUTDEVICELIST[count];
        if (GetRawInputDeviceList(list, ref count, size) == unchecked((uint)-1)) return;

        foreach (var d in list)
        {
            var info = QueryDevice(d.hDevice);
            if (info == null) continue;
            if (info.UsagePage == 0x01 && info.Usage == 0x02 && info.Vid != 0)
                _composeMouseVidPids.Add($"{info.Vid:X4}:{info.Pid:X4}");
        }

        foreach (var info in _deviceCache.Values.OrderBy(i => i.Vid).ThenBy(i => i.UsagePage))
        {
            if (info.Vid == 0) continue;
            string tag = IsMouseComposite(info) ? " (mouse composite)" : "";
            Diagnostic?.Invoke($"[Device] {info.Describe()}{tag}");
        }

        BuildMouseCatalogue();
    }

    /// <summary>Re-reads the device list (after plug/unplug) and rebuilds the mouse catalogue.</summary>
    public void RefreshDevices()
    {
        foreach (var p in _preparsedCache.Values)
            if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        _preparsedCache.Clear();
        _deviceCache.Clear();
        _composeMouseVidPids.Clear();
        _lastHidReport.Clear();
        _consumerPressed.Clear();
        EnumerateDevices();
    }

    private void BuildMouseCatalogue()
    {
        _mice.Clear();
        var hids = _deviceCache.Values.Where(d => d.Type == RIM_TYPEHID).ToList();
        foreach (var dev in _deviceCache.Values.Where(d => d.Type == RIM_TYPEMOUSE))
        {
            string product = "", manufacturer = "";
            // The mouse collection itself, then any sibling collection of the same device, may answer.
            var paths = new[] { dev.Name }.Concat(hids.Where(h => dev.Vid != 0 && h.Vid == dev.Vid && h.Pid == dev.Pid).Select(h => h.Name));
            foreach (var path in paths)
            {
                (product, manufacturer) = ReadHidStrings(path);
                if (product.Length > 0) break;
            }

            string connection = DetectConnection(dev.Name);
            if (DeviceNameResolver.IsGeneric(product) && !connection.StartsWith("Dahili"))
            {
                // Bluetooth / some USB mice expose their real name only in the PnP tree
                var (model, maker) = DeviceNameResolver.Resolve(dev.Name);
                if (model.Length > 0 && (!DeviceNameResolver.IsGeneric(model) || product.Length == 0)) product = model;
                if (manufacturer.Length == 0 || manufacturer.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) manufacturer = maker;
            }
            if (connection.StartsWith("Dahili") && DeviceNameResolver.IsGeneric(product))
                product = "Touchpad";

            _mice.Add(new MouseDevice(dev.Handle, dev.Vid, dev.Pid, product, manufacturer, connection, dev.Name));
        }
        // External mice first, then built-in touchpads, then virtual devices
        static int Rank(MouseDevice m) => m.Vid == 0 ? 2 : m.Connection.StartsWith("Dahili") ? 1 : 0;
        _mice.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
        foreach (var m in _mice.Where(x => x.Vid != 0))
            Diagnostic?.Invoke($"[Mouse] {m.DisplayName} ({m.VidPid}, {m.Connection})");
        MiceChanged?.Invoke();
    }

    private static string DetectConnection(string path)
    {
        string u = path.ToUpperInvariant();
        if (u.Contains("{00001124-") || u.Contains("BTHENUM") || u.Contains("BTHLE") || u.Contains("{00001812-")) return "Bluetooth";
        if (u.Contains("VID_")) return "USB / 2.4 GHz";
        if (u.Contains("VEN_")) return "Dahili (Touchpad)";
        if (u.Contains("RDP") || u.Contains("TERMINPUT")) return "Remote Desktop";
        return "Sistem";
    }

    private static (string Product, string Manufacturer) ReadHidStrings(string path)
    {
        if (string.IsNullOrEmpty(path)) return ("", "");
        // Access 0 = query only; works even for mice that Windows opens exclusively.
        IntPtr h = CreateFileW(path, 0, 0x3 /* FILE_SHARE_READ|WRITE */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1)) return ("", "");
        try
        {
            var buf = new byte[512];
            string product = HidD_GetProductString(h, buf, (uint)buf.Length) ? Clean(buf) : "";
            Array.Clear(buf);
            string manufacturer = HidD_GetManufacturerString(h, buf, (uint)buf.Length) ? Clean(buf) : "";
            return (product, manufacturer);
        }
        finally { CloseHandle(h); }

        static string Clean(byte[] b)
        {
            string s = Encoding.Unicode.GetString(b);
            int z = s.IndexOf('\0');
            return (z >= 0 ? s[..z] : s).Trim();
        }
    }

    // ==================== LEARN MODE ====================

    /// <summary>Listen for the next non-standard signal from a mouse (DPI, profile, extra buttons).</summary>
    public void BeginLearn()
    {
        _learning = true;
        _learnCandidate = null;
    }

    public void CancelLearn()
    {
        _learning = false;
        _learnCandidate = null;
    }

    /// <summary>Finish learning without having seen a release (one-shot signal, emitted as a tap).</summary>
    public void CompleteLearn()
    {
        if (!_learning || _learnCandidate == null) return;
        var sig = _learnCandidate;
        _learning = false;
        _learnCandidate = null;
        sig.HasRelease = false;
        SignalLearned?.Invoke(sig);
    }

    public void SetBindings(IReadOnlyDictionary<MouseButton, LearnedSignal> bindings)
    {
        _bindings.Clear();
        foreach (var kv in bindings) _bindings[kv.Key] = kv.Value;
        _bindingDown.Clear();
    }

    /// <summary>Central entry for every non-standard signal. <paramref name="pressed"/> false = release.</summary>
    private void OnCustomSignal(LearnedSignal sig, bool pressed)
    {
        long now = Environment.TickCount64;

        if (_learning)
        {
            if (pressed && _learnCandidate == null)
            {
                _learnCandidate = sig;
                Diagnostic?.Invoke($"[Learn] Sinyal: {sig.Description}");
                LearnCandidate?.Invoke(sig);
            }
            else if (!pressed && _learnCandidate != null && SameSource(_learnCandidate, sig))
            {
                var done = _learnCandidate;
                done.HasRelease = true;
                _learning = false;
                _learnCandidate = null;
                SignalLearned?.Invoke(done);
            }
            return;
        }

        foreach (var (btn, bound) in _bindings)
        {
            if (pressed && bound.Matches(sig))
            {
                RawMouseEventReceived?.Invoke(new RawMouseEvent(btn, MouseEventType.Down, 0, 0, 0, now, false, 0), RawSource.Mouse);
                if (bound.HasRelease) _bindingDown.Add(btn);
                else RawMouseEventReceived?.Invoke(new RawMouseEvent(btn, MouseEventType.Up, 0, 0, 0, now, false, 0), RawSource.Mouse);
            }
            else if (!pressed && _bindingDown.Contains(btn) && SameSource(bound, sig))
            {
                _bindingDown.Remove(btn);
                RawMouseEventReceived?.Invoke(new RawMouseEvent(btn, MouseEventType.Up, 0, 0, 0, now, false, 0), RawSource.Mouse);
            }
        }
    }

    private static bool SameSource(LearnedSignal a, LearnedSignal b) =>
        a.Kind == b.Kind && a.Vid == b.Vid && a.Pid == b.Pid && a.UsagePage == b.UsagePage && a.Usage == b.Usage &&
        (a.Kind == "Hid" || a.Code == b.Code);

    private static LearnedSignal MakeSig(string kind, RawDeviceInfo dev, uint code, string desc, string hex = "") => new()
    {
        Kind = kind, Vid = dev.Vid, Pid = dev.Pid, UsagePage = dev.UsagePage, Usage = dev.Usage,
        Code = code, ReportHex = hex, Description = desc
    };

    private bool IsMouseComposite(RawDeviceInfo info) => _composeMouseVidPids.Contains($"{info.Vid:X4}:{info.Pid:X4}");

    private RawDeviceInfo? QueryDevice(IntPtr hDevice)
    {
        if (hDevice == IntPtr.Zero) return null;
        if (_deviceCache.TryGetValue(hDevice, out var cached)) return cached;

        string name = "";
        uint nameLen = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref nameLen);
        if (nameLen > 0)
        {
            IntPtr buf = Marshal.AllocHGlobal((int)nameLen * 2);
            try
            {
                if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buf, ref nameLen) != unchecked((uint)-1))
                    name = Marshal.PtrToStringUni(buf) ?? "";
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        uint infoSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>();
        IntPtr infoBuf = Marshal.AllocHGlobal((int)infoSize);
        RID_DEVICE_INFO di;
        try
        {
            Marshal.WriteInt32(infoBuf, (int)infoSize);
            if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, infoBuf, ref infoSize) == unchecked((uint)-1))
                return null;
            di = Marshal.PtrToStructure<RID_DEVICE_INFO>(infoBuf);
        }
        finally { Marshal.FreeHGlobal(infoBuf); }

        ushort vid = 0, pid = 0, page = 0, usage = 0;
        switch (di.dwType)
        {
            case RIM_TYPEHID:
                vid = (ushort)di.hid_dwVendorId;
                pid = (ushort)di.hid_dwProductId;
                page = di.hid_usUsagePage;
                usage = di.hid_usUsage;
                break;
            case RIM_TYPEMOUSE:
                page = 0x01; usage = 0x02;
                (vid, pid) = ParseVidPid(name);
                break;
            case RIM_TYPEKEYBOARD:
                page = 0x01; usage = 0x06;
                (vid, pid) = ParseVidPid(name);
                break;
        }

        var info = new RawDeviceInfo(hDevice, name, vid, pid, page, usage, di.dwType);
        _deviceCache[hDevice] = info;
        return info;
    }

    private static readonly System.Text.RegularExpressions.Regex VidRx = new(
        @"VID_(?<v>[0-9A-F]{4})|VID&[0-9A-F]{2}(?<v>[0-9A-F]{4})|VEN_(?<v>[0-9A-F]{4})",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex PidRx = new(
        @"PID[_&](?<p>[0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Parses USB ("VID_046D&PID_C52B"), Bluetooth LE ("VID&02046d_PID&b03a") and I2C ("VEN_04F3") device paths.
    /// </summary>
    public static (ushort Vid, ushort Pid) ParseVidPid(string name)
    {
        ushort vid = 0, pid = 0;
        var vm = VidRx.Match(name);
        if (vm.Success) ushort.TryParse(vm.Groups["v"].Value, System.Globalization.NumberStyles.HexNumber, null, out vid);
        var pm = PidRx.Match(name);
        if (pm.Success) ushort.TryParse(pm.Groups["p"].Value, System.Globalization.NumberStyles.HexNumber, null, out pid);
        return (vid, pid);
    }

    private IntPtr GetPreparsed(IntPtr hDevice)
    {
        if (_preparsedCache.TryGetValue(hDevice, out var p)) return p;
        uint size = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        IntPtr buf = IntPtr.Zero;
        if (size > 0)
        {
            buf = Marshal.AllocHGlobal((int)size);
            if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, buf, ref size) == unchecked((uint)-1))
            {
                Marshal.FreeHGlobal(buf);
                buf = IntPtr.Zero;
            }
        }
        _preparsedCache[hDevice] = buf;
        return buf;
    }

    public IntPtr ProcessMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT && lParam != IntPtr.Zero)
        {
            try { HandleWmInput(lParam); }
            catch (Exception ex) { Diagnostic?.Invoke($"[RawInput] Error: {ex.Message}"); }
        }
        else if (msg == WM_INPUT_DEVICE_CHANGE)
        {
            DevicesChanged?.Invoke();
        }
        else if (_shellHookMsg != 0 && msg == _shellHookMsg && (int)wParam == HSHELL_APPCOMMAND)
        {
            HandleAppCommand(lParam);
        }

        return IntPtr.Zero;
    }

    private void HandleAppCommand(IntPtr lParam)
    {
        int hi = (int)(((long)lParam >> 16) & 0xFFFF);
        int cmd = hi & ~0xF000;
        int device = hi & 0xF000;
        string devName = device switch { 0x8000 => "Mouse", 0x1000 => "OEM", _ => "Keyboard/HID" };
        Diagnostic?.Invoke($"[AppCommand] cmd={cmd} source={devName}");

        MouseButton btn = cmd switch
        {
            APPCOMMAND_BROWSER_BACKWARD => MouseButton.XButton1,
            APPCOMMAND_BROWSER_FORWARD => MouseButton.XButton2,
            _ => MouseButton.None
        };
        if (btn == MouseButton.None) return;

        long now = Environment.TickCount64;
        RawMouseEventReceived?.Invoke(new RawMouseEvent(btn, MouseEventType.Down, 0, 0, 0, now, false, 0), RawSource.AppCommand);
        RawMouseEventReceived?.Invoke(new RawMouseEvent(btn, MouseEventType.Up, 0, 0, 0, now, false, 0), RawSource.AppCommand);
    }

    private void HandleWmInput(IntPtr hRawInput)
    {
        uint dwSize = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref dwSize, HeaderSize);
        if (dwSize == 0) return;

        IntPtr pData = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (GetRawInputData(hRawInput, RID_INPUT, pData, ref dwSize, HeaderSize) != dwSize) return;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(pData);
            IntPtr body = IntPtr.Add(pData, (int)HeaderSize);
            var dev = QueryDevice(header.hDevice);

            switch (header.dwType)
            {
                case RIM_TYPEMOUSE:
                    ProcessRawMouse(Marshal.PtrToStructure<RAWMOUSE>(body), dev);
                    break;
                case RIM_TYPEKEYBOARD:
                    ProcessRawKeyboard(Marshal.PtrToStructure<RAWKEYBOARD>(body), dev);
                    break;
                case RIM_TYPEHID:
                    ProcessRawHid(body, header.hDevice, dev);
                    break;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pData);
        }
    }

    private void ProcessRawMouse(RAWMOUSE mouse, RawDeviceInfo? dev)
    {
        if (dev != null && dev.Handle != _activeMouseHandle)
        {
            _activeMouseHandle = dev.Handle;
            var m = _mice.FirstOrDefault(x => x.Handle == dev.Handle);
            if (m != null) ActiveMouseChanged?.Invoke(m);
        }

        // Vendor-specific raw button bits beyond the 5 standard buttons (some firmwares put DPI here)
        uint extraBits = mouse.ulRawButtons & ~0x1Fu;
        if (extraBits != _lastRawButtons && dev != null)
        {
            uint pressedBits = extraBits & ~_lastRawButtons;
            uint releasedBits = _lastRawButtons & ~extraBits;
            _lastRawButtons = extraBits;
            if (pressedBits != 0) OnCustomSignal(MakeSig("RawBits", dev, pressedBits, $"Raw button bits 0x{pressedBits:X}"), true);
            if (releasedBits != 0) OnCustomSignal(MakeSig("RawBits", dev, releasedBits, ""), false);
        }

        ushort flags = mouse.usButtonFlags;
        if (flags == 0) return;

        long now = Environment.TickCount64;
        void Emit(MouseButton b, MouseEventType t, int delta = 0) =>
            RawMouseEventReceived?.Invoke(new RawMouseEvent(b, t, 0, 0, delta, now, false, 0), RawSource.Mouse);

        if ((flags & (RI_MOUSE_BUTTON_4_DOWN | RI_MOUSE_BUTTON_4_UP | RI_MOUSE_BUTTON_5_DOWN | RI_MOUSE_BUTTON_5_UP)) != 0)
            Diagnostic?.Invoke($"[RawInput Mouse] {(dev?.Describe() ?? "?")} buttonFlags=0x{flags:X4}");

        if ((flags & RI_MOUSE_BUTTON_4_DOWN) != 0) Emit(MouseButton.XButton1, MouseEventType.Down);
        if ((flags & RI_MOUSE_BUTTON_4_UP) != 0) Emit(MouseButton.XButton1, MouseEventType.Up);
        if ((flags & RI_MOUSE_BUTTON_5_DOWN) != 0) Emit(MouseButton.XButton2, MouseEventType.Down);
        if ((flags & RI_MOUSE_BUTTON_5_UP) != 0) Emit(MouseButton.XButton2, MouseEventType.Up);
        if ((flags & RI_MOUSE_BUTTON_1_DOWN) != 0) Emit(MouseButton.Left, MouseEventType.Down);
        if ((flags & RI_MOUSE_BUTTON_1_UP) != 0) Emit(MouseButton.Left, MouseEventType.Up);
        if ((flags & RI_MOUSE_BUTTON_2_DOWN) != 0) Emit(MouseButton.Right, MouseEventType.Down);
        if ((flags & RI_MOUSE_BUTTON_2_UP) != 0) Emit(MouseButton.Right, MouseEventType.Up);
        if ((flags & RI_MOUSE_BUTTON_3_DOWN) != 0) Emit(MouseButton.Middle, MouseEventType.Down);
        if ((flags & RI_MOUSE_BUTTON_3_UP) != 0) Emit(MouseButton.Middle, MouseEventType.Up);

        if ((flags & RI_MOUSE_WHEEL) != 0)
        {
            short delta = unchecked((short)mouse.usButtonData);
            Emit(delta > 0 ? MouseButton.WheelUp : MouseButton.WheelDown, MouseEventType.Wheel, delta);
        }
        if ((flags & RI_MOUSE_HWHEEL) != 0)
        {
            short delta = unchecked((short)mouse.usButtonData);
            Emit(delta > 0 ? MouseButton.WheelTiltRight : MouseButton.WheelTiltLeft, MouseEventType.HWheel, delta);
        }
    }

    private void ProcessRawKeyboard(RAWKEYBOARD kb, RawDeviceInfo? dev)
    {
        bool isUp = (kb.Flags & RI_KEY_BREAK) != 0;
        bool fromMouse = dev != null && IsMouseComposite(dev);

        // Only report keys coming from the mouse's own keyboard collection (avoid logging real typing)
        if (fromMouse)
        {
            Diagnostic?.Invoke($"[RawInput Keyboard ← Mouse] {dev!.Vid:X4}:{dev.Pid:X4} VK=0x{kb.VKey:X2} Scan=0x{kb.MakeCode:X2} {(isUp ? "UP" : "DOWN")}");
        }

        MouseButton btn = kb.VKey switch
        {
            VK_BROWSER_BACK => MouseButton.XButton1,
            VK_BROWSER_FORWARD => MouseButton.XButton2,
            _ => MouseButton.None
        };
        if (btn == MouseButton.None)
        {
            // Any other key coming from the mouse itself is a candidate custom button (e.g. DPI -> F-key)
            if (fromMouse)
                OnCustomSignal(MakeSig("Key", dev!, kb.VKey, $"Keyboard VK 0x{kb.VKey:X2}"), !isUp);
            return;
        }

        RawMouseEventReceived?.Invoke(
            new RawMouseEvent(btn, isUp ? MouseEventType.Up : MouseEventType.Down, 0, 0, 0, Environment.TickCount64, false, 0),
            RawSource.Keyboard);
    }

    private void ProcessRawHid(IntPtr body, IntPtr hDevice, RawDeviceInfo? dev)
    {
        int sizeHid = Marshal.ReadInt32(body, 0);
        int count = Marshal.ReadInt32(body, 4);
        if (sizeHid <= 0 || count <= 0) return;

        bool fromMouse = dev != null && IsMouseComposite(dev);

        for (int i = 0; i < count; i++)
        {
            var report = new byte[sizeHid];
            Marshal.Copy(IntPtr.Add(body, 8 + i * sizeHid), report, 0, sizeHid);

            if (VerboseDiagnostics && fromMouse)
            {
                Diagnostic?.Invoke($"[HID ← Mouse] {dev!.Describe()} report: {ToHex(report)}");
            }

            if (dev != null && dev.UsagePage == 0x0C)
            {
                ProcessConsumerReport(hDevice, report, fromMouse ? dev : null);
            }
            else if (dev != null && fromMouse)
            {
                ProcessGenericReport(hDevice, report, dev);
            }
        }
    }

    /// <summary>Vendor / System Control collections of a mouse: a changed report = press, the next change = release.</summary>
    private void ProcessGenericReport(IntPtr hDevice, byte[] report, RawDeviceInfo dev)
    {
        _lastHidReport.TryGetValue(hDevice, out var prev);
        if (prev != null && prev.AsSpan().SequenceEqual(report)) return;
        _lastHidReport[hDevice] = report;

        bool idle = report.Skip(1).All(b => b == 0);
        if (prev != null && !prev.Skip(1).All(b => b == 0))
            OnCustomSignal(MakeSig("Hid", dev, 0, "", ToHex(prev)), false);
        if (!idle)
            OnCustomSignal(MakeSig("Hid", dev, 0, $"HID report (Page 0x{dev.UsagePage:X2}) {ToHex(report)}", ToHex(report)), true);
    }

    private void ProcessConsumerReport(IntPtr hDevice, byte[] report, RawDeviceInfo? mouseDev)
    {
        var current = new HashSet<ushort>();
        IntPtr preparsed = GetPreparsed(hDevice);
        bool parsed = false;

        if (preparsed != IntPtr.Zero)
        {
            var usages = new ushort[32];
            uint len = (uint)usages.Length;
            int status = HidP_GetUsages(0 /* HidP_Input */, 0x0C, 0, usages, ref len, preparsed, report, (uint)report.Length);
            if (status == HIDP_STATUS_SUCCESS)
            {
                parsed = true;
                for (int i = 0; i < len; i++) current.Add(usages[i]);
            }
        }

        // Fallback: typical consumer report = [ReportId, UsageLo, UsageHi]
        if (!parsed && report.Length >= 3)
        {
            ushort u = (ushort)(report[1] | (report[2] << 8));
            if (u != 0) current.Add(u);
        }

        if (!_consumerPressed.TryGetValue(hDevice, out var previous))
        {
            previous = new HashSet<ushort>();
            _consumerPressed[hDevice] = previous;
        }

        long now = Environment.TickCount64;
        foreach (var u in current.Where(u => !previous.Contains(u)))
        {
            Diagnostic?.Invoke($"[Consumer Control] Usage 0x{u:X3} {UsageName(u)} DOWN");
            if (!EmitConsumer(u, MouseEventType.Down, now) && mouseDev != null)
                OnCustomSignal(MakeSig("Consumer", mouseDev, u, $"Consumer Usage 0x{u:X3} {UsageName(u)}".Trim()), true);
        }
        foreach (var u in previous.Where(u => !current.Contains(u)))
        {
            Diagnostic?.Invoke($"[Consumer Control] Usage 0x{u:X3} {UsageName(u)} UP");
            if (!EmitConsumer(u, MouseEventType.Up, now) && mouseDev != null)
                OnCustomSignal(MakeSig("Consumer", mouseDev, u, ""), false);
        }

        previous.Clear();
        previous.UnionWith(current);
    }

    private bool EmitConsumer(ushort usage, MouseEventType type, long now)
    {
        MouseButton btn = usage switch
        {
            USAGE_AC_BACK => MouseButton.XButton1,
            USAGE_AC_FORWARD => MouseButton.XButton2,
            _ => MouseButton.None
        };
        if (btn == MouseButton.None) return false;
        RawMouseEventReceived?.Invoke(new RawMouseEvent(btn, type, 0, 0, 0, now, false, 0), RawSource.ConsumerControl);
        return true;
    }

    private static string UsageName(ushort u) => u switch
    {
        0x224 => "(AC Back)",
        0x225 => "(AC Forward)",
        0x0E9 => "(Volume Up)",
        0x0EA => "(Volume Down)",
        0x0E2 => "(Mute)",
        0x0CD => "(Play/Pause)",
        0x0B5 => "(Next Track)",
        0x0B6 => "(Prev Track)",
        _ => ""
    };

    private static string ToHex(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 3);
        foreach (var b in data) sb.Append(b.ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    public void Dispose()
    {
        if (_registered.Count > 0)
        {
            var remove = _registered.Select(r => new RAWINPUTDEVICE
            {
                usUsagePage = r.usUsagePage,
                usUsage = r.usUsage,
                dwFlags = RIDEV_REMOVE,
                hwndTarget = IntPtr.Zero
            }).ToArray();
            RegisterRawInputDevices(remove, (uint)remove.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _registered.Clear();
        }

        if (_shellHookRegistered && _hwnd != IntPtr.Zero)
        {
            DeregisterShellHookWindow(_hwnd);
            _shellHookRegistered = false;
        }

        foreach (var p in _preparsedCache.Values)
            if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        _preparsedCache.Clear();
    }
}
