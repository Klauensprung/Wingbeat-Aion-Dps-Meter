using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PersistentIdentityTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        const long wide = 650000000000000001, other = 650000000000000002;
        CapturedPayload Packet(ushort opcode, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(opcode >> 8), (byte)opcode); w.Raw(body); return new CapturedPayload(13328,50000,w.Bytes.ToArray()){TimestampMs=100}; }
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp,"reference-persistent-identities.jsonl"));
        foreach (bool all in new[] { false, true }) {
            var spawn = Packet(0x4536, PlayerSpawnWireTests.Body(2,"Other",all,wide));
            var local = Packet(0x3336, LocalCharacterWireTests.Body(1,"Local",all:all,persistentId:other));
            fixtures.Add(spawn); fixtures.Add(local);
            var a = new EuGameDecoder().Decode(spawn).Events.First(e=>e.Actor!=null).Actor!;
            var b = new EuGameDecoder().Decode(local).Events.First(e=>e.Actor!=null).Actor!;
            check(a.PersistentId==wide && b.PersistentId==other && a.CharacterId==0 && b.CharacterId==0,
                "Full persistent IDs survive spawn/local layouts without truncation or legacy-ID aliases " + all);
        }
        var actor = new Actor(2,"Same name",true,true,0,"Cleric",2301){PersistentId=wide};
        string json = JsonSerializer.Serialize(actor);
        check(JsonDocument.Parse(json).RootElement.GetProperty("PersistentId").GetString()==wide.ToString() && JsonSerializer.Deserialize<Actor>(json)!.PersistentId==wide,
            "Actor JSON stores full IDs as decimal strings without JavaScript precision loss");
        check(JsonSerializer.Deserialize<Actor>("{\"Id\":1,\"Name\":\"Legacy\"}")!.PersistentId==0,
            "Older saved actors remain readable with an unknown persistent identity");
        var engine = new CombatEngine();engine.Apply(new("local-player",Actor:new(1,"Local",true,true,0,"Cleric",2301)));
        engine.Apply(new("actor",Actor:actor));engine.Apply(new("actor",Actor:new(3,"Boss",false,false,0,"Unknown",0){NpcId=100}));
        engine.Apply(new("damage",Damage:new(){ActorId=2,TargetId=3,Timestamp=1000,Damage=100}));
        engine.Apply(new("actor",Actor:actor with {PersistentId=other}));
        var hit=engine.Fight.Damage.Single();
        check(hit.ActorId!=2 && engine.Fight.Actors[hit.ActorId].PersistentId==wide && engine.Fight.Actors[2].PersistentId==other,
            "Same-name entity reuse with a different persistent identity cannot relabel existing hits");
        engine.Apply(new("actor",Actor:actor with {PersistentId=0}));
        check(engine.Fight.Actors[2].PersistentId==other,"An identity-less metadata update preserves the current authoritative full ID");
        var saved=engine.Snapshot();var store=new FightStore(Path.Combine(temp,"persistent-identities"));store.Save(saved);
        check(store.Load(Path.Combine(store.Root,saved.Id+".json")).Actors[hit.ActorId].PersistentId==wide,
            "Saved fight roundtrip retains full historical player identity");
        var finder=new PartyFinderTracker();finder.Follow(actor);
        void Apply(string kind, object data) => finder.Apply(new(kind,100,"test",JsonSerializer.SerializeToElement(data)));
        Apply("party-finder",new FinderGroup(1,wide,[new(wide,"Old name",2301,"Cleric",50,100,1000,false),new(other,"Same name",2301,"Cleric",50,100,1000,false)]));
        finder.Follow(actor with{Name="Renamed"});Apply("party-finder-ready",new FinderReady(null,true));
        check(finder.Snapshot()?.Members.Single(m=>m.Id==wide).Ready==true && finder.Snapshot()!.Members.Single(m=>m.Id==other).Ready==false,
            "Finder self readiness follows the persistent identity through a rename and rejects a namesake");
        finder.Follow(actor with{PersistentId=other});
        check(finder.Snapshot()==null,"A changed full identity clears finder membership even when its name and server match");
    }
}
