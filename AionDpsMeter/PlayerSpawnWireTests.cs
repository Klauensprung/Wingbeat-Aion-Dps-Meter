using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PlayerSpawnWireTests
{
    internal static byte[] Body(int entity, string name, bool all = false, long persistentId = 6000000000, byte reason = 0, byte race = 2, byte? overrideRace = null)
    {
        var w = new PartyWireTests.Writer(); w.Var(entity); w.Int(all ? 0x7ffffff : reason != 0 ? 8193 : 1);
        w.Raw(overrideRace.HasValue?(byte)23:(byte)7); w.Text(name); w.Int(32); w.Raw(race, 2); if(overrideRace.HasValue)w.Raw(overrideRace.Value);
        w.Zero(20); w.Raw(1); w.Var(200); w.Var(400); w.Zero(52); w.Raw(0xfd, 8); // Server 2301.
        if (all) { w.Zero(8); w.Text("Guild"); w.Zero(17); w.Text("Union"); }
        w.Var(0); w.Var(all ? 1 : 0);
        if (all) {
            w.Zero(14); w.Bool(false); w.Zero(4); w.Var(1);
            w.Zero(4); w.Bool(true); w.Zero(11); w.Bool(false); w.Zero(9); w.Bool(true); w.Zero(4);
            w.Var(1); w.Int(100); w.Bool(true); w.Bool(false); w.Bool(true); w.Var(1000);
            w.Zero(2); w.Bool(true); w.Bool(false); w.Raw(0); w.Zero(12); w.Zero(8); w.Zero(2); w.Zero(2); w.Zero(8);
            w.Bool(true); w.Raw(reason); w.Bool(false);
        }
        if (!all && reason != 0) w.Raw(reason);
        w.Long(persistentId); w.Var(0); w.Zero(12);
        if (all) w.Zero(12);
        w.Var(0);
        if (all) {
            w.Raw(0); w.Zero(4); w.Raw(15); w.Zero(7); w.Bool(true); w.Zero(4); w.Zero(24);
            w.Zero(16); w.Bool(false); w.Zero(4);
        }
        w.Long(25000); w.Raw(0); w.Bool(true);
        return w.Bytes.ToArray();
    }
    internal static void Run(Action<bool, string> check)
    {
        DecodeResult Decode(byte[] body) {
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x45, 0x36); w.Raw(body);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 1000 });
        }
        foreach(byte race in new byte[]{0,1,2,3}) check(Decode(Body(2,"Faction",race:race)).Events.First().Actor?.Race == (race==1?"light":race==2?"dark":null), "Spawn faction retains known races and leaves unknown values unclassified");
        foreach(byte faction in new byte[]{0,1,2,3}) check(Decode(Body(2,"Abyss",overrideRace:faction)).Events.First().Actor is {Race:"dark"} a && a.OverrideRace==faction,"Player spawn preserves base faction separately from Abyss override");
        byte[] full = Body(1, "Player", true); var result = Decode(full);
        check(result.Events.First().Actor is { Id: 1, Name: "Player", Server: 2301, Job: "Cleric", Cp: 25000 } &&
            result.Events.Single(e => e.Network?.Kind == "hp").Network!.Fields.GetProperty("MaximumHp").GetInt64() == 400,
            "Complete player spawn validates every optional flag, decorated equipment and shared booleans while exposing identity and health");
        check(Enumerable.Range(0, full.Length).All(n => Decode(full[..n]).Events.Count == 0) && Decode([..full, 0]).Events.Count == 0,
            "Every player spawn truncation and unknown trailer rejects identity atomically");
        check(Decode(Body(2, "Player")).Events.First().Actor is { Cp: 25000, Job: "Cleric" },
            "Minimal complete player spawn reads class and CP without optional appearance data");
        check(Decode(Body(2, "Player")).Events.First().Actor?.Metadata is { Guild: "", GuildAt: >= 0 },
            "Player appearance without guild payload explicitly records no guild rather than preserving stale membership");
    }
}
