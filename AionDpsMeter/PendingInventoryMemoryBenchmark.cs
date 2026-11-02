using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;
internal static class PendingInventoryMemoryBenchmark
{
    internal static int Run(string[] args,string output)
    {
        int rounds=1000,index=Array.IndexOf(args,"--rounds");
        if(index>=0&&(index+1>=args.Length||!int.TryParse(args[index+1],out rounds)||rounds is <10 or >2000))throw new ArgumentException("Rounds must be 10..2000.");
        var engine=new CombatEngine();var owner=new Actor(1,"Synthetic transfer",true,false,0,"Cleric",2301){PersistentId=6000000001};
        engine.Apply(new("local-player",Actor:owner));
        const long origin=1700000000000;long count=10000;
        void Network(string kind,long at,object data)=>engine.Apply(new("network",Network:new(kind,at,"benchmark",JsonSerializer.SerializeToElement(data))));
        Network("inventory",origin,new InventoryBatch(true,[new(100,110120003,count,1)]));
        var disk=args.Contains("--persist-fights")?new FightStore(AppDataPaths.FilePath("pending-inventory-benchmark-"+Guid.NewGuid().ToString("N"))):null;
        var archive=new List<Fight>();var samples=new List<object>();var clock=Stopwatch.StartNew();
        int runCount=0,persisted=0,raw=0,maximumPending=0;long bytes=0;bool valid=true;long allocated=GC.GetTotalAllocatedBytes(true);
        for(int round=0;round<rounds;round++) {
            long at=origin+round*1000+1;
            Network("local-zone",at,new {MapId=600002,AwaitingIdentity=true});
            for(int i=0;i<100;i++) {
                bool use=i%2==1;count+=use?-1:3;
                Network("inventory-update",at+i+1,new InventoryBatch(false,[new(100,110120003,count,1,1,use?(byte)0:(byte)5,use?(byte)14:(byte)0)]));
            }
            maximumPending=Math.Max(maximumPending,engine.PendingInventoryReceipts);
            if(round%2==0)Network("local-zone",at+101,new {MapId=1110});
            engine.Apply(new("local-player",Actor:owner with {Id=round+2}));
            valid&=engine.PendingInventoryReceipts==0&&engine.PendingTransferStacks==0&&engine.PendingInventoryOmissions==0&&engine.CurrentHoldings().Items.Single().Count==count;
            if(round%2!=0)Network("local-zone",at+102,new {MapId=1110});
            engine.EndFight("Synthetic transfer reset");
            foreach(var fight in engine.SaveCompleted(_=>{})) {
                if(fight.DungeonRun is {} run) {runCount++;valid&=run.ReceivedItems is [{Count:150}]&&run.UsedItems is [{Count:50}];}
                if(disk!=null) {
                    disk.Save(fight);string file=Path.Combine(disk.Root,fight.Id+".json");var loaded=disk.Load(file);
                    valid&=loaded.DungeonRun?.Id==fight.DungeonRun?.Id&&loaded.SavedLoot?.Rows.Sum(r=>r.Quantity)==fight.ForStorage().SavedLoot?.Rows.Sum(r=>r.Quantity);
                    persisted++;bytes+=new FileInfo(file).Length;
                }
                var summary=FightArchiveSummary.Create(fight);raw+=summary.Damage.Count+summary.Healing.Count+summary.Network.Count;archive.Add(summary);
            }
            valid&=!engine.HasCompletedFights;
            if((round+1)%100==0||round+1==rounds) {
                long managed=GC.GetTotalMemory(true);using var process=Process.GetCurrentProcess();process.Refresh();
                samples.Add(new {Rounds=round+1,ManagedBytes=managed,WorkingSetBytes=process.WorkingSet64,ArchiveSummaries=archive.Count,engine.PendingInventoryReceipts,engine.PendingTransferStacks});
            }
        }
        using var final=Process.GetCurrentProcess();valid&=runCount==rounds&&raw==0;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(new {Rounds=rounds,InventoryUpdates=rounds*100,RunSummaries=runCount,Valid=valid,ArchiveRawEvents=raw,MaximumPendingReceipts=maximumPending,
            Persistence=new {Enabled=disk!=null,Directory=disk?.Root,Fights=persisted,Bytes=bytes},AllocatedBytes=GC.GetTotalAllocatedBytes(true)-allocated,PeakWorkingSetBytes=final.PeakWorkingSet64,ElapsedSeconds=clock.Elapsed.TotalSeconds,Samples=samples,
            Scope="Synthetic pending-identity inventory updates, alternating current/completed dungeon binding, exact rewards/uses, resets, compact archive summaries and optional real save/load. Forced GC measures retention; excludes WPF, live Npcap and long single fights."},new JsonSerializerOptions{WriteIndented=true}));
        GC.KeepAlive(engine);GC.KeepAlive(archive);return valid?0:1;
    }
}
