using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDpsMeter.Local;

internal static class LocalizationTests
{
    internal static void Run(Action<bool,string> check)
    {
        check(Localization.Resolve("auto",CultureInfo.GetCultureInfo("de-AT"))=="de"&&
            Localization.Resolve("auto",CultureInfo.GetCultureInfo("fr-FR"))=="en"&&
            Localization.Resolve("en",CultureInfo.GetCultureInfo("de-DE"))=="en"&&
            Localization.Resolve("de",CultureInfo.GetCultureInfo("en-US"))=="de",
            "Windows language detection supports German variants, English fallback and explicit overrides");
        using var stream=typeof(Localization).Assembly.GetManifestResourceStream("Wingbeat.Language.de.json")!;
        var catalogue=JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
        bool valid=true;
        foreach(var (source,target) in catalogue){
            string[] Tokens(string s)=>Regex.Matches(s,@"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}").Select(m=>m.Value).Order().ToArray();
            valid&=Tokens(source).SequenceEqual(Tokens(target));
            if(Tokens(source).Length>0)try{CompositeFormat.Parse(target);}catch(FormatException){valid=false;}
        }
        check(catalogue.Count>=800&&valid,"Bundled German interface translations retain every formatting placeholder");
        check(catalogue["Farming overlay"]=="Sammel-Overlay"&&catalogue["Pet farming"]=="Begleiter farmen"&&
            catalogue["Boss alerts"]=="Bosswarnungen"&&catalogue["Remaining uptime"]=="Verbleibende Buff-Dauer"&&
            catalogue["ID"]=="ID"&&catalogue["ddd dd MMM HH:mm"]=="ddd dd MMM HH:mm",
            "German gaming terms and date format tokens retain their intended meaning");
        check(catalogue.Values.All(v=>!new[]{"Landwirtschaft","landwirtschaft","Vorladung","Besetzungslücke","Begleitere","Schauspieler"}.Any(v.Contains)),
            "German catalogue excludes mistranslated farming, summon, cast and actor terminology");
        check(catalogue["Interface language"]=="Oberflächensprache"&&catalogue["Game language"]=="Spielsprache"&&catalogue["Wingbeat"]=="Wingbeat",
            "Interface and game language settings have independent labels and preserve the product name");
        var names=new SkillNames();var original=names.Resolve(17120001);
        check(LocalizedSkillNames.Name(17120001,original.Name,"de")=="Glanz der Genesung"&&
            names.Resolve(17120001)==original&&LocalizedSkillNames.Name(17120001,original.Name,"en")==original.Name,
            "German skill names resolve by numeric family without changing captured IDs or English matching names");
        check(LocalizedSkillNames.Name(999,"Fallback","de")=="Fallback"&&
            LocalizedSkillNames.Matches(18190000,"Unbesiegbarkeit"),
            "Missing German skills retain their original name and German searches find known skill families");
    }
}
