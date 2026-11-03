using HyperMouse.Core.Common;
using HyperMouse.Core.Input;

namespace HyperMouse.Tests.Mocks;

public sealed class MockMouseHook : IMouseHook
{
    public event Action<RawMouseEvent>? MouseEventReceived;
    public MouseEventFilter? Filter { get; set; }
    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;
    public void Stop() => IsRunning = false;

    public bool SimulateEvent(RawMouseEvent ev)
    {
        bool suppress = Filter?.Invoke(ev) ?? false;
        MouseEventReceived?.Invoke(ev);
        return suppress;
    }

    public void Dispose() => Stop();
}

