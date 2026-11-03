using HyperMouse.Core.Common;
using HyperMouse.Core.Input;

namespace HyperMouse.Tests.Mocks;

public sealed class MockInputSimulator : IInputSimulator
{
    public List<string> Invocations { get; } = new();

    public void KeyDown(VirtualKey key) => Invocations.Add($"KeyDown:{key}");
    public void KeyUp(VirtualKey key) => Invocations.Add($"KeyUp:{key}");
    public void KeyPress(VirtualKey key)
    {
        KeyDown(key);
        KeyUp(key);
    }

    public void SendHotkey(VirtualKey[] modifiers, VirtualKey key)
    {
        foreach (var m in modifiers) KeyDown(m);
        KeyPress(key);
        for (int i = modifiers.Length - 1; i >= 0; i--) KeyUp(modifiers[i]);
    }

    public void SendText(string text) => Invocations.Add($"SendText:{text}");

    public void MouseDown(MouseButton button) => Invocations.Add($"MouseDown:{button}");
    public void MouseUp(MouseButton button) => Invocations.Add($"MouseUp:{button}");
    public void MouseClick(MouseButton button)
    {
        MouseDown(button);
        MouseUp(button);
    }

    public void MouseWheel(int delta) => Invocations.Add($"MouseWheel:{delta}");
    public void MouseHWheel(int delta) => Invocations.Add($"MouseHWheel:{delta}");

    public void ReleaseAllHeld() => Invocations.Add("ReleaseAllHeld");
}

