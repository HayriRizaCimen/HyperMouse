using System.Runtime.InteropServices;
using System.Text;
using HyperMouse.Core.Common;

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
            (0x01, 0x02) => "Fare",
            (0x01, 0x06) => "Klavye",
            (0x01, 0x80) => "System Control",
            (0x0C, 0x01) => "Consumer Control",
            (0x0D, _) => "Dokunmatik",
            _ when UsagePage >= 0xFF00 => "Vendor",
            _ => "HID"
        };
        return $"{Vid:X4}:{Pid:X4} {kind} (Page 0x{UsagePage:X2}, Usage 0x{Usage:X2})";
    }
}

public sealed class RawInputReceiver : IDisposable
{
    public const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;
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

    /// <summary>Side/standard button events decoded from any source.</summary>
    public event Action<RawMouseEvent, RawSource>? RawMouseEventReceived;

    /// <summary>Human readable diagnostic lines (device list, raw reports, unknown keys).</summary>
    public event Action<string>? Diagnostic;

    /// <summary>When true, every raw report from mouse-composite devices is hex dumped to <see cref="Diagnostic"/>.</summary>
    public bool VerboseDiagnostics { get; set; } = true;

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
                dwFlags = RIDEV_INPUTSINK,
                hwndTarget = _hwnd
            };
            bool ok = RegisterRawInputDevices(new[] { rid }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            if (ok)
            {
                _registered.Add(rid);
                anyOk = true;
            }
            Diagnostic?.Invoke($"[RAW KAYIT] Page 0x{page:X2} Usage 0x{usage:X2} -> {(ok ? "OK" : "HATA " + Marshal.GetLastWin32Error())}");
        }

        // Global WM_APPCOMMAND via shell hook (catches AC Back/Forward translated by Windows)
        _shellHookMsg = RegisterWindowMessageW("SHELLHOOK");
        _shellHookRegistered = RegisterShellHookWindow(_hwnd);
        Diagnostic?.Invoke($"[SHELL HOOK] Global WM_APPCOMMAND dinleyici -> {(_shellHookRegistered ? "OK" : "HATA")}");

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
            string tag = IsMouseComposite(info) ? " ← FARE BİLEŞENİ" : "";
            Diagnostic?.Invoke($"[CİHAZ] {info.Describe()}{tag}");
        }
    }

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

    private static (ushort, ushort) ParseVidPid(string name)
    {
        ushort vid = 0, pid = 0;
        string upper = name.ToUpperInvariant();
        int v = upper.IndexOf("VID_", StringComparison.Ordinal);
        int p = upper.IndexOf("PID_", StringComparison.Ordinal);
        if (v >= 0 && v + 8 <= upper.Length) ushort.TryParse(upper.AsSpan(v + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out vid);
        if (p >= 0 && p + 8 <= upper.Length) ushort.TryParse(upper.AsSpan(p + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out pid);
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
            catch (Exception ex) { Diagnostic?.Invoke($"[RAW HATA] {ex.Message}"); }
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
        string devName = device switch { 0x8000 => "Fare", 0x1000 => "OEM", _ => "Klavye/HID" };
        Diagnostic?.Invoke($"[APPCOMMAND] cmd={cmd} kaynak={devName}");

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
        ushort flags = mouse.usButtonFlags;
        if (flags == 0) return;

        long now = Environment.TickCount64;
        void Emit(MouseButton b, MouseEventType t, int delta = 0) =>
            RawMouseEventReceived?.Invoke(new RawMouseEvent(b, t, 0, 0, delta, now, false, 0), RawSource.Mouse);

        if ((flags & (RI_MOUSE_BUTTON_4_DOWN | RI_MOUSE_BUTTON_4_UP | RI_MOUSE_BUTTON_5_DOWN | RI_MOUSE_BUTTON_5_UP)) != 0)
            Diagnostic?.Invoke($"[RAW FARE] {(dev?.Describe() ?? "?")} buttonFlags=0x{flags:X4}");

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
            Diagnostic?.Invoke($"[RAW KLAVYE ← FARE] {dev!.Vid:X4}:{dev.Pid:X4} VK=0x{kb.VKey:X2} Scan=0x{kb.MakeCode:X2} {(isUp ? "UP" : "DOWN")}");
        }

        MouseButton btn = kb.VKey switch
        {
            VK_BROWSER_BACK => MouseButton.XButton1,
            VK_BROWSER_FORWARD => MouseButton.XButton2,
            _ => MouseButton.None
        };
        if (btn == MouseButton.None) return;

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
                Diagnostic?.Invoke($"[RAW HID ← FARE] {dev!.Describe()} rapor: {ToHex(report)}");
            }

            if (dev != null && dev.UsagePage == 0x0C)
            {
                ProcessConsumerReport(hDevice, report);
            }
        }
    }

    private void ProcessConsumerReport(IntPtr hDevice, byte[] report)
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
            Diagnostic?.Invoke($"[CONSUMER] Usage 0x{u:X3} {UsageName(u)} DOWN");
            EmitConsumer(u, MouseEventType.Down, now);
        }
        foreach (var u in previous.Where(u => !current.Contains(u)))
        {
            Diagnostic?.Invoke($"[CONSUMER] Usage 0x{u:X3} {UsageName(u)} UP");
            EmitConsumer(u, MouseEventType.Up, now);
        }

        previous.Clear();
        previous.UnionWith(current);
    }

    private void EmitConsumer(ushort usage, MouseEventType type, long now)
    {
        MouseButton btn = usage switch
        {
            USAGE_AC_BACK => MouseButton.XButton1,
            USAGE_AC_FORWARD => MouseButton.XButton2,
            _ => MouseButton.None
        };
        if (btn == MouseButton.None) return;
        RawMouseEventReceived?.Invoke(new RawMouseEvent(btn, type, 0, 0, 0, now, false, 0), RawSource.ConsumerControl);
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
