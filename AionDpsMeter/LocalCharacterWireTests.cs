using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class LocalCharacterWireTests
{
    internal static byte[] Body(int entity, string name, ulong cp = 25000, ulong peak = 26000, bool all = false, int? wing = null, int? skin = null, long persistentId = 6000000000, byte[]? appearance = null, byte race = 2, byte gender = 2, float direction = 0, int gearScore = 0, bool activeEffect = false)
    {
        var w = new PartyWireTests.Writer(); w.Var(entity); w.Int(all ? 0x3fffffff : 0); w.Raw(all ? (byte)63 : (byte)7);
        w.Text(name); w.Raw(0xfd, 8); w.Int(32); w.Raw(race); w.Int(0); w.Int(gearScore); w.Zero(28); w.Raw(gender);
        w.Var(all ? 1 : 0); if (all) w.Zero(14);
        w.Var(all ? 1 : 0); if (all) w.Zero(13);
        w.Zero(17); if (all) { w.Raw(0); w.Var(1); w.Zero(3); w.Var(1); w.Zero(3); }
        ushort stateFlags = (ushort)((wing.HasValue ? 4096 : 0) | (skin.HasValue ? 16384 : 0));
        w.Raw(BitConverter.GetBytes(stateFlags)); w.Zero(12); w.Raw(BitConverter.GetBytes(direction)); w.Zero(2); w.Raw(1); w.Var(200); w.Var(400); w.Zero(52);
        if (wing.HasValue) w.Int(wing.Value); if (skin.HasValue) w.Int(skin.Value);
        if (all) { w.Zero(8); w.Text("Guild"); w.Zero(17); w.Text("Union"); }
        w.Var(1); w.Raw(19, 0); w.Int(1234); w.Var(activeEffect ? 1 : 0);
        if (activeEffect) {
            w.Raw(1); w.Var(7); w.Int(100); w.Long(10000); w.Long(0);
            w.Var(99); w.Raw(1); w.Bool(false);
        }
        w.Var(0); // Equipment.
        if (all) w.Raw(0); w.Long(123); w.Int(1010);
        if (all) w.Zero(22); w.Long(persistentId);
        if (all) { w.Zero(11); w.Bool(true); w.Var(300); }
        w.Var(appearance?.Length ?? 0); if (appearance != null) w.Raw(appearance); w.Bool(false);
        if (all) {
            w.Raw(0); w.Var(300); w.Zero(12);
            w.Var(1); w.Raw(1, 0); w.Text("Pin"); w.Zero(32); w.Var(1); w.Long(6000000000);
            w.Zero(20); w.Zero(10); w.Text("Killer"); w.Zero(5); w.Zero(2);
        }
        w.Zero(16);
        if (all) { w.Bool(true); w.Zero(28); }
        w.Raw(BitConverter.GetBytes(cp)); w.Raw(BitConverter.GetBytes(peak));
        w.Var(all ? 1 : 0); if (all) w.Zero(3);
        w.Var(all ? 1 : 0); if (all) w.Zero(3);
        if (all) { w.Var(1); w.Zero(8); w.Var(1); w.Zero(8); }
        return w.Bytes.ToArray();
    }
    internal static void Run(Action<bool, string> check)
    {
        DecodeResult Decode(byte[] body) {
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(0x33, 0x36); w.Raw(body);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 1000 });
        }
        byte[] full = Body(1, "Local", all: true); var result = Decode(full); var engine = new CombatEngine();
        check(Decode(Body(1, "Local", direction: -123.5f)).Events.First(e => e.Network?.Kind == "local-zone").Network!.Fields.GetProperty("Direction").GetSingle() == -123.5f,
            "Login preserves signed facing direction for timestamp-matched cast evidence");
        check(Decode(Body(1, "Local", direction: float.NaN)).Events.Count == 0,
            "Invalid login facing rejects the packet before publishing partial character state");
        foreach (var e in result.Events) engine.Apply(e);
        check(engine.Fight.LocalPlayer == 1 && engine.Fight.Actors[1] is { Job: "Cleric", Cp: 25000, Server: 2301 } &&
            engine.Fight.LocalPosition?.MapId == 1010 && engine.Fight.LocalStats?.Values[19] == 1234 && engine.Fight.LocalResources is { Mp: 0, Sp: 0, Fp: 0 } &&
            CapturedHealth.Build(engine.Fight)[1] is { Value: 200, Maximum: 400 },
            "Complete local login parses all optional fields and publishes identity, position, stats and health after reset");
        check(Enumerable.Range(0, full.Length).All(n => Decode(full[..n]).Events.Count == 0) && Decode([..full, 0]).Events.Count == 0,
            "Every login truncation and unknown trailer is rejected before session-reset identity publication");
        check(Decode(Body(1, "Local", 0, 0)).Events.First().Actor?.Cp == 0 &&
            Decode(Body(1, "Local", 9000, 10000)).Events.First().Actor?.Cp == 9000 &&
            Decode(Body(1, "Local", 3000000, 4000000)).Events.First().Actor?.Cp == 3000000,
            "Full login CP accepts exact zero, low and high readings without the previous plausibility heuristic");
        foreach (var e in Decode(Body(1, "Local", wing: 100, skin: 200)).Events) engine.Apply(e);
        check(engine.Fight.Actors[1].Wings is { Equipped: 100, Skin: 200, Owned.Length: 0 },
            "Initial local character state establishes equipped wing and cosmetic skin without inventing an owned list");
        foreach (var e in Decode(Body(1, "Local")).Events) engine.Apply(e);
        check(engine.Fight.Actors[1].Wings is { Equipped: 0, Skin: 0 },
            "Full local appearance without wing flags clears prior equipment and skin using protocol defaults");
        var spawnEngine = new CombatEngine();
        var spawn = Decode(Body(1, "Local", all: true, activeEffect: true));
        foreach (var e in spawn.Events) spawnEngine.Apply(e);
        var session = new LiveOverlaySession(); var effects = new LiveEffectTracker();
        var cooldowns = new CooldownTracker(); var party = new PartyDebuffTracker();
        session.Observe(spawnEngine, spawnEngine.Snapshot(), cooldowns, effects, party, 2000, 5000);
        check(effects.Active(5000) is [{ Slot: 7, EffectId: 100, CasterId: 99, RemainingSeconds: 9 }] && party.RetainedSlots == 1,
            "Full character packet carries spawn effects through decoder, engine snapshot and live overlay session");
        spawnEngine.NewFight("Spawn effect regression");
        session.Observe(spawnEngine, spawnEngine.Snapshot(), cooldowns, effects, party, 3000, 6000);
        check(effects.Active(6000) is [{ Slot: 7, RemainingSeconds: 8 }] && effects.Active(14000).Length == 0,
            "Wire-origin spawn effect survives manual fight reset without restarting its expiry");
    }
}
