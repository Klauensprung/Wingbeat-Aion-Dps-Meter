using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace AionDpsMeter.Local;

internal sealed partial record CooldownSkillChoice(int Id, string Name)
{
    // Cache by immutable catalogue instance: the overlay reads these every 100 ms,
    // and importing a new catalogue must not reuse old name associations.
    static readonly ConditionalWeakTable<SkillNames, ConcurrentDictionary<int, CooldownSkillChoice>> Cache = new();
    internal string Key { get; } = $"{Family(Id)}:{Name.ToUpperInvariant()}";
    public override string ToString() => Name;
    internal bool Matches(CooldownSkillChoice other) => Key == other.Key;
    static int Family(int id) => id is >= 11000000 and < 20000000 ? id / 10000 * 10000 : id;

    internal static CooldownSkillChoice Resolve(int id, SkillNames names) =>
        Cache.GetOrCreateValue(names).GetOrAdd(id, static (code, catalogue) => {
            var actual = catalogue.Resolve(code);
            int family = Family(code);
            if (family != code) {
                var parent = catalogue.Resolve(family);
                if (parent.MatchedCode == family &&
                    (string.Equals(actual.Name, parent.Name, StringComparison.OrdinalIgnoreCase) || IsChargeTier(actual.Name, parent.Name)))
                    return new(family, parent.Name);
            }
            return new(code, actual.Name);
        }, names);

    static bool IsChargeTier(string name, string parent)
    {
        // Some stance families label only the base with a tag, e.g. [Rampage].
        // Keep that tag in the grouped name so the normal stance stays separate.
        int tag = parent.LastIndexOf(" [", StringComparison.Ordinal);
        string stem = tag > 0 && parent.EndsWith(']') ? parent[..tag] : parent;
        if (!name.StartsWith(stem + " ", StringComparison.OrdinalIgnoreCase)) return false;
        string suffix = name[stem.Length..].Trim();
        if (suffix.StartsWith('-') || suffix.StartsWith('\u2013') || suffix.StartsWith('\u2014')) suffix = suffix[1..].TrimStart();
        return ChargeSuffix().IsMatch(suffix);
    }

    [GeneratedRegex(@"^(?:MAX|(?:(?:Level|Lv\.?|Lvl\.?)\s*)?[1-9][0-9]?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChargeSuffix();

    internal static CooldownSkillChoice[] Group(IEnumerable<int> ids, SkillNames names) => ids.Where(id => id > 0)
        .Select(id => Resolve(id, names)).DistinctBy(c => c.Key).ToArray();

    internal static CooldownSkillChoice[] Search(IEnumerable<int> observed, SkillNames names, string query)
    {
        string text = query.Trim();
        var ids = observed.Concat(names.SearchCodes(text));
        if (int.TryParse(text, out int exact) && exact > 0) ids = new[] { exact }.Concat(ids);
        // Filter original aliases before grouping: searching "Bolt MAX" or its ID
        // must still return the single Bolt choice.
        return Group(ids.Distinct().Where(id => text.Length == 0 || names.GetSkillName(id).Contains(text, StringComparison.OrdinalIgnoreCase) ||
            id.ToString().Contains(text)), names).Take(150).ToArray();
    }
}
