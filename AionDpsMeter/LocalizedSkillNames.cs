using System.Collections.Frozen;
using System.Text.Json;

namespace AionDpsMeter.Local;

// A separate display catalogue: matching, grouping and capture use the original
// SkillNames.Resolve result and numeric IDs, irrespective of either UI language.
internal static class LocalizedSkillNames
{
    static readonly FrozenDictionary<int,string> german=Load();
    static FrozenDictionary<int,string> Load(){
        using var stream=typeof(LocalizedSkillNames).Assembly.GetManifestResourceStream("Wingbeat.SkillNames.de.json");
        return (stream==null?new Dictionary<int,string>():SkillNames.Read(stream,"German skill names")).ToFrozenDictionary();
    }
    internal static string Name(int code,string fallback,string language)=>language=="de"&&german.TryGetValue(code,out var exact)?exact:
        language=="de"&&code is >=11000000 and <20000000&&german.TryGetValue(code/10000*10000,out var family)?family:fallback;
    internal static string Display(int code,string fallback)=>Name(code,fallback,Localization.GameLanguage);
    internal static bool Matches(int code,string query)=>Name(code,"","de").Contains(query,StringComparison.OrdinalIgnoreCase);
}
