using System.Runtime.InteropServices;

namespace HyperMouse.Core.Input.RawInput;

/// <summary>
/// Resolves a human readable model name for a HID interface path by walking the PnP device tree
/// (HID collection -> HID device -> USB / Bluetooth LE node). Bluetooth mice usually expose their
/// name only here (e.g. "Philips SPK7547"), not through HidD_GetProductString.
/// </summary>
public static class DeviceNameResolver
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
        public DEVPROPKEY(string g, uint p) { fmtid = new Guid(g); pid = p; }
    }

    private static readonly DEVPROPKEY InstanceId = new("78c34fc8-104a-4aca-9ea4-524d52996e57", 256);
    private static readonly DEVPROPKEY FriendlyName = new("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
    private static readonly DEVPROPKEY DeviceDesc = new("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
    private static readonly DEVPROPKEY Manufacturer = new("a45c254e-df1c-4efd-8020-67d146a850e0", 13);
    private static readonly DEVPROPKEY BusReportedDesc = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2", 4);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_PropertyW(string pszDeviceInterface, ref DEVPROPKEY key, out uint type, byte[]? buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint type, byte[]? buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    private const int CR_SUCCESS = 0;

    private static readonly string[] GenericNames =
    {
        "hid-compliant", "hid compliant", "hid uyumlu", "usb input device", "usb giriş aygıtı", "usb composite", "usb bileşik",
        "bluetooth low energy", "bluetooth le", "gatt", "hid keyboard", "hid klavye", "microsoft hidi2c", "hidi2c", "i2c hid",
        "usb receiver", "wireless receiver", "2.4g", "usb root hub", "generic usb hub", "standart", "standard", "bilinmeyen", "unknown",
        "bluetooth radio", "bluetooth adapter", "intel(r) wireless bluetooth"
    };

    public static bool IsGeneric(string name) =>
        string.IsNullOrWhiteSpace(name) || GenericNames.Any(g => name.Contains(g, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns (model, manufacturer). Empty strings when nothing specific is found.</summary>
    public static (string Model, string Manufacturer) Resolve(string interfacePath)
    {
        try
        {
            string instanceId = GetInterfaceString(interfacePath, InstanceId);
            if (instanceId.Length == 0 || CM_Locate_DevNodeW(out uint node, instanceId, 0) != CR_SUCCESS)
                return ("", "");

            string model = "", manufacturer = "", fallback = "";
            for (int depth = 0; depth < 5 && node != 0; depth++)
            {
                foreach (var key in new[] { FriendlyName, BusReportedDesc, DeviceDesc })
                {
                    string v = GetNodeString(node, key);
                    if (v.Length == 0) continue;
                    if (!IsGeneric(v)) { model = v; break; }
                    if (fallback.Length == 0 && key.pid == BusReportedDesc.pid && key.fmtid == BusReportedDesc.fmtid) fallback = v;
                }

                if (manufacturer.Length == 0)
                {
                    string m = GetNodeString(node, Manufacturer);
                    if (m.Length > 0 && !m.StartsWith("(", StringComparison.Ordinal) && !m.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                        manufacturer = m;
                }

                if (model.Length > 0) break;
                if (CM_Get_Parent(out uint parent, node, 0) != CR_SUCCESS) break;
                node = parent;
            }

            return (model.Length > 0 ? model : fallback, manufacturer);
        }
        catch
        {
            return ("", "");
        }
    }

    private static string GetInterfaceString(string path, DEVPROPKEY key)
    {
        uint size = 0;
        CM_Get_Device_Interface_PropertyW(path, ref key, out _, null, ref size, 0);
        if (size == 0) return "";
        var buf = new byte[size];
        return CM_Get_Device_Interface_PropertyW(path, ref key, out _, buf, ref size, 0) == CR_SUCCESS ? Decode(buf) : "";
    }

    private static string GetNodeString(uint node, DEVPROPKEY key)
    {
        uint size = 0;
        CM_Get_DevNode_PropertyW(node, ref key, out _, null, ref size, 0);
        if (size == 0) return "";
        var buf = new byte[size];
        return CM_Get_DevNode_PropertyW(node, ref key, out _, buf, ref size, 0) == CR_SUCCESS ? Decode(buf) : "";
    }

    private static string Decode(byte[] b)
    {
        string s = System.Text.Encoding.Unicode.GetString(b);
        int z = s.IndexOf('\0');
        return (z >= 0 ? s[..z] : s).Trim();
    }
}
