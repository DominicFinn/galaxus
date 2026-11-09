using System.Text.RegularExpressions;

namespace Galaxus.Core.Text;

/// <summary>
/// A phrase grammar loaded from data. Rules map a symbol to a list of alternatives.
///
/// Syntax inside an alternative:
///   #symbol#            expand another rule in the same grammar
///   #grammar:symbol#    expand a rule in another grammar (for shared word lists)
///   #symbol.cap#        modifiers: cap, upper, lower, a (adds "a"/"an")
///   {variable}          a value supplied by the game, such as {cargo} or {system}
/// </summary>
public sealed class GrammarDef
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Variables the game must supply when expanding this grammar.</summary>
    public List<string> Variables { get; set; } = [];
    public Dictionary<string, List<string>> Rules { get; set; } = [];
}

public sealed partial class GrammarLibrary
{
    private const int MaxDepth = 24;
    private readonly Dictionary<string, GrammarDef> _grammars = new(StringComparer.Ordinal);

    [GeneratedRegex(@"#([^#\s]+)#")]
    private static partial Regex SymbolPattern();

    [GeneratedRegex(@"\{([a-zA-Z0-9_]+)\}")]
    private static partial Regex VariablePattern();

    public void Add(GrammarDef grammar) => _grammars[grammar.Id] = grammar;

    public bool Contains(string grammarId) => _grammars.ContainsKey(grammarId);

    public IEnumerable<GrammarDef> All => _grammars.Values;

    public string Expand(string grammarId, string symbol, Rng rng, IReadOnlyDictionary<string, string>? variables = null)
    {
        string text = ExpandSymbol(grammarId, symbol, rng, 0);
        return VariablePattern().Replace(text, m =>
            variables != null && variables.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
    }

    private string ExpandSymbol(string grammarId, string symbol, Rng rng, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidOperationException($"Grammar '{grammarId}' recursed too deeply at '{symbol}'.");
        if (!_grammars.TryGetValue(grammarId, out var grammar))
            throw new KeyNotFoundException($"Unknown grammar '{grammarId}'.");
        if (!grammar.Rules.TryGetValue(symbol, out var options) || options.Count == 0)
            throw new KeyNotFoundException($"Grammar '{grammarId}' has no rule '{symbol}'.");

        string chosen = rng.Pick(options);
        return SymbolPattern().Replace(chosen, m =>
        {
            var (refGrammar, refSymbol, modifiers) = ParseReference(grammarId, m.Groups[1].Value);
            string expanded = ExpandSymbol(refGrammar, refSymbol, rng, depth + 1);
            foreach (var modifier in modifiers)
                expanded = ApplyModifier(expanded, modifier);
            return expanded;
        });
    }

    internal static (string Grammar, string Symbol, string[] Modifiers) ParseReference(string currentGrammar, string reference)
    {
        var parts = reference.Split('.');
        string target = parts[0];
        string[] modifiers = parts[1..];
        int colon = target.IndexOf(':');
        return colon >= 0
            ? (target[..colon], target[(colon + 1)..], modifiers)
            : (currentGrammar, target, modifiers);
    }

    private static readonly HashSet<string> KnownModifiers = ["cap", "upper", "lower", "a"];

    private static string ApplyModifier(string text, string modifier) => modifier switch
    {
        "cap" => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..],
        "upper" => text.ToUpperInvariant(),
        "lower" => text.ToLowerInvariant(),
        "a" => ("aeiouAEIOU".Contains(text.Length > 0 ? text[0] : 'x') ? "an " : "a ") + text,
        _ => throw new InvalidOperationException($"Unknown grammar modifier '{modifier}'."),
    };

    /// <summary>Checks every reference, modifier and variable. Returns a list of problems.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        foreach (var grammar in _grammars.Values)
        {
            if (grammar.Rules.Count == 0)
                problems.Add($"Grammar '{grammar.Id}' has no rules.");

            foreach (var (symbol, options) in grammar.Rules)
            {
                if (options.Count == 0)
                    problems.Add($"Grammar '{grammar.Id}' rule '{symbol}' has no alternatives.");

                foreach (var option in options)
                {
                    foreach (Match m in SymbolPattern().Matches(option))
                    {
                        var (refGrammar, refSymbol, modifiers) = ParseReference(grammar.Id, m.Groups[1].Value);
                        if (!_grammars.TryGetValue(refGrammar, out var target))
                            problems.Add($"Grammar '{grammar.Id}' rule '{symbol}' references unknown grammar '{refGrammar}'.");
                        else if (!target.Rules.ContainsKey(refSymbol))
                            problems.Add($"Grammar '{grammar.Id}' rule '{symbol}' references missing rule '{refGrammar}:{refSymbol}'.");
                        foreach (var modifier in modifiers.Where(x => !KnownModifiers.Contains(x)))
                            problems.Add($"Grammar '{grammar.Id}' rule '{symbol}' uses unknown modifier '{modifier}'.");
                    }

                    if (option.Count(c => c == '#') % 2 != 0)
                        problems.Add($"Grammar '{grammar.Id}' rule '{symbol}' has an unpaired '#': \"{option}\"");

                    foreach (Match m in VariablePattern().Matches(option))
                    {
                        if (!grammar.Variables.Contains(m.Groups[1].Value))
                            problems.Add($"Grammar '{grammar.Id}' rule '{symbol}' uses undeclared variable '{{{m.Groups[1].Value}}}'.");
                    }
                }
            }
        }
        return problems;
    }
}
