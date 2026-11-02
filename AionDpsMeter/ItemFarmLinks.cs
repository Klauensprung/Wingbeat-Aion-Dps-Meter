using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;
internal sealed record ItemFarmDefinition(int Id,int[] Npcs,int[] Gatherables);
internal sealed class ItemFarmLinks
{
    sealed record Row(int Id,int Npcs,int Gatherables);
    sealed record Data(string SourceSha256,int[][] NpcGroups,int[][] GatherGroups,Row[] Items);
    static readonly Lazy<ItemFarmLinks> instance=new(()=>new());
    internal static ItemFarmLinks Default=>instance.Value;
    readonly FrozenDictionary<int,ItemFarmDefinition> byId;
    internal string? LoadError {get;}
    internal int Count=>byId.Count;
    internal IEnumerable<ItemFarmDefinition> Items=>byId.Values;
    internal ItemFarmDefinition? Find(int id)=>byId.GetValueOrDefault(id);
    ItemFarmLinks() {
        try {
            using var stream=typeof(ItemFarmLinks).Assembly.GetManifestResourceStream("Wingbeat.ItemFarmLinks.json")??throw new InvalidDataException();
            var data=JsonSerializer.Deserialize<Data>(stream)??throw new InvalidDataException();
            bool ValidGroups(int[][]? groups)=>groups is {Length:>0 and <=10000}&&groups.All(g=>g!=null&&g.Length<=10000&&g.All(id=>id>0)&&g.Distinct().Count()==g.Length)&&groups.Sum(g=>(long)g.Length)<=200000;
            if(data.SourceSha256 is not {Length:64}||!data.SourceSha256.All(Uri.IsHexDigit)||!ValidGroups(data.NpcGroups)||!ValidGroups(data.GatherGroups)||
                data.Items is not {Length:>0 and <=10000}||data.Items.Any(r=>r==null||r.Id<=0||r.Npcs<0||r.Npcs>=data.NpcGroups.Length||r.Gatherables<0||r.Gatherables>=data.GatherGroups.Length)||data.Items.Select(r=>r.Id).Distinct().Count()!=data.Items.Length)throw new InvalidDataException();
            byId=data.Items.ToFrozenDictionary(r=>r.Id,r=>new ItemFarmDefinition(r.Id,data.NpcGroups[r.Npcs],data.GatherGroups[r.Gatherables]));
        }catch(Exception ex)when(ex is IOException or JsonException or ArgumentException){byId=FrozenDictionary<int,ItemFarmDefinition>.Empty;LoadError="Item source catalogue unavailable.";}
    }
}
