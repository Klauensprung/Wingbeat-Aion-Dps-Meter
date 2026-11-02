using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class CombatPowerTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        DecodeResult Decode(byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [(byte)(body.Length + 6), 0x56, 0x36, ..body]));
        byte[] bytes = [..BitConverter.GetBytes(90000L), ..BitConverter.GetBytes(100000L)];
        check(Decode(bytes).Events.Single().Network!.Fields.GetProperty("CombatPower").GetInt32() == 90000 &&
            Enumerable.Range(0, bytes.Length).All(n => Decode(bytes[..n]).Events.Count == 0) && Decode([..bytes, 0]).Events.Count == 0 &&
            Decode([..BitConverter.GetBytes(-1L), ..BitConverter.GetBytes(100000L)]).Events.Count == 0 &&
            Decode([..BitConverter.GetBytes(90000L), ..BitConverter.GetBytes(-1L)]).Events.Count == 0,
            "Local combat power requires both complete 64-bit values and rejects negative values and unknown trailers");
        var engine = new CombatEngine();
        void Update(long at, int cp) => engine.Apply(new("network", Network: new("local-combat-power", at, "test",
            JsonSerializer.SerializeToElement(new { CombatPower = cp, HighestCombatPower = 100000L }))));
        Update(1, 999);
        var actor = new Actor(1, "Player", true, false, 50000, "Cleric", 2301) { CpSource = "Local character packet" };
        engine.Apply(new("local-player", Actor: actor));
        check(engine.Fight.Actors[1].Cp == 50000, "Unscoped combat power updates cannot create a player or replace a later login baseline");
        var before = engine.Snapshot(); Update(10, 90000); Update(9, 100);
        engine.Apply(new("actor", Actor: actor with { Cp = 20000, CpSource = "Captured party" }));
        engine.Apply(new("party-profiles", Profiles: [new("Player", 2301, 10000, 0)]));
        check(engine.Fight.Actors[1] is { Cp: 90000, CpSource: "Local CP update" } && before.Actors[1].Cp == 50000 && engine.Fight.Network.Count == 0,
            "Fresh CP updates preserve earlier snapshots and outrank stale updates, roster and profile copies without raw retention");
        Update(11, 0);
        check(engine.Snapshot().Actors[1] is { Cp: 0, CpSource: "Local CP update" }, "Captured zero CP cannot resurrect stale cached CP");
        Update(12, 95000); engine.NewFight("Reset");
        var store = new FightStore(Path.Combine(temp, "combat-power")); store.Save(engine.Snapshot());
        check(store.Load(Path.Combine(store.Root, engine.Fight.Id + ".json")).Actors[1].Cp == 95000,
            "Latest captured CP survives ordinary fight reset and persistence");
        engine.Apply(new("network", Network: new("local-zone", 20, "test", JsonSerializer.SerializeToElement(new { MapId = 1010, AwaitingIdentity = true }))));
        Update(21, 80000);
        check(engine.Fight.Actors[1].Cp == 95000, "CP updates during map preparation cannot modify the previous identity");
        engine.Apply(new("local-player", Actor: actor with { Id = 2, Name = "Second", Cp = 30000 })); Update(22, 40000);
        check(engine.Fight.Actors[2].Cp == 40000, "Local CP updates resume for the newly identified character");
        engine.Apply(new("actor", Actor: engine.Fight.Actors[2] with { Cp = 70000, CpSource = "Entered locally" })); Update(23, 50000);
        check(engine.Fight.Actors[2].Cp == 70000, "Manual CP override remains authoritative over live CP updates");
    }
}
