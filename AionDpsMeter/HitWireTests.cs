using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class HitWireTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(ushort flags, int effect = 1234, byte hitType = 3) {
            var b = new List<byte>(); int bitIndex = -1, bit = 8;
            void U32(int value) => b.AddRange(BitConverter.GetBytes(value));
            void Number(long value) { ulong n = unchecked((ulong)value); do { byte v = (byte)(n & 127); n >>= 7; b.Add((byte)(v | (n == 0 ? 0 : 128))); } while (n > 0); }
            void Boolean(bool value) { if (bit == 8) { bitIndex = b.Count; b.Add(0); bit = 0; } if (value) b[bitIndex] |= (byte)(1 << bit); bit++; }
            b.Add(100); b.AddRange(BitConverter.GetBytes(flags)); b.Add(1); U32(17010001); b.Add(3);
            if ((flags & 1) != 0) U32(7);
            b.Add(hitType);
            if ((flags & 2) != 0) { for (int i = 0; i < 6; i++) Boolean(i % 2 == 0); Number(5000000000); Boolean(true); b.Add(1); }
            U32(effect); U32(2); Number(1000);
            if ((flags & 4) != 0) Number(6000000000);
            if ((flags & 8) != 0) U32(12);
            if ((flags & 16) != 0) Boolean(true);
            if ((flags & 32) != 0) { b.Add(2); Number(100); Number(200); }
            if ((flags & 64) != 0) { b.Add(1); U32(7); }
            if ((flags & 128) != 0) b.Add(2);
            if ((flags & 256) != 0) { b.Add(1); U32(9); }
            b.AddRange(new byte[] { 0x34, 0x12 });
            if ((flags & 512) != 0) Number(20);
            if ((flags & 1024) != 0) Number(30);
            if ((flags & 2048) != 0) U32(8);
            if ((flags & 4096) != 0) Boolean(true);
            if ((flags & 8192) != 0) U32(44);
            return b.ToArray();
        }
        HitReading Read(byte[] body) { var reader = new WireReader(body); return HitWire.Read(ref reader); }
        bool valid = true, bounded = true;
        foreach (ushort flags in new ushort[] { 0, 4, 6, 0x3fff }.Concat(Enumerable.Range(0, 14).Select(i => (ushort)(1 << i)))) {
            byte[] body = Body(flags); var hit = Read(body);
            valid &= hit.TargetId == 100 && hit.ActorId == 1 && hit.EffectId == 1234 && hit.AttackSpeed == 1000 &&
                hit.Amount == ((flags & 4) != 0 ? 6000000000 : 0) && hit.HpDrain == ((flags & 512) != 0 ? 20 : 0);
            for (int i = 0; i < body.Length; i++) { try { Read(body[..i]); bounded = false; } catch (InvalidDataException) { } }
            try { Read([..body, 0]); bounded = false; } catch (InvalidDataException) { }
        }
        check(valid && bounded, "Complete hit wire handles optional fields and 64-bit amounts while rejecting every tested truncation and trailer");
        var full = Read(Body(0x3fff));
        check(full.ShieldBlock && !full.WeaponBlock && full.Perfect && !full.HardHit && full.IronWall && !full.Restoration && full.PerfectBlock &&
            full.RestoredHp == 5000000000 && full.Angle == 1 && full.AdditionalHits.SequenceEqual(new long[] { 100, 200 }) && full.HpRecovery == 30,
            "Hit boolean state remains aligned across restoration, sealstone and barrier fields, including crossing a boolean byte");
        var negative = new WireReader([255, 255, 255, 255, 255, 255, 255, 255, 255, 1]);
        check(HitWire.Number(ref negative) == -1 && negative.Remaining == 0, "Signed 64-bit wire varints preserve negative values for semantic validation");
        bool overflow = false; var invalid = new WireReader([255, 255, 255, 255, 255, 255, 255, 255, 255, 2]);
        try { HitWire.Number(ref invalid); } catch (InvalidDataException) { overflow = true; }
        check(overflow, "Hit varints reject values wider than the wire's 64 bits");
        DecodeResult Decode(byte[] body) {
            var prefix = new List<byte>(); int length = body.Length + 6;
            do { byte b = (byte)(length & 127); length >>= 7; prefix.Add((byte)(b | (length == 0 ? 0 : 128))); } while (length > 0);
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, [..prefix, 4, 0x38, ..body]) { TimestampMs = 1000 });
        }
        var decoded = Decode(Body(0x3fff));
        check(decoded.Events is [{ Kind: "damage", Damage.Damage: 6000000000 }, { Kind: "heal", Damage: { TargetId: 1, Damage: 50 } }],
            "Production hit decoding counts primary damage once and combines drain/recovery as a separate caster heal");
        check(Decode(Body(4, 41)).Events.Single() is { Kind: "heal", Damage.TargetId: 100 } && Decode(Body(4, 51)).Events.Count == 0,
            "The same skill can heal or change resources according to its packet effect ID");
        check(Decode(Body(0, hitType: 1)).Events.Single().Defense?.Outcome == DefenseOutcome.Evade &&
            Decode(Body(0, hitType: 6)).Events.Single().Defense?.Outcome == DefenseOutcome.Resist,
            "Zero-amount evade and resist effects produce defense events rather than malformed hit errors");
        check(!Decode(Body(4, hitType: 9)).Events.Single().Damage!.IsCritical,
            "Critical classification uses hit type 3, not the old arithmetic approximation");
        var engine = new CombatEngine(); foreach (var e in decoded.Events) engine.Apply(e);
        var store = new FightStore(Path.Combine(temp, "wide-hit")); var fight = engine.Snapshot(); store.Save(fight);
        var saved = store.Load(Path.Combine(store.Root, fight.Id + ".json"));
        check(saved.Damage.Single().Damage == 6000000000 && Analysis.Skills(saved, _ => "Synthetic skill").Single().Damage == 6000000000 &&
            saved.Healing.Single().Damage == 50 && Analysis.Timeline(saved, new[] { 1 }).Single().Damage == 6000000000,
            "64-bit decoded damage survives engine, disk, skill totals and timeline projections");
    }
}
