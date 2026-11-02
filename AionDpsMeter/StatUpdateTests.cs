using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class StatUpdateTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] body = [1, 1, 19, 0, ..BitConverter.GetBytes(-5), ..BitConverter.GetBytes(5000000000L)];
        var reader = new WireReader(body); var value = StatUpdateWire.Read(ref reader);
        check(value.Values[19] == -5 && value.MaximumHp == 5000000000, "Stat deltas retain signed values and 64-bit maximum HP");
        bool truncation = true;
        for (int i = 0; i < body.Length; i++) {
            try { var r = new WireReader(body.AsSpan(0, i)); StatUpdateWire.Read(ref r); truncation = false; }
            catch (InvalidDataException) { }
        }
        check(truncation, "Truncated stat deltas cannot partially overwrite current stats");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Local", true, false, 0, "Cleric", 1)));
        void Send(int actor, long time, ushort stat, int amount) => engine.Apply(new("network", Network: new("actor-stats", time, "test",
            JsonSerializer.SerializeToElement(new StatUpdateReading(actor, new() { [stat] = amount }, 1000)))));
        Send(1, 1000, 19, 100); var before = engine.Snapshot();
        Send(2, 2000, 19, 999); Send(1, 2000, 31, 200); Send(1, 1500, 19, 999);
        check(engine.Fight.LocalStats is { IsComplete: false } stats && stats.Values[19] == 100 && stats.Values[31] == 200 &&
            before.LocalStats!.Values.Count == 1 && engine.Fight.Network.All(e => e.Kind != "actor-stats"),
            "Local stat deltas merge without losing earlier values, borrowing another actor, retaining raw packets or mutating snapshots");
        engine.Apply(new("player-stats", Stats: new(3000, new() { [19] = 300 })));
        Send(1, 4000, 31, 400);
        check(engine.Fight.LocalStats is { IsComplete: true } full && full.Values[19] == 300 && full.Values[31] == 400,
            "A full stat snapshot establishes completeness and later deltas preserve it");
        engine.Apply(new("network", Network: new("hp", 4000, "test", JsonSerializer.SerializeToElement(new { EntityId = 1, CurrentHp = 600 }))));
        var health = CapturedHealth.Build(engine.Snapshot())[1];
        check(health is { Value: 600, Maximum: 1000 } && health.Label.Contains("/"),
            "Local health presentation uses captured maximum HP without inferring it from damage");
        engine.Apply(new("network", Network: new("hp", 5000, "test", JsonSerializer.SerializeToElement(new { EntityId = 1, CurrentHp = 2000 }))));
        check(CapturedHealth.Build(engine.Snapshot())[1].Maximum == null,
            "An older maximum below observed current HP is not displayed as an impossible health fraction");
    }
}
