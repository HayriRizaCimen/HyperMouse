using HyperMouse.Core.Common;
using HyperMouse.Core.Engine;
using HyperMouse.Tests.Mocks;

namespace HyperMouse.Tests;

public class GestureStateMachineTests
{
    private readonly MockTimeProvider _time = new();
    private readonly StateMachineConfig _config = new()
    {
        DoubleClickThresholdMs = 250,
        LongPressThresholdMs = 300
    };

    [Fact]
    public void PassthroughButton_ShouldNotBeSuppressedAndFiresNoTriggers()
    {
        var rules = new Dictionary<MouseButton, ButtonRuleConfig>();
        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            rules.TryGetValue(btn, out var r) ? r : new ButtonRuleConfig());

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        var downEv = new RawMouseEvent(MouseButton.Left, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0);
        bool suppressedDown = sm.ProcessEvent(downEv);

        _time.Advance(50);
        var upEv = new RawMouseEvent(MouseButton.Left, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0);
        bool suppressedUp = sm.ProcessEvent(upEv);

        Assert.False(suppressedDown);
        Assert.False(suppressedUp);
        Assert.Empty(triggered);
    }

    [Fact]
    public void SingleClick_WithoutMultiClickConfigured_ShouldFireImmediatelyOnUp()
    {
        var rules = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton1] = new() { HasSingleClick = true }
        };

        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            rules.TryGetValue(btn, out var r) ? r : new ButtonRuleConfig());

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        // Down
        bool downSuppressed = sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        Assert.True(downSuppressed);
        Assert.Empty(triggered);

        // Quick release (50ms)
        _time.Advance(50);
        bool upSuppressed = sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));
        Assert.True(upSuppressed);

        // Because multi-click is not configured, it doesn't need to wait for timeout
        Assert.Single(triggered);
        Assert.Equal(MouseButton.XButton1, triggered[0].Button);
        Assert.Equal(CadenceType.SingleClick, triggered[0].Cadence);
    }

    [Fact]
    public void DoubleClick_WhenConfigured_ShouldTriggerOnSecondClick()
    {
        var rules = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton1] = new() { HasSingleClick = true, HasDoubleClick = true }
        };

        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            rules.TryGetValue(btn, out var r) ? r : new ButtonRuleConfig());

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        // First click (Down + Up)
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        _time.Advance(40);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        // Not yet fired because waiting for 2nd click
        Assert.Empty(triggered);

        // Second click within threshold (100ms later)
        _time.Advance(100);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        _time.Advance(40);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        // Double click fired!
        Assert.Single(triggered);
        Assert.Equal(MouseButton.XButton1, triggered[0].Button);
        Assert.Equal(CadenceType.DoubleClick, triggered[0].Cadence);
    }

    [Fact]
    public void SingleClick_WhenDoubleClickConfigured_ShouldTriggerAfterTimeout()
    {
        var rules = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton1] = new() { HasSingleClick = true, HasDoubleClick = true }
        };

        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            rules.TryGetValue(btn, out var r) ? r : new ButtonRuleConfig());

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        // Click once
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        _time.Advance(50);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        Assert.Empty(triggered);

        // Advance past DoubleClickThreshold (250ms)
        _time.Advance(260);
        sm.CheckTimeouts();

        // Single click fired after timeout!
        Assert.Single(triggered);
        Assert.Equal(MouseButton.XButton1, triggered[0].Button);
        Assert.Equal(CadenceType.SingleClick, triggered[0].Cadence);
    }

    [Fact]
    public void LongPress_ShouldTriggerWhenHeldPastThreshold_AndSuppressSubsequentUp()
    {
        var rules = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton2] = new() { HasSingleClick = true, HasLongPress = true }
        };

        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            rules.TryGetValue(btn, out var r) ? r : new ButtonRuleConfig());

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        // Down
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        
        // 200ms elapsed (threshold is 300ms)
        _time.Advance(200);
        sm.CheckTimeouts();
        Assert.Empty(triggered);

        // Another 150ms elapsed (total 350ms > 300ms)
        _time.Advance(150);
        sm.CheckTimeouts();

        Assert.Single(triggered);
        Assert.Equal(MouseButton.XButton2, triggered[0].Button);
        Assert.Equal(CadenceType.LongPress, triggered[0].Cadence);

        // Button finally released: should NOT fire single click
        _time.Advance(100);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        Assert.Single(triggered); // Still only the long press
    }

    [Fact]
    public void Chording_ShouldTriggerChordAndSuppressBaseButton()
    {
        var rules = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton1] = new() { HasChords = true, HasSingleClick = true }
        };

        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            rules.TryGetValue(btn, out var r) ? r : new ButtonRuleConfig());

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        // Hold XButton1
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));

        // Scroll Wheel Up while XButton1 is held
        bool wheelSuppressed = sm.ProcessEvent(new RawMouseEvent(MouseButton.WheelUp, MouseEventType.Wheel, 0, 0, 120, _time.NowMs, false, 0));

        Assert.True(wheelSuppressed);
        Assert.Single(triggered);
        Assert.Equal(MouseButton.XButton1, triggered[0].Button);
        Assert.Equal(CadenceType.Chord, triggered[0].Cadence);
        Assert.Equal(MouseButton.WheelUp, triggered[0].ChordSecondary);

        // Release XButton1: chord was executed so no single click should fire!
        _time.Advance(50);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        Assert.Single(triggered); // No rogue single click
    }

    [Fact]
    public void LayerModifier_ShouldSwitchLayerWhileHeld_AndResetOnRelease()
    {
        var rulesLayer0 = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton2] = new() { IsLayerModifier = true, TargetLayer = 1 },
            [MouseButton.XButton1] = new() { HasSingleClick = true }
        };

        var rulesLayer1 = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton1] = new() { HasSingleClick = true }
        };

        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            layer == 1 
                ? (rulesLayer1.TryGetValue(btn, out var r1) ? r1 : new ButtonRuleConfig())
                : (rulesLayer0.TryGetValue(btn, out var r0) ? r0 : new ButtonRuleConfig()));

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        // Hold XButton2
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        Assert.Equal(1, sm.ActiveLayer);

        // Click XButton1 while in Layer 1
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        _time.Advance(30);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        var x1Trigger = triggered.Last();
        Assert.Equal(MouseButton.XButton1, x1Trigger.Button);
        Assert.Equal(1, x1Trigger.Layer); // fired under layer 1!

        // Release XButton2
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));
        Assert.Equal(0, sm.ActiveLayer); // returned to layer 0!
    }

    [Fact]
    public void TripleClick_WhenConfigured_ShouldTriggerOnThirdClick()
    {
        var rules = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton1] = new() { HasSingleClick = true, HasDoubleClick = true, HasTripleClick = true }
        };

        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            rules.TryGetValue(btn, out var r) ? r : new ButtonRuleConfig());

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        // Click 1
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        _time.Advance(30);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        // Click 2
        _time.Advance(50);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        _time.Advance(30);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        // Click 3
        _time.Advance(50);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        _time.Advance(30);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));

        Assert.Single(triggered);
        Assert.Equal(MouseButton.XButton1, triggered[0].Button);
        Assert.Equal(CadenceType.TripleClick, triggered[0].Cadence);
    }

    [Fact]
    public void PressAndHold_ShouldTriggerDownAndUpEvents()
    {
        var rules = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton1] = new() { HasPressAndHold = true }
        };

        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            rules.TryGetValue(btn, out var r) ? r : new ButtonRuleConfig());

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        Assert.Single(triggered);
        Assert.Equal(CadenceType.PressAndHoldDown, triggered[0].Cadence);

        _time.Advance(500);
        sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, _time.NowMs, false, 0));
        Assert.Equal(2, triggered.Count);
        Assert.Equal(CadenceType.PressAndHoldUp, triggered[1].Cadence);
    }

    [Fact]
    public void Paused_ShouldPassThroughAllEventsWithoutInterception()
    {
        _config.IsPaused = true;
        var rules = new Dictionary<MouseButton, ButtonRuleConfig>
        {
            [MouseButton.XButton1] = new() { HasSingleClick = true }
        };

        var sm = new GestureStateMachine(_time, _config, (btn, layer) => 
            rules.TryGetValue(btn, out var r) ? r : new ButtonRuleConfig());

        var triggered = new List<GestureTrigger>();
        sm.TriggerFired += triggered.Add;

        bool suppressed = sm.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, _time.NowMs, false, 0));
        Assert.False(suppressed);
        Assert.Empty(triggered);
    }
}
