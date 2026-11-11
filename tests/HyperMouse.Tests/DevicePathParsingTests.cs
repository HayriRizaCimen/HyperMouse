using HyperMouse.Core.Input.RawInput;

namespace HyperMouse.Tests;

public class DevicePathParsingTests
{
    [Theory]
    [InlineData(@"\?\HID#VID_413C&PID_2113&MI_00#7&332a22d&0&0000#{884b96c3}", 0x413C, 0x2113)]
    [InlineData(@"\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&021235_PID&aa22_REV&0001_1308ac00094b&Col01#9&2c91c242", 0x1235, 0xAA22)]
    [InlineData(@"\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02046d_PID&b03a_REV&0009&Col01", 0x046D, 0xB03A)]
    [InlineData(@"\?\HID#VEN_04F3&Col01#5&272e981b&0&0000", 0x04F3, 0x0000)]
    [InlineData(@"\?\Root#RDP_MOU#0000", 0, 0)]
    public void ParsesUsbBluetoothAndI2cPaths(string path, int vid, int pid)
    {
        var (v, p) = RawInputReceiver.ParseVidPid(path);
        Assert.Equal((ushort)vid, v);
        Assert.Equal((ushort)pid, p);
    }
}
