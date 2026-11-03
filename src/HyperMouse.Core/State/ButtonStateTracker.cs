using System.Collections.Concurrent;
using HyperMouse.Core.Common;

namespace HyperMouse.Core.State;

public sealed class ButtonStateTracker
{
    private readonly ConcurrentDictionary<MouseButton, bool> _suppressedButtons = new();
    private readonly ConcurrentDictionary<MouseButton, bool> _physicalDown = new();

    public void OnButtonDown(MouseButton button, bool suppressed)
    {
        _physicalDown[button] = true;
        _suppressedButtons[button] = suppressed;
    }

    public bool OnButtonUp(MouseButton button)
    {
        _physicalDown.TryRemove(button, out _);
        _suppressedButtons.TryRemove(button, out bool wasSuppressed);
        return wasSuppressed;
    }

    public bool IsPhysicallyDown(MouseButton button)
    {
        return _physicalDown.TryGetValue(button, out bool down) && down;
    }

    public bool WasSuppressed(MouseButton button)
    {
        return _suppressedButtons.TryGetValue(button, out bool suppressed) && suppressed;
    }

    public IReadOnlyCollection<MouseButton> GetCurrentlyPressedButtons()
    {
        return _physicalDown.Keys.ToArray();
    }

    public void Reset()
    {
        _suppressedButtons.Clear();
        _physicalDown.Clear();
    }
}

