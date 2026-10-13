using PocsagDecoder.Core.Models;

namespace PocsagDecoder.Core.Filtering;

/// <summary>Wertet die Regeln der Reihe nach aus und liefert alle Treffer.</summary>
public static class RuleEngine
{
    public static IReadOnlyList<FilterRule> Match(IEnumerable<FilterRule> rules, PocsagMessage message)
    {
        var hits = new List<FilterRule>();
        foreach (var rule in rules)
        {
            if (!rule.IsMatch(message)) continue;
            hits.Add(rule);
            if (rule.StopProcessing) break;
        }
        return hits;
    }
}
