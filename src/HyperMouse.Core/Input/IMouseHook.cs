using HyperMouse.Core.Common;

namespace HyperMouse.Core.Input;

public delegate bool MouseEventFilter(RawMouseEvent mouseEvent);

public interface IMouseHook : IDisposable
{
    event Action<RawMouseEvent>? MouseEventReceived;
    
    /// <summary>
    /// Synchronous filter called inside the hook callback. 
    /// If returns true, the event is suppressed (blocked from passing to other apps).
    /// </summary>
    MouseEventFilter? Filter { get; set; }

    bool IsRunning { get; }
    void Start();
    void Stop();
}

