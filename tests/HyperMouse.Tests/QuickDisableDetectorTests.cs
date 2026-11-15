using HyperMouse.Core.Common;
using HyperMouse.Core.Engine;
using Xunit;

namespace HyperMouse.Tests;

public class QuickDisableDetectorTests
{
    [Fact]
    public void Hold_TriggersToggle_WhenHeldForDuration()
    {
        var detector = new QuickDisableDetector
        {
            TargetButton = MouseButton.XButton2,
            Cadence = QuickDisableCadence.Hold,
            HoldDurationMs = 1000
        };

        bool triggered = false;
        detector.ToggleRequested += () => triggered = true;

        // Button down at t=100
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Down, 0, 0, 0, 100, false, 0));

        // Tick at t=600 (not yet 1000ms)
        detector.Tick(600);
        Assert.False(triggered);

        // Tick at t=1100 (1000ms reached)
        detector.Tick(1100);
        Assert.True(triggered);

        // Releasing after hold should be suppressed
        bool upSuppressed = detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Up, 0, 0, 0, 1150, false, 0));
        Assert.True(upSuppressed);
    }

    [Fact]
    public void Hold_DoesNotTrigger_WhenReleasedEarly()
    {
        var detector = new QuickDisableDetector
        {
            TargetButton = MouseButton.XButton2,
            Cadence = QuickDisableCadence.Hold,
            HoldDurationMs = 1000
        };

        bool triggered = false;
        detector.ToggleRequested += () => triggered = true;

        // Button down at t=100
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Down, 0, 0, 0, 100, false, 0));

        // Released at t=400 (early)
        bool upSuppressed = detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Up, 0, 0, 0, 400, false, 0));
        Assert.False(upSuppressed);

        // Tick at t=1200
        detector.Tick(1200);
        Assert.False(triggered);
    }

    [Fact]
    public void TripleClick_TriggersToggle_WhenClickedThreeTimesQuickly()
    {
        var detector = new QuickDisableDetector
        {
            TargetButton = MouseButton.XButton1,
            Cadence = QuickDisableCadence.TripleClick,
            MultiClickWindowMs = 400
        };

        int triggerCount = 0;
        detector.ToggleRequested += () => triggerCount++;

        // Click 1 (down 100, up 150)
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, 100, false, 0));
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, 150, false, 0));
        Assert.Equal(0, triggerCount);

        // Click 2 (down 250, up 300)
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, 250, false, 0));
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, 300, false, 0));
        Assert.Equal(0, triggerCount);

        // Click 3 (down 400, up 450)
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, 400, false, 0));
        bool suppressed = detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, 450, false, 0));

        Assert.Equal(1, triggerCount);
        Assert.True(suppressed);
    }

    [Fact]
    public void TripleClick_DoesNotTrigger_WhenSlowClicks()
    {
        var detector = new QuickDisableDetector
        {
            TargetButton = MouseButton.XButton1,
            Cadence = QuickDisableCadence.TripleClick,
            MultiClickWindowMs = 300
        };

        int triggerCount = 0;
        detector.ToggleRequested += () => triggerCount++;

        // Click 1 at 100
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, 100, false, 0));
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, 150, false, 0));

        // Wait 500ms > 300ms window
        detector.Tick(650);

        // Click 2 at 700
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, 700, false, 0));
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, 750, false, 0));

        // Click 3 at 900
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Down, 0, 0, 0, 900, false, 0));
        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton1, MouseEventType.Up, 0, 0, 0, 950, false, 0));

        Assert.Equal(0, triggerCount); // Only 2 consecutive clicks within window, not 3
    }

    [Fact]
    public void Disabled_DoesNotTriggerToggle()
    {
        var detector = new QuickDisableDetector
        {
            IsEnabled = false,
            TargetButton = MouseButton.XButton2,
            Cadence = QuickDisableCadence.Hold,
            HoldDurationMs = 500
        };

        bool triggered = false;
        detector.ToggleRequested += () => triggered = true;

        detector.ProcessEvent(new RawMouseEvent(MouseButton.XButton2, MouseEventType.Down, 0, 0, 0, 100, false, 0));
        detector.Tick(700);

        Assert.False(triggered);
    }
}
