using HyperMouse.Core.Common;

namespace HyperMouse.Core.Engine;

public readonly record struct GestureTrigger(
    MouseButton Button,
    CadenceType Cadence,
    MouseButton ChordSecondary = MouseButton.None,
    int Layer = 0
);

