namespace HyperMouse.Core.Common;

/// <summary>
/// Immutable representation of a low-level mouse event intercepted from the operating system.
/// </summary>
public readonly record struct RawMouseEvent(
    MouseButton Button,
    MouseEventType EventType,
    int X,
    int Y,
    int Delta,
    long TimestampMs,
    bool Injected,
    nuint ExtraInfo
);

