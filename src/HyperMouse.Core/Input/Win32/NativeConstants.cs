namespace HyperMouse.Core.Input.Win32;

public static class NativeConstants
{
    // Windows Hook IDs
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    // Window Mouse Messages (Client Area)
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_XBUTTONUP = 0x020C;
    public const int WM_XBUTTONDBLCLK = 0x020D;
    public const int WM_MOUSEHWHEEL = 0x020E;

    // Window Mouse Messages (Non-Client Area like title bars, taskbars, desktop)
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int WM_NCRBUTTONDOWN = 0x00A4;
    public const int WM_NCRBUTTONUP = 0x00A5;
    public const int WM_NCMBUTTONDOWN = 0x00A7;
    public const int WM_NCMBUTTONUP = 0x00A8;
    public const int WM_NCXBUTTONDOWN = 0x00AB;
    public const int WM_NCXBUTTONUP = 0x00AC;
    public const int WM_NCXBUTTONDBLCLK = 0x00AD;

    // Keyboard Messages (for gaming mice sending Browser Back / Forward)
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const uint VK_BROWSER_BACK = 0xA6;
    public const uint VK_BROWSER_FORWARD = 0xA7;

    // Quit message for internal hook thread pump
    public const uint WM_QUIT = 0x0012;

    // MSLLHOOKSTRUCT Flags
    public const uint LLMHF_INJECTED = 0x00000001;
    public const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;

    // XBUTTON definitions in mouseData HIWORD
    public const int XBUTTON1 = 0x0001;
    public const int XBUTTON2 = 0x0002;

    // Custom 32/64-bit signature injected into dwExtraInfo to prevent infinite loops:
    // ASCII "MMAP" = 0x4D4D4150
    public static readonly nuint HyperMouse_EXTRA_INFO = (nuint)0x4D4D4150;

    // SendInput Types
    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint INPUT_HARDWARE = 2;

    // Keyboard flags
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    // Mouse flags
    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_XDOWN = 0x0080;
    public const uint MOUSEEVENTF_XUP = 0x0100;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL = 0x1000;
}
