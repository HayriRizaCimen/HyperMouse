using HyperMouse.Core.Common;
using HyperMouse.Core.State;

namespace HyperMouse.Tests;

public class ButtonStateTrackerTests
{
    [Fact]
    public void SuppressionTracking_MatchesDownAndUp()
    {
        var tracker = new ButtonStateTracker();

        tracker.OnButtonDown(MouseButton.XButton1, suppressed: true);
        Assert.True(tracker.IsPhysicallyDown(MouseButton.XButton1));
        Assert.True(tracker.WasSuppressed(MouseButton.XButton1));

        bool wasSuppressedOnUp = tracker.OnButtonUp(MouseButton.XButton1);
        Assert.True(wasSuppressedOnUp);
        Assert.False(tracker.IsPhysicallyDown(MouseButton.XButton1));
    }

    [Fact]
    public void Reset_ClearsAllTracking()
    {
        var tracker = new ButtonStateTracker();
        tracker.OnButtonDown(MouseButton.XButton1, suppressed: true);
        tracker.OnButtonDown(MouseButton.XButton2, suppressed: false);

        Assert.Equal(2, tracker.GetCurrentlyPressedButtons().Count);

        tracker.Reset();

        Assert.Empty(tracker.GetCurrentlyPressedButtons());
        Assert.False(tracker.IsPhysicallyDown(MouseButton.XButton1));
    }
}

