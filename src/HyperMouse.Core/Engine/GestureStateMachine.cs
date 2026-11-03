using HyperMouse.Core.Common;

namespace HyperMouse.Core.Engine;

public sealed class GestureStateMachine
{
    private sealed class ButtonState
    {
        public bool IsDown;
        public long DownTimestampMs;
        public long LastUpTimestampMs;
        public int PendingClickCount;
        public int PendingLayer;
        public bool LongPressFired;
        public bool ChordExecuted;
        public bool PressAndHoldActive;
        public bool IsLayerModifierHeld;
    }

    private readonly ITimeProvider _timeProvider;
    private readonly StateMachineConfig _config;
    private readonly Func<MouseButton, int, ButtonRuleConfig> _ruleResolver;
    private readonly Dictionary<MouseButton, ButtonState> _buttonStates = new();
    private readonly List<GestureTrigger> _firedTriggers = new();

    public int ActiveLayer { get; private set; } = 0;
    public event Action<GestureTrigger>? TriggerFired;

    public GestureStateMachine(
        ITimeProvider timeProvider,
        StateMachineConfig config,
        Func<MouseButton, int, ButtonRuleConfig> ruleResolver)
    {
        _timeProvider = timeProvider;
        _config = config;
        _ruleResolver = ruleResolver;

        foreach (MouseButton b in Enum.GetValues<MouseButton>())
        {
            _buttonStates[b] = new ButtonState();
        }
    }

    private ButtonState GetState(MouseButton button) => _buttonStates[button];

    /// <summary>
    /// Evaluates a raw mouse event. Returns true if the event should be suppressed from Windows.
    /// </summary>
    public bool ProcessEvent(RawMouseEvent ev)
    {
        if (_config.IsPaused || ev.Button == MouseButton.None)
        {
            return false;
        }

        // Handle wheel / tilt events
        if (ev.EventType is MouseEventType.Wheel or MouseEventType.HWheel)
        {
            return HandleWheelEvent(ev);
        }

        if (ev.EventType == MouseEventType.Down)
        {
            return HandleButtonDown(ev);
        }

        if (ev.EventType == MouseEventType.Up)
        {
            return HandleButtonUp(ev);
        }

        return false;
    }

    private bool HandleWheelEvent(RawMouseEvent ev)
    {
        // 1. Check if any held button has a chord with this wheel action
        foreach (var (heldBtn, state) in _buttonStates)
        {
            if (state.IsDown && !IsWheel(heldBtn))
            {
                var heldRule = _ruleResolver(heldBtn, ActiveLayer);
                if (heldRule.HasChords)
                {
                    state.ChordExecuted = true;
                    FireTrigger(new GestureTrigger(heldBtn, CadenceType.Chord, ev.Button, ActiveLayer));
                    return true; // Suppress wheel
                }
            }
        }

        // 2. Check if the wheel itself has a direct rule
        var wheelRule = _ruleResolver(ev.Button, ActiveLayer);
        if (!wheelRule.IsPassthrough)
        {
            FireTrigger(new GestureTrigger(ev.Button, CadenceType.SingleClick, MouseButton.None, ActiveLayer));
            return true;
        }

        return false;
    }

    private bool HandleButtonDown(RawMouseEvent ev)
    {
        var state = GetState(ev.Button);
        var rule = _ruleResolver(ev.Button, ActiveLayer);

        // 1. Check if another held button chords with this button
        foreach (var (otherBtn, otherState) in _buttonStates)
        {
            if (otherBtn != ev.Button && otherState.IsDown && !IsWheel(otherBtn))
            {
                var otherRule = _ruleResolver(otherBtn, ActiveLayer);
                if (otherRule.HasChords)
                {
                    otherState.ChordExecuted = true;
                    FireTrigger(new GestureTrigger(otherBtn, CadenceType.Chord, ev.Button, ActiveLayer));
                    state.IsDown = true;
                    state.ChordExecuted = true; // prevent this button from firing when released
                    return true;
                }
            }
        }

        if (rule.IsPassthrough)
        {
            return false;
        }

        state.IsDown = true;
        state.DownTimestampMs = ev.TimestampMs;
        state.LongPressFired = false;
        state.ChordExecuted = false;

        if (rule.IsLayerModifier)
        {
            state.IsLayerModifierHeld = true;
            ActiveLayer = rule.TargetLayer;
            FireTrigger(new GestureTrigger(ev.Button, CadenceType.LayerModifier, MouseButton.None, ActiveLayer));
            return true;
        }

        if (rule.HasPressAndHold)
        {
            state.PressAndHoldActive = true;
            FireTrigger(new GestureTrigger(ev.Button, CadenceType.PressAndHoldDown, MouseButton.None, ActiveLayer));
            return true;
        }

        return true;
    }

