using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;

internal sealed record PetFarmDefinition(int Id,string Name,int[] Npcs,int[] SoulItems);
internal sealed record PetFarmData(string Source,string SourceSha256,PetFarmDefinition[] Pets);
internal sealed class PetFarmLinks
{
    static readonly Lazy<PetFarmLinks> instance=new(()=>new());
    internal static PetFarmLinks Default=>instance.Value;
    internal PetFarmDefinition[] Pets {get;}=[];
    readonly FrozenDictionary<int,PetFarmDefinition> byId;
    readonly FrozenDictionary<int,int[]> byNpc;
    internal string? LoadError {get;}
    internal PetFarmDefinition? Find(int id)=>byId.GetValueOrDefault(id);
    internal string Name(int id)=>Find(id)?.Name??PetCatalogue.Default.Find(id)?.Name??$"Pet {id}";
    internal IEnumerable<(int Id,string Name)> Targets=>Pets.Select(p=>(p.Id,p.Name))
        .Concat(PetCatalogue.Default.Pets.Where(p=>!byId.ContainsKey(p.Id)).Select(p=>(p.Id,p.Name)));
    internal int[] OfNpc(int id)=>byNpc.GetValueOrDefault(id)??[];
    PetFarmLinks() {
        try {
            using var stream=typeof(PetFarmLinks).Assembly.GetManifestResourceStream("Wingbeat.PetFarmLinks.json")??throw new InvalidDataException();
            var data=JsonSerializer.Deserialize<PetFarmData>(stream)??throw new InvalidDataException();
            if(data.SourceSha256?.Length!=64||!data.SourceSha256.All(Uri.IsHexDigit)||data.Pets is not {Length:>0 and <=2000}||data.Pets.Any(p=>p==null||p.Id is <1000 or >99999||string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>300||
                p.Npcs==null||p.Npcs.Length>10000||p.SoulItems==null||p.SoulItems.Length>1000||p.Npcs.Concat(p.SoulItems).Any(id=>id<=0)||p.Npcs.Distinct().Count()!=p.Npcs.Length||p.SoulItems.Distinct().Count()!=p.SoulItems.Length)||
                data.Pets.Select(p=>p.Id).Distinct().Count()!=data.Pets.Length||data.Pets.Sum(p=>(long)p.Npcs.Length+p.SoulItems.Length)>30000)throw new InvalidDataException();
            Pets=data.Pets;byId=Pets.ToFrozenDictionary(p=>p.Id);byNpc=Pets.SelectMany(p=>p.Npcs.Select(n=>(Npc:n,Pet:p.Id))).GroupBy(p=>p.Npc).ToFrozenDictionary(g=>g.Key,g=>g.Select(p=>p.Pet).Distinct().ToArray());
        }catch(Exception ex)when(ex is IOException or JsonException or ArgumentException){byId=FrozenDictionary<int,PetFarmDefinition>.Empty;byNpc=FrozenDictionary<int,int[]>.Empty;LoadError="Pet farming links unavailable.";}
    }
}
internal sealed record TrackedPetProgress(int Id,string Name,int? Level,long SoulsGained,decimal? KillsPerSoul,bool? Done);
internal sealed record FarmTargetProgress(string Kind,int Id,decimal? Count,long? Goal,bool? Done,TrackedPetProgress[] Pets, bool GoalReached = false)
{
    public bool CountIsPartial {get;init;}
}
internal static class FarmTargetProgressBuilder
{
    internal static FarmTargetProgress Build(MapTarget target,MapTargetIndex markers,PetFarmLinks pets,
        IReadOnlyDictionary<int,long> kills,IReadOnlyDictionary<int,long> gathers,IReadOnlyDictionary<int,long> loot,IReadOnlyDictionary<int,long> souls,
        PetCollectionSnapshot? collection,int petGoal=1,TraceDiscoverySnapshot? traces=null,int currentWorld=0)
    {
        if(target.Kind=="collectable") {
            var world=traces?.Worlds.FirstOrDefault(w=>w.World==(target.Id==1?currentWorld:target.Id));
            return new(target.Kind,target.Id,world?.Found,world?.Total,world?.Complete,[],world?.Complete==true) {CountIsPartial=world!=null&&traces?.HasFullSnapshot!=true};
        }
        int[] npcs=target.Kind=="pet"?pets.Find(target.Id)?.Npcs??[]:target.Kind=="npc"?[target.Id]:target.Kind=="item"?ItemFarmLinks.Default.Find(target.Id)?.Npcs??[]:[];
        decimal killCount=npcs.Distinct().Sum(id=>(decimal)kills.GetValueOrDefault(id));
        decimal? count=target.Kind=="pet"?(pets.Find(target.Id)==null?null:killCount):markers.Count(target,kills,gathers,loot);
        int[] petIds=target.Kind=="pet"?[target.Id]:npcs.SelectMany(pets.OfNpc).Distinct().ToArray();
        var states=collection?.Pets.ToDictionary(p=>p.PetId)??new();petGoal=Math.Clamp(petGoal,1,3);
        var progress=petIds.Select(id=>{
            int? level=states.GetValueOrDefault(id)?.Level ?? (collection?.HasRoster==true?0:null);long gained=souls.GetValueOrDefault(id);
            return new TrackedPetProgress(id,pets.Name(id),level,gained,target.Kind=="pet"&&count.HasValue&&gained>0?decimal.Floor(killCount/gained+.5m):null,level.HasValue?level>=petGoal:null);
        }).ToArray();
        long? goal=target.Kind is "pet" or "collectable"?null:target.Goal;
        bool? done=target.Kind=="pet"?progress.FirstOrDefault()?.Done:target.Kind=="collectable"?null:goal is >0&&count!=null?count>=goal:false;
        return new(target.Kind,target.Id,count,goal,done,progress,goal is >0&&count!=null&&count>=goal);
    }
}
