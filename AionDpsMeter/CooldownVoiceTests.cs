namespace AionDpsMeter.Local;

internal static class CooldownVoiceTests
{
    internal static void Run(Action<bool,string> check)
    {
        var transitions=new CooldownVoiceTransitions();
        check(CooldownVoiceTransitions.Ready(.8,null)==false&&CooldownVoiceTransitions.Ready(0,null)==true&&CooldownVoiceTransitions.Ready(null,null)==null,
            "Voice uses the actual cooldown deadline rather than the optional early visual display");
        check(CooldownVoiceTransitions.Ready(10,1)==true&&CooldownVoiceTransitions.Ready(0,0)==false,
            "Charged skills announce restored usability, not each recharge while a charge is already available");
        CooldownVoiceReading[] Read(bool? ready)=>[new("skill","clip",ready)];
        check(transitions.Poll("a",Read(true),1000).Length==0,"Voice does not announce ready skills at login");
        check(transitions.Poll("a",Read(false),1200).Length==0&&transitions.Poll("a",Read(true),1400).SequenceEqual(new[]{"clip"}),"Voice announces one unavailable-to-ready transition");
        check(transitions.Poll("a",Read(true),1600).Length==0,"Repeated ready updates do not repeat voice warnings");
        transitions.Poll("a",Read(false),1800);transitions.Poll("a",Read(null),2000);
        check(transitions.Poll("a",Read(true),2200).Length==0,"An unknown cooldown breaks voice readiness history");
        transitions.Poll("a",Read(false),2400);
        check(transitions.Poll("b",Read(true),2600).Length==0,"Character and scene changes suppress old readiness transitions");
        transitions.Poll("b",Read(false),2800);
        check(transitions.Poll("b",Read(true),9000).Length==0,"A long polling gap discards stale voice transitions");
        transitions.Poll("b",Read(false),9200);transitions.Poll("b",[],9400);
        check(transitions.Poll("b",Read(true),9600).Length==0,"Removing then enabling a voice skill does not announce an old cooldown");
        transitions.Poll("b",[new("one","same",false),new("two","same",false)],9800);
        check(transitions.Poll("b",[new("one","same",true),new("two","same",true)],10000).Length==1,"Simultaneous matching spoken names are deduplicated");
        var settings=new Preferences{CooldownVoiceSkills=[11010000]};
        var actor=new Actor(1,"First",true,true,100,"Gladiator",1){PersistentId=10};
        CooldownCharacterPresets.Activate(settings,actor);
        CooldownCharacterPresets.Activate(settings,actor with{PersistentId=11,Name="Second"});
        check(settings.CooldownVoiceSkills.Length==0,"New characters do not inherit another character's voice warnings");
        CooldownCharacterPresets.Activate(settings,actor);
        check(settings.CooldownVoiceSkills.SequenceEqual(new[]{11010000}),"Voice choices restore with character cooldown presets");
        var names=new SkillNames();
        check(CooldownVoicePack.Clip(new(11010010,"Custom name"),"en")==CooldownVoicePack.Clip(CooldownSkillChoice.Resolve(11010000,names),"en"),
            "Numeric skill aliases retain their recording when a local name override changes the displayed name");
        var classes=CooldownSkillChoice.Group(names.SearchCodes(""),names);
        check(classes.All(c=>CooldownVoicePack.Clip(c,"en")!=null&&CooldownVoicePack.Clip(c,"de")!=null),"Voice manifest covers every grouped bundled class skill in both game languages");
        var assembly=typeof(CooldownVoicePack).Assembly;
        var resources=assembly.GetManifestResourceNames().ToHashSet();
        var clips=classes.SelectMany(c=>new[]{CooldownVoicePack.Clip(c,"en"),CooldownVoicePack.Clip(c,"de")}).Distinct();
        check(clips.All(c=>c!=null&&resources.Contains("Wingbeat.CooldownVoice."+c+".mp3")),"Every voice manifest entry has a bundled recording");
    }
}
