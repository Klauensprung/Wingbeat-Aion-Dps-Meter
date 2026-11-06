using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal static class CooldownVoicePack
{
    sealed record Pack(Dictionary<string,Dictionary<string,string>> Clips,string Revision="1",Dictionary<int,string>? Ids=null);
    static readonly Lazy<Pack> Data=new(()=>{
        using var stream=typeof(CooldownVoicePack).Assembly.GetManifestResourceStream("Wingbeat.CooldownVoice.json");
        return stream==null?new(new()):JsonSerializer.Deserialize<Pack>(stream)??new(new());
    });
    internal static string? Clip(CooldownSkillChoice skill,string language)
    {
        var data=Data.Value;
        string key=data.Ids?.GetValueOrDefault(skill.Id)??skill.Key;
        if(!data.Clips.TryGetValue(key,out var languages))return null;
        return languages.GetValueOrDefault(language=="de"?"de":"en");
    }
    internal static bool Available(CooldownSkillChoice skill)=>Clip(skill,Localization.GameLanguage)!=null;
    internal static string? Extract(string clip)
    {
        if(clip.Length!=20||clip.Any(c=>!char.IsAsciiHexDigit(c)))return null;
        string revision=Data.Value.Revision;
        if(revision.Length>20||revision.Any(c=>!char.IsAsciiHexDigit(c)))return null;
        string path=AppDataPaths.CacheFilePath("cooldown-voice-"+revision+"-"+clip+".mp3");
        if(File.Exists(path)&&new FileInfo(path).Length>1000)return path;
        using var stream=typeof(CooldownVoicePack).Assembly.GetManifestResourceStream("Wingbeat.CooldownVoice."+clip+".mp3");
        if(stream==null)return null;
        string temp=path+".tmp";
        using(var file=File.Create(temp))stream.CopyTo(file);
        File.Move(temp,path,true);return path;
    }
}

internal sealed record CooldownVoiceReading(string Key,string Clip,bool? Ready);

// Observe an unavailable -> ready transition; opening settings, logging in, an
// unknown reading, or switching characters must never announce already-ready skills.
internal sealed class CooldownVoiceTransitions
{
    internal static bool? Ready(double? seconds,int? charges)=>charges is int count?count>0:seconds is double s&&double.IsFinite(s)?s<=0:null;
    readonly Dictionary<string,bool> previous=new();
    string? context;
    long lastPoll;
    internal string[] Poll(string session,IEnumerable<CooldownVoiceReading> readings,long now)
    {
        if(context!=session||now-lastPoll>3000||now<lastPoll)previous.Clear();
        context=session;lastPoll=now;
        var live=new HashSet<string>();var clips=new List<string>();
        foreach(var row in readings){
            live.Add(row.Key);
            if(row.Ready is not bool ready){previous.Remove(row.Key);continue;}
            if(ready&&previous.TryGetValue(row.Key,out bool wasReady)&&!wasReady)clips.Add(row.Clip);
            previous[row.Key]=ready;
        }
        foreach(var key in previous.Keys.Where(k=>!live.Contains(k)).ToArray())previous.Remove(key);
        return clips.Distinct().ToArray();
    }
    internal void Reset(){previous.Clear();context=null;lastPoll=0;}
}

// A single decoder and a bounded, expiring queue avoid overlapping announcements
// and keep the voice pack out of RAM until a selected skill actually becomes ready.
internal sealed class CooldownVoicePlayer:IDisposable
{
    readonly Func<string,bool>? canPlay;
    readonly MediaPlayer player=new();
    readonly Queue<(string Clip,long At,double Volume)> queue=new();
    readonly System.Windows.Threading.DispatcherTimer watchdog=new(){Interval=TimeSpan.FromSeconds(1)};
    bool playing;
    long started;
    internal CooldownVoicePlayer(Func<string,bool>? canPlay=null){
        this.canPlay=canPlay;
        player.MediaEnded+=(_,_)=>Next();player.MediaFailed+=(_,_)=>Next();
        watchdog.Tick+=(_,_)=>{if(playing&&Environment.TickCount64-started>8000)Next();};
    }
    internal void Enqueue(string clip,double volume){
        if(queue.Count>=8)return;
        queue.Enqueue((clip,Environment.TickCount64,double.IsFinite(volume)?Math.Clamp(volume,0,1):.7));
        if(!playing)Next();
    }
    void Next(){
        player.Stop();player.Close();playing=false;watchdog.Stop();
        while(queue.TryDequeue(out var item)){
            if(Environment.TickCount64-item.At>5000||canPlay?.Invoke(item.Clip)==false)continue;
            try{
                string? path=CooldownVoicePack.Extract(item.Clip);if(path==null)continue;
                player.Volume=item.Volume;player.Open(new Uri(path));started=Environment.TickCount64;playing=true;watchdog.Start();player.Play();return;
            }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){LocalLog.Write("Cooldown voice: "+ex.Message);}
        }
    }
    internal void Stop(){if(!playing&&queue.Count==0)return;queue.Clear();player.Stop();player.Close();playing=false;watchdog.Stop();}
    public void Dispose()=>Stop();
}
