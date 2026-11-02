using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class SkillStartWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] Var(int n) { var bytes = new List<byte>(); do { byte b = (byte)(n & 127); n >>= 7; bytes.Add((byte)(b | (n == 0 ? 0 : 128))); } while (n > 0); return bytes.ToArray(); }
        byte[] Body(byte flags, byte targetFlags) {
            var b = new List<byte> { 1, flags }; b.AddRange(BitConverter.GetBytes(17010001)); b.Add(3);
            if ((flags & 1) != 0) b.Add(4);
            b.Add(targetFlags); b.Add(100);
            if ((targetFlags & 1) != 0) b.AddRange(new byte[4]);
            b.AddRange(new byte[4]);
            if ((targetFlags & 2) != 0) b.AddRange(new byte[12]);
            b.AddRange(Var(1000)); b.Add(0);
            if ((flags & 2) != 0) b.Add(8);
            b.AddRange(Var(5000));
            if ((flags & 4) != 0) b.Add(2);
            if ((flags & 8) != 0) b.AddRange(Var(2000));
            if ((flags & 16) != 0) b.Add(1);
            if ((flags & 32) != 0) { b.Add(1); b.Add(100); b.AddRange(new byte[12]); }
            return b.ToArray();
        }
        DecodeResult Decode(byte[] b, byte opcode = 2) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [..Var(b.Length + 6), opcode, 0x38, ..b]) { TimestampMs = 1000 });
        bool valid = true, bounded = true;
        for (byte flags = 0; flags < 64; flags++) for (byte targetFlags = 0; targetFlags < 4; targetFlags++) {
            var body = Body(flags, targetFlags); var events = Decode(body).Events;
            valid &= events.Count == ((flags & 8) != 0 ? 3 : 2) && events[0].Network!.Fields.Deserialize<SkillStartReading>() is { ActorId: 1, TargetId: 100, CooldownMs: 5000 };
            bounded &= Enumerable.Range(0, body.Length).All(n => Decode(body[..n]).Events.Count == 0) && Decode([..body, 0]).Events.Count == 0;
        }
        check(valid, "All 256 supported skill-start flag/target combinations retain target and reported cooldown");
        check(bounded, "Every truncation and extra trailer of every skill-start layout is rejected atomically");
        var fight = new Fight { LocalPlayer = 1 };
        fight.Network.AddRange(Decode(Body(0, 0)).Events.Select(e => e.Network!));
        var tracker = new CooldownTracker(); tracker.Observe(fight, 1000, 100);
        check(tracker.Remaining(17010001, 100) == 5, "Skill start seeds an authoritative local cooldown");
        fight.Network.Add(new("skill-cooldown", 1100, "synthetic", JsonSerializer.SerializeToElement(new { ActorId = 2, SkillCode = 17010001, RemainingMs = 9000 })));
        tracker.Observe(fight, 1100, 200);
        check(tracker.Remaining(17010001, 200) == 4.9, "Another actor's skill start cannot replace the local cooldown");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Charge user", true, false, 0, "Cleric", 2301)));
        foreach (var e in Decode(Body(12, 0)).Events) engine.Apply(e);
        var chargeObserver = new CooldownTracker(); chargeObserver.Observe(engine.Snapshot(), 1000, 100);
        check(chargeObserver.Charges(17010001, 100) == new ChargeReading(2, 2, 2) && engine.Fight.Network.Count == 0,
            "Wire skill start applies ordinary cooldown before recharge state and retains no raw charge events");
        var unknown = new EuGameDecoder(); unknown.Decode(new CapturedPayload(13328, 50000, [7, 0x3d, 0x38, 0]));
        check(unknown.UnknownRecords == 1 && unknown.Bundles == 0 && unknown.RejectedRecords == 0,
            "Unidentified 3D 38 records are not falsely expanded as bundles");
    }
}
