namespace HyperMouse.Core.Common;

/// <summary>
/// Logical mouse button and wheel action enumeration.
/// </summary>
public enum MouseButton
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3,
    XButton1 = 4,
    XButton2 = 5,
    WheelUp = 6,
    WheelDown = 7,
    WheelTiltLeft = 8,
    WheelTiltRight = 9
}

/// <summary>
/// Type of raw low-level mouse event.
/// </summary>
public enum MouseEventType
{
    Down = 1,
    Up = 2,
    Move = 3,
    Wheel = 4,
    HWheel = 5
}