    private bool HandleButtonUp(RawMouseEvent ev)
    {
        var state = GetState(ev.Button);
        var rule = _ruleResolver(ev.Button, ActiveLayer);

        if (!state.IsDown && rule.IsPassthrough)
        {
            return false;
        }

        state.IsDown = false;
        state.LastUpTimestampMs = ev.TimestampMs;

        if (state.IsLayerModifierHeld)
        {
            state.IsLayerModifierHeld = false;
            ActiveLayer = 0;
            return true;
        }

        if (state.PressAndHoldActive)
        {
            state.PressAndHoldActive = false;
            state.IsLayerModifierHeld = false;
            FireTrigger(new GestureTrigger(ev.Button, CadenceType.PressAndHoldUp, MouseButton.None, ActiveLayer));
            return true;
        }

        if (state.ChordExecuted)
        {
            state.ChordExecuted = false;
            return true;
        }

        if (state.LongPressFired)
        {
            state.LongPressFired = false;
            return true;
        }

        // Quick release without long-press or chord: evaluate single/double/triple click
        if (!rule.RequiresMultiClickBuffering)
        {
            // Direct single click
            FireTrigger(new GestureTrigger(ev.Button, CadenceType.SingleClick, MouseButton.None, ActiveLayer));
            state.PendingClickCount = 0;
            return true;
        }

        // Multi-click cadence
        state.PendingClickCount++;
        state.PendingLayer = ActiveLayer;

        if (rule.HasTripleClick && state.PendingClickCount >= 3)
        {
            FireTrigger(new GestureTrigger(ev.Button, CadenceType.TripleClick, MouseButton.None, state.PendingLayer));
            state.PendingClickCount = 0;
        }
        else if (!rule.HasTripleClick && rule.HasDoubleClick && state.PendingClickCount >= 2)
        {
            FireTrigger(new GestureTrigger(ev.Button, CadenceType.DoubleClick, MouseButton.None, state.PendingLayer));
            state.PendingClickCount = 0;
        }

        return true;
    }

    /// <summary>
    /// Must be invoked periodically (e.g. every 10-20ms) or when simulated time advances
    /// to trigger LongPress holds and resolve buffered single/double click timeouts.
    /// </summary>
    public void CheckTimeouts()
    {
        long now = _timeProvider.NowMs;

        foreach (var (btn, state) in _buttonStates)
        {
            var rule = _ruleResolver(btn, ActiveLayer);

            // 1. Check Long-Press while button is held down
            if (state.IsDown && !state.LongPressFired && !state.ChordExecuted && !state.PressAndHoldActive)
            {
                if (rule.HasLongPress && (now - state.DownTimestampMs >= _config.LongPressThresholdMs))
                {
                    state.LongPressFired = true;
                    FireTrigger(new GestureTrigger(btn, CadenceType.LongPress, MouseButton.None, ActiveLayer));
                }
            }

            // 2. Check pending click cadence timeout while button is UP
            if (!state.IsDown && state.PendingClickCount > 0)
            {
                if (now - state.LastUpTimestampMs >= _config.DoubleClickThresholdMs)
                {
                    int count = state.PendingClickCount;
                    state.PendingClickCount = 0;

                    if (count == 1 && rule.HasSingleClick)
                    {
                        FireTrigger(new GestureTrigger(btn, CadenceType.SingleClick, MouseButton.None, state.PendingLayer));
                    }
                    else if (count == 2 && rule.HasDoubleClick)
                    {
                        FireTrigger(new GestureTrigger(btn, CadenceType.DoubleClick, MouseButton.None, state.PendingLayer));
                    }
                    else if (count >= 3 && rule.HasTripleClick)
                    {
                        FireTrigger(new GestureTrigger(btn, CadenceType.TripleClick, MouseButton.None, state.PendingLayer));
                    }
                }
            }
        }
    }

    public void Reset()
    {
        ActiveLayer = 0;
        foreach (var state in _buttonStates.Values)
        {
            state.IsDown = false;
            state.PendingClickCount = 0;
            state.LongPressFired = false;
            state.ChordExecuted = false;
            state.PressAndHoldActive = false;
            state.IsLayerModifierHeld = false;
        }
    }

    private void FireTrigger(GestureTrigger trigger)
    {
        _firedTriggers.Add(trigger);
        TriggerFired?.Invoke(trigger);
    }

    private static bool IsWheel(MouseButton button) =>
        button is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelTiltLeft or MouseButton.WheelTiltRight;
}
