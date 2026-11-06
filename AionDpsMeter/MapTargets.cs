using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;

internal sealed record MapTarget(string Kind,int Id,long? Goal=null);
internal sealed class MapTargets
{
    readonly string? path;
    MapTarget[] targets=[];
    internal string? LoadError {get;}
    internal MapTargets(string? path=null) {
        this.path=path;if(path==null||!File.Exists(path))return;
        try {
            if(new FileInfo(path).Length>256*1024)throw new InvalidDataException();
            var data=JsonSerializer.Deserialize<MapTarget[]>(File.ReadAllText(path))??throw new InvalidDataException();Validate(data);targets=data;
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){LoadError="Saved farming targets could not be loaded. The original file was retained.";}
    }
    static void Validate(MapTarget[] data) {
        if(data.Length>512 || data.Any(t=>t==null||t.Id<=0||t.Kind is not ("npc" or "gatherable" or "pet" or "item" or "collectable")||t.Goal is <0 or >1000000000)||
            data.Select(t=>(t.Kind,t.Id)).Distinct().Count()!=data.Length)throw new InvalidDataException("Invalid farming targets.");
    }
    internal MapTarget[] Snapshot()=>targets.ToArray();
    internal void SetItems(IEnumerable<int> ids)=>Commit(ids.Distinct().Order().Select(id=>new MapTarget("item",id)).ToArray());
    internal void AddGatherables(IEnumerable<int> ids) {
        var known=targets.Where(t=>t.Kind=="gatherable").Select(t=>t.Id).ToHashSet();
        var added=ids.Distinct().Where(id=>!known.Contains(id)).Select(id=>new MapTarget("gatherable",id)).ToArray();
        if(added.Length>0)Commit([..targets,..added]);
    }
    internal void Toggle(string kind,int id) {
        var target=new MapTarget(kind,id);Validate([target]);
        if(targets.Any(t=>t.Kind==kind&&t.Id==id)){Remove(kind,id);return;}
        var next=new[]{target}.Concat(targets.Where(t=>!(kind=="collectable"&&id==1&&t.Kind=="collectable"))).ToArray();Commit(next);
    }
    internal void Remove(string kind,int id)=>Commit(targets.Where(t=>t.Kind!=kind||t.Id!=id).ToArray());
    internal void SetGoal(string kind,int id,long? goal)=>Commit(targets.Select(t=>t.Kind==kind&&t.Id==id?t with {Goal=goal}:t).ToArray());
    void Commit(MapTarget[] next) {
        Validate(next);
        if(path!=null) {
            byte[] data=JsonSerializer.SerializeToUtf8Bytes(next);if(data.Length>256*1024)throw new InvalidDataException("Farming target storage limit reached.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);string temporary=path+".tmp";
            try{File.WriteAllBytes(temporary,data);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        targets=next;
    }
}
internal sealed class MapTargetIndex
{
    readonly Dictionary<(string Kind,int Id),MapMarkerGroup[]> markers;
    internal MapTargetIndex(IEnumerable<MapMarkerGroup> groups) {
        markers=groups.Select(g=>(Group:g,Target:Identity(g))).Where(p=>p.Target!=null)
            .GroupBy(p=>(p.Target!.Kind,p.Target.Id)).ToDictionary(g=>g.Key,g=>g.Select(p=>p.Group).ToArray());
    }
    internal static MapTarget? Identity(MapMarkerGroup group)=>group.Category=="monolith"?new("collectable",1):
        group.DatabaseType=="npc"&&group.Type=="monster"&&group.DatabaseId>0?new("npc",group.DatabaseId):
        group.DatabaseType=="gatherable"&&group.DatabaseId>0?new("gatherable",group.DatabaseId):null;
    internal static MapTarget[] Choices(IEnumerable<MapMarkerGroup> visible,string name)=>visible.Where(g=>g.Name==name)
        .Select(Identity).OfType<MapTarget>().Distinct().OrderBy(t=>t.Kind,StringComparer.Ordinal).ThenBy(t=>t.Id).ToArray();
    internal MapMarkerGroup[] Groups(MapTarget target,int? world=null) {
        var key=(target.Kind,target.Kind=="collectable"?1:target.Id);
        int? filter=target.Kind=="collectable"&&target.Id!=1?target.Id:world;
        return (markers.GetValueOrDefault(key)??[]).Where(g=>filter==null||g.World==filter).ToArray();
    }
    internal int[] ObjectIds(MapTarget target,int? world=null)=>Groups(target,world).Where(g=>g.ObjectId>0).Select(g=>g.ObjectId).Distinct().Order().ToArray();
    internal decimal? Count(MapTarget target,IReadOnlyDictionary<int,long> kills,IReadOnlyDictionary<int,long> gathered,IReadOnlyDictionary<int,long> loot)=>target.Kind switch {
        "npc"=>kills.GetValueOrDefault(target.Id),"gatherable"=>ObjectIds(target).Sum(id=>(decimal)gathered.GetValueOrDefault(id)),"item"=>loot.GetValueOrDefault(target.Id),_=>null
    };
}
