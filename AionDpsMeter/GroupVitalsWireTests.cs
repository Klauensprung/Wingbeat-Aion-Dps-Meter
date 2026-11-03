using AionDpsMeter.Models;
using System.IO;

namespace AionDpsMeter.Local;

internal static class GroupVitalsWireTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Var(long value) { var b = new List<byte>(); do { b.Add((byte)((value & 127L) | (value >= 128 ? 128L : 0L))); value >>= 7; } while (value != 0); return b.ToArray(); }
        DecodeResult Decode(byte high, byte low, byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [..Var(body.Length + 6), high, low, ..body]) { TimestampMs = 1000 });
        byte[] vitals = [1, ..Var(5000000000), ..Var(6000000000), ..BitConverter.GetBytes(500), ..BitConverter.GetBytes(1000), ..new byte[16], 1];
        foreach (var op in new[] { (High: (byte)0x1b, Low: (byte)0x92), (High: (byte)0x2b, Low: (byte)0x96) }) {
            var result = Decode(op.High, op.Low, vitals);
            var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Local", true, false, 0, "Cleric", 1)));
            foreach (var e in result.Events) engine.Apply(e);
            check(CapturedHealth.Build(engine.Fight)[1] is { Value: 5000000000, Maximum: 6000000000 } &&
                engine.Fight.LocalResources is { Mp: 500, MaximumMp: 1000 } && engine.Fight.Damage.Count == 0 && engine.Fight.Deaths.Count == 0,
                "Group HP/MP updates preserve wide health values and feed local resources without inventing combat");
            check(Enumerable.Range(0, vitals.Length).All(n => Decode(op.High, op.Low, vitals[..n]).Events.Count == 0) &&
                Decode(op.High, op.Low, [..vitals, 0]).Events.Count == 0,
                "Party and alliance vitals reject incomplete or extended records atomically");
        }
        var partyEngine = new CombatEngine();
        partyEngine.Apply(new("local-player", Actor: new(9, "Local", true, false, 0, "Cleric", 2301)));
        partyEngine.Apply(new("actor", Actor: new(8, "Nearby outsider", true, false, 0, "Cleric", 2301)));
        foreach (var item in Decode(0x1b, 0x92, vitals).Events) partyEngine.Apply(item);
        check(!partyEngine.Fight.PartyKnown && partyEngine.Fight.Actors[1].Party && !partyEngine.Fight.Actors[8].Party &&
            PartyHealthView.Build(partyEngine.Fight, CapturedHealth.Build(partyEngine.Fight), []).Length == 2,
            "Party-only vitals recover confirmed members mid-session without admitting nearby players or claiming a full roster");
        partyEngine.Apply(new("actor", Actor: new(1, "Arriving member", true, false, 0, "Cleric", 2301) { PersistentId = 6000000001 }));
        check(partyEngine.Fight.Actors[1] is { Party: true, Name: "Arriving member" },
            "A party-health placeholder retains membership when its player identity arrives");
        partyEngine.Apply(new("party", Party: new([], "Captured party departure")));
        check(!partyEngine.Fight.Actors[1].Party, "Explicit departure clears membership recovered from party HP packets");
        byte[] guard = [1, 63, 2, ..BitConverter.GetBytes(2000000), ..BitConverter.GetBytes(1500000), ..BitConverter.GetBytes(4),
            ..BitConverter.GetBytes(5000000000L), ..BitConverter.GetBytes(6000000000L), 3, ..BitConverter.GetBytes(7000000000L)];
        var fields = Decode(5, 0xe0, guard).Events.Single().Network!.Fields;
        check(fields.GetProperty("Paused").GetBoolean() && fields.GetProperty("Activated").GetBoolean() &&
            fields.GetProperty("CooldownExpiresAt").GetInt64() == 7000000000 && fields.GetProperty("CurrentGuard").GetInt32() == 1500000,
            "Guard flags decode every optional field and share packed booleans across the cooldown timestamp");
        check(Decode(5, 0xe0, [1, 0, 0, 0]).Events.Single().Network!.Fields.GetProperty("MaxGuard").GetInt32() == 0 &&
            Enumerable.Range(0, guard.Length).All(n => Decode(5, 0xe0, guard[..n]).Events.Count == 0) &&
            Decode(5, 0xe0, [1, 64, 0, 0]).Events.Count == 0 && Decode(5, 0xe0, [..guard, 0]).Events.Count == 0,
            "Empty guard state is valid while unknown flags, truncations and trailers are rejected");
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "guard-wire-fixtures.jsonl"));
        bool valid = true;
        for (int flags = 0; flags < 64; flags++) for (byte bits = 0; bits < 4; bits++) {
            var body = new List<byte>(Var(16384)) { (byte)flags, 255 };
            if ((flags & 1) != 0) body.AddRange(BitConverter.GetBytes(int.MaxValue));
            if ((flags & 2) != 0) body.AddRange(BitConverter.GetBytes(1500000));
            if ((flags & 4) != 0) body.AddRange(BitConverter.GetBytes(4));
            if ((flags & 8) != 0) body.AddRange(BitConverter.GetBytes(5000000000L));
            if ((flags & 16) != 0) body.AddRange(BitConverter.GetBytes(6000000000L));
            body.Add(bits);
            if ((flags & 32) != 0) body.AddRange(BitConverter.GetBytes(7000000000L));
            var payload = new CapturedPayload(13328, 50000, [..Var(body.Count + 6), 5, 0xe0, ..body]) { TimestampMs = 1000 };
            fixtures.Add(payload);
            valid &= new EuGameDecoder().Decode(payload).Events is [{ Network.Kind: "guard" }];
        }
        check(valid, "Guard reference fixtures cover all 64 optional masks and four packed-boolean combinations");
    }
}
