using HyperMouse.Core.Engine;

namespace HyperMouse.Core.Config;

public sealed record RuleConflict(MappingRuleDto Existing, string Reason);

/// <summary>
/// Guarantees that a physical trigger (button + cadence + chord partner + layer) is bound to at most
/// one action. Binding a second action to the same trigger — whether different or identical — is
/// ambiguous for the engine (only one can win) and is therefore rejected.
/// </summary>
public static class RuleConflictChecker
{
    /// <summary>Cadences that compete for the same physical "hold" gesture on one button.</summary>
    private static readonly CadenceType[] HoldCadences = { CadenceType.LongPress, CadenceType.PressAndHoldDown };

    public static RuleConflict? FindConflict(IEnumerable<MappingRuleDto> rules, MappingRuleDto candidate, MappingRuleDto? ignore = null)
    {
        foreach (var r in rules)
        {
            if (ReferenceEquals(r, ignore) || ReferenceEquals(r, candidate)) continue;
            if (r.Button != candidate.Button || r.Layer != candidate.Layer) continue;

            if (r.Cadence == candidate.Cadence &&
                (candidate.Cadence != CadenceType.Chord || r.ChordSecondary == candidate.ChordSecondary))
            {
                bool sameAction = string.Equals(r.ActionType, candidate.ActionType, StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(r.Parameter.Trim(), candidate.Parameter.Trim(), StringComparison.OrdinalIgnoreCase);
                return new RuleConflict(r, sameAction
                    ? "Bu tuş ve ritimde aynı eylem zaten var."
                    : "Bu tuş ve ritim zaten kullanılıyor.");
            }

            if (r.Cadence != candidate.Cadence &&
                HoldCadences.Contains(r.Cadence) && HoldCadences.Contains(candidate.Cadence))
            {
                return new RuleConflict(r, "Uzun Basma ve Bas-Çek aynı tuşta birlikte olamaz.");
            }
        }
        return null;
    }

    /// <summary>Removes rules that conflict with an earlier rule (first one wins). Returns removed rules.</summary>
    public static List<MappingRuleDto> RemoveConflicts(List<MappingRuleDto> rules)
    {
        var kept = new List<MappingRuleDto>();
        var removed = new List<MappingRuleDto>();
        foreach (var r in rules)
        {
            if (FindConflict(kept, r) == null) kept.Add(r);
            else removed.Add(r);
        }
        rules.Clear();
        rules.AddRange(kept);
        return removed;
    }
}
