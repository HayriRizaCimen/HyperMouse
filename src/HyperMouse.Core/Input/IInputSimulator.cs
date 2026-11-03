using HyperMouse.Core.Common;

namespace HyperMouse.Core.Input;

public interface IInputSimulator
{
    void KeyDown(VirtualKey key);
    void KeyUp(VirtualKey key);
    void KeyPress(VirtualKey key);
    void SendHotkey(VirtualKey[] modifiers, VirtualKey key);
    void SendText(string text);
    
    void MouseDown(MouseButton button);
    void MouseUp(MouseButton button);
    void MouseClick(MouseButton button);
    void MouseWheel(int delta);
    void MouseHWheel(int delta);

    void ReleaseAllHeld();
}

