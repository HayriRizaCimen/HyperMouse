using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HyperMouse.Core.Common;
using HyperMouse.Core.Input.Win32;

namespace HyperMouse.Core.Input;

public sealed class Win32InputSimulator : IInputSimulator
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();
    private readonly ConcurrentDictionary<VirtualKey, bool> _heldKeys = new();
    private readonly ConcurrentDictionary<MouseButton, bool> _heldMouseButtons = new();

    public void KeyDown(VirtualKey key)
    {
        SendKeyboardInput((ushort)key, 0, isKeyUp: false);
        _heldKeys[key] = true;
    }

    public void KeyUp(VirtualKey key)
    {
        SendKeyboardInput((ushort)key, 0, isKeyUp: true);
        _heldKeys.TryRemove(key, out _);
    }

    public void KeyPress(VirtualKey key)
    {
        KeyDown(key);
        KeyUp(key);
    }

    public void SendHotkey(VirtualKey[] modifiers, VirtualKey key)
    {
        foreach (var mod in modifiers)
        {
            KeyDown(mod);
        }

        KeyPress(key);

        for (int i = modifiers.Length - 1; i >= 0; i--)
        {
            KeyUp(modifiers[i]);
        }
    }

    public void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var inputs = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            
            // KeyDown Unicode
            inputs[i * 2] = new INPUT
            {
                type = NativeConstants.INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeConstants.KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = NativeConstants.HyperMouse_EXTRA_INFO
                    }
                }
            };

            // KeyUp Unicode
            inputs[i * 2 + 1] = new INPUT
            {
                type = NativeConstants.INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeConstants.KEYEVENTF_UNICODE | NativeConstants.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = NativeConstants.HyperMouse_EXTRA_INFO
                    }
                }
            };
        }

        NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
    }

    public void MouseDown(MouseButton button)
    {
        var (flag, mouseData) = GetDownFlag(button);
        if (flag != 0)
        {
            SendMouseInput(flag, mouseData, 0, 0);
            _heldMouseButtons[button] = true;
        }
    }

    public void MouseUp(MouseButton button)
    {
        var (flag, mouseData) = GetUpFlag(button);
        if (flag != 0)
        {
            SendMouseInput(flag, mouseData, 0, 0);
            _heldMouseButtons.TryRemove(button, out _);
        }
    }

    public void MouseClick(MouseButton button)
    {
        MouseDown(button);
        MouseUp(button);
    }

    public void MouseWheel(int delta)
    {
        SendMouseInput(NativeConstants.MOUSEEVENTF_WHEEL, (uint)delta, 0, 0);
    }

    public void MouseHWheel(int delta)
    {
        SendMouseInput(NativeConstants.MOUSEEVENTF_HWHEEL, (uint)delta, 0, 0);
    }

    public void ReleaseAllHeld()
    {
        foreach (var key in _heldKeys.Keys.ToArray())
        {
            KeyUp(key);
        }

        foreach (var button in _heldMouseButtons.Keys.ToArray())
        {
            MouseUp(button);
        }
    }

    private static (uint Flag, uint MouseData) GetDownFlag(MouseButton button) => button switch
    {
        MouseButton.Left => (NativeConstants.MOUSEEVENTF_LEFTDOWN, 0),
        MouseButton.Right => (NativeConstants.MOUSEEVENTF_RIGHTDOWN, 0),
        MouseButton.Middle => (NativeConstants.MOUSEEVENTF_MIDDLEDOWN, 0),
        MouseButton.XButton1 => (NativeConstants.MOUSEEVENTF_XDOWN, NativeConstants.XBUTTON1),
        MouseButton.XButton2 => (NativeConstants.MOUSEEVENTF_XDOWN, NativeConstants.XBUTTON2),
        _ => (0, 0)
    };

    private static (uint Flag, uint MouseData) GetUpFlag(MouseButton button) => button switch
    {
        MouseButton.Left => (NativeConstants.MOUSEEVENTF_LEFTUP, 0),
        MouseButton.Right => (NativeConstants.MOUSEEVENTF_RIGHTUP, 0),
        MouseButton.Middle => (NativeConstants.MOUSEEVENTF_MIDDLEUP, 0),
        MouseButton.XButton1 => (NativeConstants.MOUSEEVENTF_XUP, NativeConstants.XBUTTON1),
        MouseButton.XButton2 => (NativeConstants.MOUSEEVENTF_XUP, NativeConstants.XBUTTON2),
        _ => (0, 0)
    };

    private static void SendKeyboardInput(ushort vk, ushort scan, bool isKeyUp)
    {
        uint flags = 0;
        if (isKeyUp) flags |= NativeConstants.KEYEVENTF_KEYUP;
        
        // Extended key flag for arrows, nav keys, media keys
        if (vk is >= 0x21 and <= 0x2E or >= 0xA6 and <= 0xB3 or (ushort)VirtualKey.LWin or (ushort)VirtualKey.RWin)
        {
            flags |= NativeConstants.KEYEVENTF_EXTENDEDKEY;
        }

        var input = new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = NativeConstants.HyperMouse_EXTRA_INFO
                }
            }
        };

        NativeMethods.SendInput(1, new[] { input }, InputSize);
    }

    private static void SendMouseInput(uint dwFlags, uint mouseData, int dx, int dy)
    {
        var input = new INPUT
        {
            type = NativeConstants.INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = mouseData,
                    dwFlags = dwFlags,
                    time = 0,
                    dwExtraInfo = NativeConstants.HyperMouse_EXTRA_INFO
                }
            }
        };

        NativeMethods.SendInput(1, new[] { input }, InputSize);
    }
}

