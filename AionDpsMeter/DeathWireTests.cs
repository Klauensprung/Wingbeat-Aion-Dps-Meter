using System.Text;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class DeathWireTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Var(int value) { var b = new List<byte>(); do { b.Add((byte)((value & 127) | (value >= 128 ? 128 : 0))); value >>= 7; } while (value != 0); return b.ToArray(); }
        byte[] Text(string s) { var b = Encoding.UTF8.GetBytes(s); return [..Var(b.Length), ..b]; }
        byte[] Body(int skill, int killer, ushort server, string name, int npc) => [1, ..BitConverter.GetBytes(skill), ..Var(killer),
            ..BitConverter.GetBytes(server), ..Text(name), ..Text(new string('g', 130)), 0, 0, ..Var(npc), ..BitConverter.GetBytes(-1), 2, 3];
        using var fixtures = new ReferencePacketFixtures(System.IO.Path.Combine(temp,"death-wire-fixtures.jsonl"));
        DecodeResult Decode(byte[] body) {
            var payload = new CapturedPayload(13328, 50000,[..Var(body.Length + 6), 4, 0x8d, ..body]) { TimestampMs = 100 };
            var decoded = new EuGameDecoder().Decode(payload);
            if (decoded.Events.Count > 0) fixtures.Add(payload);
            return decoded;
        }
        var body = Body(17090000, 2, 1001, "Player\0", 0);
        var result = Decode(body);
        var death = result.Events.Single(e => e.Network?.Kind == "entity-death").Network!;
        check(death.Fields.GetProperty("SkillCode").GetInt32() == 17090000 &&
            result.Events.Single(e => e.Actor != null).Actor!.Name == "Player" &&
            result.Events.All(e => e.Network?.Kind != "target"),
            "Complete death record preserves killer and skill without inventing target selection");
        var npc = Decode(Body(0, 0, 0, "", 1234)).Events.Single().Network!;
        check(npc.Kind == "entity-death" && npc.Fields.GetProperty("NpcId").GetInt32() == 1234 &&
            npc.Fields.GetProperty("KillerId").GetInt32() == 0,
            "Death without skill or actor attribution is retained, including NPC data identity");
        check(Decode(Body(17090000,16384,2301,"Ä火",0)).Events.Single(e=>e.Actor!=null).Actor is {Id:16384,Name:"Ä火",Server:2301},
            "Death attribution retains Unicode names and multi-byte killer entity IDs");
        check(Enumerable.Range(0, body.Length).All(n => Decode(body[..n]).Events.Count == 0) &&
            Decode([..body, 0]).Events.Count == 0,
            "Every death truncation and unknown trailer is rejected atomically");
        var fight = new Fight(); fight.Network.Add(death);
        var saved = FightStorageEvents.Select(fight).Single();
        check(saved.Fields.GetProperty("SkillCode").GetInt32() == 17090000 &&
            saved.Fields.GetProperty("Reason").GetByte() == 2,
            "Compact death evidence retains skill and reason for historical analysis");
    }
}
