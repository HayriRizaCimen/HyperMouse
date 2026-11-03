using HyperMouse.Core.Actions;
using HyperMouse.Core.Common;
using HyperMouse.Core.Input;
using HyperMouse.Core.State;

namespace HyperMouse.Core.Engine;

public sealed class HyperMouseEngine : IDisposable
{
    private readonly IMouseHook _hook;
    private readonly IInputSimulator _input;
    private readonly ITimeProvider _timeProvider;
    private readonly StateMachineConfig _config;
    private readonly GestureStateMachine _stateMachine;
    private readonly ButtonStateTracker _buttonTracker = new();
    private readonly Dictionary<(MouseButton Button, CadenceType Cadence, MouseButton Chord, int Layer), IAction> _actionMap = new();

    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private Task? _timerTask;
    private bool _isDisposed;

    public GestureStateMachine StateMachine => _stateMachine;
    public IInputSimulator Input => _input;
    public bool IsPaused => _config.IsPaused;
    public bool IsRunning => _hook.IsRunning;

    public HyperMouseEngine(
        IMouseHook hook,
        IInputSimulator input,
        ITimeProvider? timeProvider = null,
        StateMachineConfig? config = null)
    {
        _hook = hook;
        _input = input;
        _timeProvider = timeProvider ?? SystemTimeProvider.Instance;
        _config = config ?? new StateMachineConfig();

        _stateMachine = new GestureStateMachine(_timeProvider, _config, ResolveRule);
        _stateMachine.TriggerFired += OnTriggerFired;

        _hook.Filter = OnFilterMouseEvent;
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(15));
    }

    public void RegisterAction(MouseButton button, CadenceType cadence, IAction action, MouseButton chord = MouseButton.None, int layer = 0)
    {
        _actionMap[(button, cadence, chord, layer)] = action;
    }

    public void ClearActions()
    {
        _actionMap.Clear();
    }

    public void Start()
    {
        if (IsRunning) return;

        _hook.Start();
        _timerTask = Task.Run(RunTimerLoopAsync);
    }

    public void Stop()
    {
        _config.IsPaused = true;
        _input.ReleaseAllHeld();
        _buttonTracker.Reset();
        _stateMachine.Reset();
        _hook.Stop();
    }

    public void Pause()
    {
        _config.IsPaused = true;
        _input.ReleaseAllHeld();
        _buttonTracker.Reset();
        _stateMachine.Reset();
    }

    public void Resume()
    {
        _config.IsPaused = false;
    }

    private bool OnFilterMouseEvent(RawMouseEvent ev)
    {
        bool suppress = _stateMachine.ProcessEvent(ev);
        
        if (ev.EventType == MouseEventType.Down)
        {
            _buttonTracker.OnButtonDown(ev.Button, suppress);
        }
        else if (ev.EventType == MouseEventType.Up)
        {
            // If DOWN was suppressed, UP MUST also be suppressed for consistency
            if (_buttonTracker.OnButtonUp(ev.Button))
            {
                suppress = true;
            }
        }

        return suppress;
    }

    private void OnTriggerFired(GestureTrigger trigger)
    {
        var key = (trigger.Button, trigger.Cadence, trigger.ChordSecondary, trigger.Layer);
        if (_actionMap.TryGetValue(key, out var action))
        {
            var ctx = new ActionExecutionContext(_input, trigger.Layer);
            _ = Task.Run(async () =>
            {
                try
                {
                    await action.ExecuteAsync(ctx);
                }
                catch
                {
                    // Fail-safe: prevent unhandled exception crash
                }
            });
        }
    }

    private ButtonRuleConfig ResolveRule(MouseButton button, int layer)
    {
        var rule = new ButtonRuleConfig();

        foreach (var key in _actionMap.Keys)
        {
            if (key.Layer != layer) continue;

            if (key.Button == button)
            {
                switch (key.Cadence)
                {
                    case CadenceType.SingleClick:
                        rule.HasSingleClick = true;
                        break;
                    case CadenceType.DoubleClick:
                        rule.HasDoubleClick = true;
                        break;
                    case CadenceType.TripleClick:
                        rule.HasTripleClick = true;
                        break;
                    case CadenceType.LongPress:
                        rule.HasLongPress = true;
                        break;
                    case CadenceType.PressAndHoldDown:
                    case CadenceType.PressAndHoldUp:
                        rule.HasPressAndHold = true;
                        break;
                    case CadenceType.Chord:
                        rule.HasChords = true;
                        break;
                    case CadenceType.LayerModifier:
                        rule.IsLayerModifier = true;
                        break;
                }
            }
        }

        return rule;
    }

    private async Task RunTimerLoopAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                if (!_config.IsPaused)
                {
                    _stateMachine.CheckTimeouts();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();
        _cts.Cancel();
        _cts.Dispose();
        _timer.Dispose();
        _hook.Dispose();
    }
}

