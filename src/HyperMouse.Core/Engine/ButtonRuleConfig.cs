namespace HyperMouse.Core.Engine;

/// <summary>
/// Specifies what cadences are active for a button so the engine knows whether
/// it needs to delay single click to distinguish double-click or long-press.
/// </summary>
public sealed class ButtonRuleConfig
{
    public bool HasSingleClick { get; set; }
    public bool HasDoubleClick { get; set; }
    public bool HasTripleClick { get; set; }
    public bool HasLongPress { get; set; }
    public bool HasPressAndHold { get; set; }
    public bool HasChords { get; set; }
    public bool IsLayerModifier { get; set; }
    public int TargetLayer { get; set; } = 1;

    /// <summary>
    /// If true, the event is allowed to pass to Windows without any interception or delay.
    /// </summary>
    public bool IsPassthrough => 
        !HasSingleClick && 
        !HasDoubleClick && 
        !HasTripleClick && 
        !HasLongPress && 
        !HasPressAndHold && 
        !HasChords && 
        !IsLayerModifier;

    /// <summary>
    /// Whether single click evaluation must be buffered/delayed to check for multi-clicks.
    /// </summary>
    public bool RequiresMultiClickBuffering => HasDoubleClick || HasTripleClick;

    /// <summary>
    /// Whether button down evaluation must be checked for long press.
    /// </summary>
    public bool RequiresHoldDetection => HasLongPress || HasChords || IsLayerModifier || HasPressAndHold;
}

