using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class ResourceSnapshotTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        check(Math.Abs(FlightPowerOverlayWindow.Fraction(new(1,Fp:240527,MaximumFp:661027))!.Value-.36386868)<.000001,"Flight power overlay uses the captured current/max ratio");
        check(FlightPowerOverlayWindow.Fraction(new(1,Fp:661027,MaximumFp:661027))==1&&FlightPowerOverlayWindow.Fraction(new(1,Fp:0,MaximumFp:661027))==0,"Flight power distinguishes fully recharged from empty");
        check(FlightPowerOverlayWindow.Fraction(null)==null&&FlightPowerOverlayWindow.Fraction(new(1,Fp:5))==null&&FlightPowerOverlayWindow.Fraction(new(1,Fp:6,MaximumFp:5))==null,"Missing or inconsistent flight power remains unknown instead of inventing readiness");
        var engine = new CombatEngine(); var local = new Actor(1, "Local", true, false, 0, "Cleric", 1);
        engine.Apply(new("local-player", Actor: local));
        void Send(int id, long time, byte type, long amount) => engine.Apply(new("network", Network: new("resources", time, "test",
            JsonSerializer.SerializeToElement(new { EntityId = id, Values = new Dictionary<byte, long> { [type] = amount } }))));
        Send(1, 1000, 1, 5000000000); var before = engine.Snapshot();
        Send(2, 2000, 1, 1); Send(1, 2000, 8, 6000000000); Send(1, 1500, 1, 1);
        check(engine.Fight.LocalResources is { Mp: 5000000000, MaximumMp: 6000000000 } && before.LocalResources!.MaximumMp == null &&
            engine.Fight.Network.All(e => e.Kind != "resources"),
            "Resource snapshots merge 64-bit local values without stale updates, other actors, raw history or snapshot mutation");
        engine.NewFight("Resource reset");
        check(engine.Fight.LocalResources?.Mp == 5000000000 && ResourceSnapshots.Rows(engine.Fight.LocalResources).Count() == 2,
            "DPS reset preserves only captured resource values and their display rows");
        var store = new FightStore(Path.Combine(temp, "resources")); var snapshot = engine.Snapshot(); store.Save(snapshot);
        check(store.Load(Path.Combine(store.Root, snapshot.Id + ".json")).LocalResources == snapshot.LocalResources,
            "Captured resources survive compact fight storage without retaining resource packets");
        engine.Apply(new("local-player", Actor: local));
        check(engine.Fight.LocalResources == null, "Character re-entry clears obsolete resource observations until new packets arrive");
    }
}
