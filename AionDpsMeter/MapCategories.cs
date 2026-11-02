using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;

internal sealed record MapCategoryState(Dictionary<string,string[]> Enabled, Dictionary<string,string[]> Seen);
internal sealed class MapCategories
{
    readonly string? path;
    Dictionary<int,MapCategoryState> worlds=new();
    internal string? LoadError {get;}
    internal MapCategories(string? path=null) {
        this.path=path;if(path==null || !File.Exists(path))return;
        try {
            if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException();
            var data=JsonSerializer.Deserialize<Dictionary<int,MapCategoryState>>(File.ReadAllText(path))??throw new InvalidDataException();
            Validate(data);worlds=data;
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){LoadError="Saved map categories could not be loaded. The original file was retained.";}
    }
    static void Validate(Dictionary<int,MapCategoryState> data) {
        bool Invalid(Dictionary<string,string[]>? d)=>d==null || d.Count>32 || d.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Key.Length>100||p.Value==null||p.Value.Length>256||p.Value.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>100)||p.Value.Distinct().Count()!=p.Value.Length);
        if(data.Count>1024 || data.Any(p=>p.Key<=0||p.Value==null||Invalid(p.Value.Enabled)||Invalid(p.Value.Seen)))throw new InvalidDataException("Invalid map categories.");
    }
    static string[] Defaults(string type,IEnumerable<string> categories)=>categories.Where(c=>type switch {"npc" or "monster"=>false,"collectable"=>c=="monolith",_=>true}).ToArray();
    static MapCategoryState Copy(MapCategoryState state)=>new(state.Enabled.ToDictionary(p=>p.Key,p=>p.Value.ToArray()),state.Seen.ToDictionary(p=>p.Key,p=>p.Value.ToArray()));
    internal MapCategoryState Snapshot(int world)=>worlds.TryGetValue(world,out var state)?Copy(state):new(new(),new());
    internal void Reconcile(int world,IEnumerable<MapMarkerGroup> groups) {
        if(world<=0)throw new ArgumentOutOfRangeException(nameof(world));
        var state=Snapshot(world);
        foreach(var type in groups.Where(g=>g.World==world).GroupBy(g=>g.Type)) {
            var names=type.Select(g=>g.Category).Distinct().ToArray();state.Enabled.TryGetValue(type.Key,out var enabled);state.Seen.TryGetValue(type.Key,out var seen);
            if(enabled==null || enabled.Length>0 && !enabled.Intersect(names).Any())state.Enabled[type.Key]=Defaults(type.Key,names);
            else if(seen!=null)state.Enabled[type.Key]=enabled.Concat(Defaults(type.Key,names.Except(seen))).Distinct().ToArray();
            state.Seen[type.Key]=(seen??[]).Concat(names).Distinct().ToArray();
        }
        var next=new Dictionary<int,MapCategoryState>(worlds){[world]=state};Validate(next);worlds=next;
    }
    internal bool Enabled(int world,string type,string category)=>worlds.TryGetValue(world,out var state)&&state.Enabled.TryGetValue(type,out var names)&&names.Contains(category);
    internal void Toggle(int world,string type,string category,IEnumerable<MapMarkerGroup> groups) {
        var available=groups.Where(g=>g.World==world&&g.Type==type).Select(g=>g.Category).Distinct().ToArray();
        if(!available.Contains(category))throw new ArgumentException("Unknown map category.");
        var state=Snapshot(world);var names=state.Enabled.GetValueOrDefault(type)??available;
        state.Enabled[type]=names.Contains(category)?names.Where(n=>n!=category).ToArray():names.Append(category).ToArray();Commit(world,state);
    }
    internal void SetAll(int world,IEnumerable<MapMarkerGroup> groups,bool enabled) {
        var state=Snapshot(world);state.Enabled.Clear();
        foreach(var type in groups.Where(g=>g.World==world).GroupBy(g=>g.Type))state.Enabled[type.Key]=enabled?type.Select(g=>g.Category).Distinct().ToArray():[];
        Commit(world,state);
    }
    internal void ToggleType(int world,string type,IEnumerable<MapMarkerGroup> groups) {
        var names=groups.Where(g=>g.World==world&&g.Type==type).Select(g=>g.Category).Distinct().ToArray();
        if(names.Length==0)return;var state=Snapshot(world);
        state.Enabled[type]=(state.Enabled.GetValueOrDefault(type)?.Length??0)==names.Length?[]:names;Commit(world,state);
    }
    void Commit(int world,MapCategoryState state) {
        var next=new Dictionary<int,MapCategoryState>(worlds){[world]=state};Validate(next);
        if(path!=null) {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(next);if(bytes.Length>1024*1024)throw new InvalidOperationException("Map category storage limit reached.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);string temporary=path+".tmp";
            try{File.WriteAllBytes(temporary,bytes);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        worlds=next;
    }
}
