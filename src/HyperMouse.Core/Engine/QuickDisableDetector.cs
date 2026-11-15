using HyperMouse.Core.Common;

namespace HyperMouse.Core.Engine;

public enum QuickDisableCadence
{
    Hold,         // Basılı tutma (Hold - varsayılan 1000ms)
    TripleClick,  // 3 kez üst üste tık
    QuadClick,    // 4 kez üst üste tık
    DoubleClick   // Çift tık
}

public sealed class QuickDisableDetector
{
    public bool IsEnabled { get; set; } = true;
    public MouseButton TargetButton { get; set; } = MouseButton.XButton2;
    public QuickDisableCadence Cadence { get; set; } = QuickDisableCadence.Hold;
    public int HoldDurationMs { get; set; } = 1000;
    public int MultiClickWindowMs { get; set; } = 400;

    public event Action? ToggleRequested;

    private bool _isDown;
    private long _downTime;
    private int _clickCount;
    private long _lastUpTime;
    private bool _holdFired;

    /// <summary>
    /// Processes a raw mouse event. Returns true if this event is consumed by the quick-disable gesture.
    /// </summary>
    public bool ProcessEvent(RawMouseEvent ev)
    {
        if (!IsEnabled || ev.Button != TargetButton)
        {
            if (ev.EventType == MouseEventType.Down && ev.Button != TargetButton && _clickCount > 0)
            {
                _clickCount = 0;
            }
            return false;
        }

        if (ev.EventType == MouseEventType.Down)
        {
            _isDown = true;
            _downTime = ev.TimestampMs;
            _holdFired = false;
            return false;
        }

        if (ev.EventType == MouseEventType.Up)
        {
            if (!_isDown) return false;
            _isDown = false;

            if (_holdFired)
            {
                // Suppress release of the held button after hold toggle was executed
                _holdFired = false;
                return true;
            }

            if (Cadence != QuickDisableCadence.Hold)
            {
                long now = ev.TimestampMs;
                if (now - _lastUpTime <= MultiClickWindowMs)
                {
                    _clickCount++;
                }
                else
                {
                    _clickCount = 1;
                }
                _lastUpTime = now;

                int required = Cadence switch
                {
                    QuickDisableCadence.DoubleClick => 2,
                    QuickDisableCadence.TripleClick => 3,
                    QuickDisableCadence.QuadClick => 4,
                    _ => 0
                };

                if (required > 0 && _clickCount >= required)
                {
                    _clickCount = 0;
                    ToggleRequested?.Invoke();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks periodic timeouts such as hold duration and multi-click expiration.
    /// </summary>
    public void Tick(long nowMs)
    {
        if (!IsEnabled) return;

        if (Cadence == QuickDisableCadence.Hold && _isDown && !_holdFired)
        {
            if (nowMs - _downTime >= HoldDurationMs)
            {
                _holdFired = true;
                ToggleRequested?.Invoke();
            }
        }

        if (_clickCount > 0 && (nowMs - _lastUpTime > MultiClickWindowMs))
        {
            _clickCount = 0;
        }
    }

    public void Reset()
    {
        _isDown = false;
        _holdFired = false;
        _clickCount = 0;
    }
}
