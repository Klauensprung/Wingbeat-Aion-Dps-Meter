using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class AbyssReadingTests
{
    internal static void Run(Action<bool,string> check,string temp)
    {
        byte[] Matching(bool abyss, bool empty=false) {
            var w=new PartyWireTests.Writer();w.Var(2);w.Raw(0);w.Var(1);w.Raw(1,0,1);w.Raw(abyss?(byte)1:(byte)2);w.Var(empty?0:3);
            if(!empty) {w.Raw(BitConverter.GetBytes((ushort)2301));w.Raw(1);w.Raw(BitConverter.GetBytes((ushort)2302));w.Raw(2);w.Raw(9,0,3);}return w.Bytes.ToArray();
        }
        byte[] Season(ushort result=0,byte type=1,string extra="{\"kill_count\":20,\"death_count\":3,\"assist_count\":7}") {
            var w=new PartyWireTests.Writer();w.Raw(BitConverter.GetBytes(result));w.Raw(type,7);w.Int(4);w.Long(6000000001);w.Text("Untrusted ranking name");w.Text("Guild");w.Raw(1,0);w.Int(50);w.Int(2);w.Raw(7);w.Long(5000000000);
            w.Text(extra);w.Int(-2);w.Int(8);w.Int(5);w.Int(5);w.Int(100);w.Long(1793470000000);return w.Bytes.ToArray();
        }
        CapturedPayload Packet(ushort op,byte[] body,long at=100) {var w=new PartyWireTests.Writer();w.Var(body.Length+6);w.Raw((byte)(op>>8),(byte)op);w.Raw(body);return new CapturedPayload(13328,50000,w.Bytes.ToArray()){TimestampMs=at};}
        var local=Packet(0x3336,LocalCharacterWireTests.Body(1,"Local"));
        DecodeResult Decode(ushort op,byte[] body,bool identity=true) {var decoder=new EuGameDecoder();if(identity)decoder.Decode(local);return decoder.Decode(Packet(op,body,200));}
        using var fixtures=new ReferencePacketFixtures(Path.Combine(temp,"reference-abyss-synthetic.jsonl"));
        fixtures.Add(Packet(0x578d,Season()));fixtures.Add(local);
        check(Decode(0x578d,Season(),false).Events.Count==0,"Season reply before authoritative identity cannot be assigned to a character");
        foreach(var (abyss,empty) in new[]{(true,false),(true,true),(false,false)}) {
            var body=Matching(abyss,empty);fixtures.Add(Packet(0x0a40,body));
            var decoded=Decode(0x0a40,body);
            check(decoded.Events.Count==(abyss?1:0) && (!abyss || decoded.Events.Single().Network!.Fields.Deserialize<AbyssMatching>()!.Servers.Length==(empty?0:2)),
                "Abyss matching chooses the appropriate content type and filters unknown races "+abyss+"/"+empty);
        }
        foreach(var body in new[]{Season(),Season(1),Season(type:2),Season(extra:"invalid"),Season(extra:"{\"kill_count\":\"20\",\"death_count\":0}"),Season(extra:"[]")})fixtures.Add(Packet(0x578d,body,200));
        var season=Decode(0x578d,Season()).Events.Single().Network!.Fields.Deserialize<AbyssSeason>();
        check(season is {Name:"Local",Points:5000000000,Grade:5,Kills:20,Deaths:3,Assists:7,RankChange:-2},"Season captures wide points, optional statistics and rank change under the local name");
        check(Decode(0x578d,Season(1)).Events.Count==0 && Decode(0x578d,Season(type:2)).Events.Count==0,"Failed and non-Abyss season replies do not replace current Abyss readings");
        check(Decode(0x578d,Season(extra:"invalid")).Events.Single().Network!.Fields.Deserialize<AbyssSeason>() is {Kills:null,Deaths:null,Assists:null},"Malformed optional season JSON preserves the valid points without inventing zero counts");
        foreach(var (op,body) in new[]{((ushort)0x0a40,Matching(true)),((ushort)0x578d,Season())})
            check(Enumerable.Range(0,body.Length).All(n=>Decode(op,body[..n]).Events.Count==0) && Decode(op,[..body,0]).Events.Count==0,"Abyss exact reader rejects every truncation/trailer "+op);
        var engine=new CombatEngine(new EuGameDecoder());engine.Process(local);engine.Process(Packet(0x0a40,Matching(true),200));engine.Process(Packet(0x578d,Season(),200));engine.NewFight("Reset");
        check(engine.Abyss.Season is {Name:"Local",Points:5000000000} && engine.Abyss.Servers().Length==2 && engine.Fight.Network.Count==0 && engine.Loot.Rows().Length==0,
            "Abyss readings survive manual fight reset without raw packet history or loot changes");
        var detached=engine.Abyss.Servers();detached[0]=new(9999,"dark");check(engine.Abyss.Servers()[0].Server==2301,"Abyss matching snapshots cannot mutate retained server information");
        engine.Apply(new("local-player",Actor:new(2,"Other",true,true,0,"Cleric",2302)));
        check(engine.Abyss.Season==null && engine.Abyss.Servers().Length==0,"Changing local server and character cannot carry old Abyss season or matchup readings");
    }
}
