namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    readonly CooldownVoiceTransitions cooldownVoiceTransitions=new();
    CooldownVoicePlayer? cooldownVoicePlayer;
    string? voiceContext;
    void RefreshCooldownVoice()
    {
        if(capture==null||demoMode||preferences.CooldownVoiceSkills is not {Length:>0}){
            cooldownVoiceTransitions.Reset();cooldownVoicePlayer?.Stop();voiceContext=null;return;
        }
        bool ready;long scene;
        lock(engine.Gate){ready=engine.LiveIdentityReady;scene=engine.LiveSceneRevision;}
        if(!ready){cooldownVoiceTransitions.Reset();cooldownVoicePlayer?.Stop();voiceContext=null;return;}
        string context=$"{scene}:{preferences.CooldownCharacterKey}:{Localization.GameLanguage}";
        if(context!=voiceContext){cooldownVoicePlayer?.Stop();voiceContext=context;}
        var enabled=CooldownSkillChoice.Group(preferences.CooldownVoiceSkills,skills).Select(c=>c.Key).ToHashSet();
        long now=Environment.TickCount64;
        var readings=CooldownSkillChoice.Group(preferences.CooldownSkills,skills).Take(24).Where(c=>enabled.Contains(c.Key)).Select(c=>{
            bool? available;
            if(cooldownTracker.ChargeDisplay(c.Id,now) is {} charge)available=CooldownVoiceTransitions.Ready(null,charge.Count);
            else {var seconds=cooldownTracker.Reading(c.Id,now,skills,preferences.CooldownFallbackSeconds).Seconds;available=CooldownVoiceTransitions.Ready(seconds,null);}
            return new CooldownVoiceReading(c.Key,CooldownVoicePack.Clip(c,Localization.GameLanguage)??"",available);
        });
        foreach(var clip in cooldownVoiceTransitions.Poll(context,readings,now).Where(c=>c.Length>0))
            (cooldownVoicePlayer??=new(CooldownVoiceStillReady)).Enqueue(clip,preferences.CooldownVoiceVolume);
    }
    bool CooldownVoiceStillReady(string clip)
    {
        if(capture==null)return false;
        var enabled=CooldownSkillChoice.Group(preferences.CooldownVoiceSkills??[],skills).Select(c=>c.Key).ToHashSet();
        long now=Environment.TickCount64;
        return CooldownSkillChoice.Group(preferences.CooldownSkills??[],skills).Take(24).Any(c=>enabled.Contains(c.Key)&&
            CooldownVoicePack.Clip(c,Localization.GameLanguage)==clip&&
            (cooldownTracker.ChargeDisplay(c.Id,now) is {} charge?charge.Count>0:cooldownTracker.Reading(c.Id,now,skills,preferences.CooldownFallbackSeconds).Seconds==0));
    }
}
