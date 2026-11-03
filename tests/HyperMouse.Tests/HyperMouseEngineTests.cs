using HyperMouse.Core.Actions;
using HyperMouse.Core.Common;
using HyperMouse.Core.Engine;
using HyperMouse.Core.Input;
using HyperMouse.Tests.Mocks;

namespace HyperMouse.Tests;

public class HyperMouseEngineTests
{
    private readonly MockMouseHook _hook = new();
    private readonly MockInputSimulator _input = new();
    private readonly MockTimeProvider _time = new();
    private readonly StateMachineConfig _config = new()
    {
        DoubleClickThresholdMs = 200,
        LongPressThresholdMs = 300
    };

    [Fact]
    public async Task Engine_DispatchesConfiguredAction_OnSingleClick()
    {
        using var engine = new HyperMouseEngine(_hook, _input, _time, _config);

        // Map XButton1 SingleClick -> Ctrl+C
        var hotkeyAction = new HotkeyAction(new[] { VirtualKey.Control }, VirtualKey.C);
        engine.RegisterAction(MouseButton.XButton1, CadenceType.SingleClick, hotkeyAction);

        // Click XButton1 (Down + Up quickly)
        bool downSuppressed = _hook.SimulateEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        _time.Advance(40);
        bool upSuppressed = _hook.SimulateEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        Assert.True(downSuppressed);
        Assert.True(upSuppressed);

        // Allow async action invocation to execute
        await Task.Delay(50);

        Assert.Contains("KeyDown:Control", _input.Invocations);
        Assert.Contains("KeyDown:C", _input.Invocations);
        Assert.Contains("KeyUp:C", _input.Invocations);
        Assert.Contains("KeyUp:Control", _input.Invocations);
    }

    [Fact]
    public async Task Engine_DispatchesLongPressAction_WhenHeld()
    {
        using var engine = new HyperMouseEngine(_hook, _input, _time, _config);

        // Map XButton2 LongPress -> Media Volume Up
        var volumeAction = new MediaKeyAction(MediaCommand.VolumeUp);
        engine.RegisterAction(MouseButton.XButton2, CadenceType.LongPress, volumeAction);

        // Hold XButton2
        _hook.SimulateEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        
        _time.Advance(350); // > 300ms
        engine.StateMachine.CheckTimeouts();

        await Task.Delay(50);

        Assert.Contains("KeyDown:VolumeUp", _input.Invocations);
        Assert.Contains("KeyUp:VolumeUp", _input.Invocations);
    }
}

