using HyperMouse.Core.Common;
using HyperMouse.Core.Config;
using HyperMouse.Core.Engine;

namespace HyperMouse.Tests;

public class RuleConflictCheckerTests
{
    private static MappingRuleDto Rule(MouseButton b, CadenceType c, string param = "Ctrl+C", MouseButton chord = MouseButton.None, int layer = 0) =>
        new() { Button = b, Cadence = c, ActionType = "Hotkey", Parameter = param, ChordSecondary = chord, Layer = layer };

    [Fact]
    public void SameButtonSameCadence_DifferentAction_IsConflict()
    {
        var rules = new List<MappingRuleDto> { Rule(MouseButton.XButton1, CadenceType.SingleClick, "Ctrl+C") };
        Assert.NotNull(RuleConflictChecker.FindConflict(rules, Rule(MouseButton.XButton1, CadenceType.SingleClick, "Ctrl+V")));
    }

    [Fact]
    public void SameButtonSameCadence_SameAction_IsConflict()
    {
        var rules = new List<MappingRuleDto> { Rule(MouseButton.XButton1, CadenceType.SingleClick) };
        var c = RuleConflictChecker.FindConflict(rules, Rule(MouseButton.XButton1, CadenceType.SingleClick));
        Assert.NotNull(c);
        Assert.Contains("aynı eylem", c!.Reason);
    }

    [Fact]
    public void DifferentCadenceOrButtonOrLayer_IsAllowed()
    {
        var rules = new List<MappingRuleDto> { Rule(MouseButton.XButton1, CadenceType.SingleClick) };
        Assert.Null(RuleConflictChecker.FindConflict(rules, Rule(MouseButton.XButton1, CadenceType.DoubleClick)));
        Assert.Null(RuleConflictChecker.FindConflict(rules, Rule(MouseButton.XButton2, CadenceType.SingleClick)));
        Assert.Null(RuleConflictChecker.FindConflict(rules, Rule(MouseButton.XButton1, CadenceType.SingleClick, layer: 1)));
    }

    [Fact]
    public void Chord_DependsOnSecondary()
    {
        var rules = new List<MappingRuleDto> { Rule(MouseButton.XButton2, CadenceType.Chord, chord: MouseButton.WheelUp) };
        Assert.Null(RuleConflictChecker.FindConflict(rules, Rule(MouseButton.XButton2, CadenceType.Chord, chord: MouseButton.WheelDown)));
        Assert.NotNull(RuleConflictChecker.FindConflict(rules, Rule(MouseButton.XButton2, CadenceType.Chord, chord: MouseButton.WheelUp)));
    }

    [Fact]
    public void LongPress_And_PressAndHold_Conflict()
    {
        var rules = new List<MappingRuleDto> { Rule(MouseButton.Middle, CadenceType.LongPress) };
        Assert.NotNull(RuleConflictChecker.FindConflict(rules, Rule(MouseButton.Middle, CadenceType.PressAndHoldDown)));
    }

    [Fact]
    public void EditingRule_IsIgnored()
    {
        var existing = Rule(MouseButton.XButton1, CadenceType.SingleClick);
        var rules = new List<MappingRuleDto> { existing };
        Assert.Null(RuleConflictChecker.FindConflict(rules, Rule(MouseButton.XButton1, CadenceType.SingleClick, "Ctrl+X"), ignore: existing));
    }

    [Fact]
    public void RemoveConflicts_KeepsFirst()
    {
        var first = Rule(MouseButton.XButton1, CadenceType.SingleClick, "Ctrl+C");
        var rules = new List<MappingRuleDto> { first, Rule(MouseButton.XButton1, CadenceType.SingleClick, "Ctrl+V"), Rule(MouseButton.XButton1, CadenceType.DoubleClick) };
        var removed = RuleConflictChecker.RemoveConflicts(rules);
        Assert.Single(removed);
        Assert.Equal(2, rules.Count);
        Assert.Same(first, rules[0]);
    }
}
