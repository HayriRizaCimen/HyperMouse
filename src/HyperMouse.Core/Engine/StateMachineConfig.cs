namespace HyperMouse.Core.Engine;

public sealed class StateMachineConfig
{
    /// <summary>
    /// Maximum time window between clicks to count as double/triple click (default ~280ms).
    /// </summary>
    public int DoubleClickThresholdMs { get; set; } = 280;

    /// <summary>
    /// Time threshold in ms for button hold to count as a Long Press (default ~320ms).
    /// </summary>
    public int LongPressThresholdMs { get; set; } = 320;

    /// <summary>
    /// Global pause / emergency bypass flag.
    /// </summary>
    public bool IsPaused { get; set; } = false;
}

