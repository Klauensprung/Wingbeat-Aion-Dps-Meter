using System.Buffers.Binary;
using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed class HitWireAudit
{
    readonly Dictionary<string, GameRecordStream> streams = new();
    internal long Records, Parsed, Rejected, PositiveAmounts, ExistingAmountMatches, ExistingAmountDiffers, ExistingMissing;
    internal long CriticalFlagDifferences, DirectionDifferences;
    internal readonly Dictionary<string, int> Errors = new();
    internal readonly Dictionary<int, int> MissingEffects = new();
    internal void Add(CapturedPayload payload) {
        if (!streams.TryGetValue(payload.Connection, out var stream)) streams[payload.Connection] = stream = new();
        foreach (var record in stream.Push(payload.Data)) Read(record, payload, 0);
    }
    void Read(byte[] record, CapturedPayload payload, int depth) {
        if (depth > 4) return;
        var reader = new WireReader(record); reader.Var();
        ushort opcode = BinaryPrimitives.ReadUInt16BigEndian(reader.Take(2));
        if (opcode == 0xffff) {
            int length = checked((int)reader.U32()); if (length > GameRecordStream.Limit * 8) return;
            var body = Lz4Block.Expand(reader.Take(reader.Remaining), length);
            foreach (var child in new GameRecordStream().Push(body)) Read(child, payload, depth + 1);
            return;
        }
        if (opcode != 0x0438) return;
        Records++;
        try {
            var hit = HitWire.Read(ref reader); Parsed++;
            if (hit.Amount <= 0) return;
            PositiveAmounts++;
            var old = new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, record) { TimestampMs = payload.TimestampMs }).Events.FirstOrDefault(e => e.Damage != null)?.Damage;
            if (old == null) { ExistingMissing++; MissingEffects[hit.EffectId] = MissingEffects.GetValueOrDefault(hit.EffectId) + 1; return; }
            if (old.Damage == hit.Amount) ExistingAmountMatches++; else ExistingAmountDiffers++;
            if (old.IsCritical != (hit.HitType == 3)) CriticalFlagDifferences++;
            if (old.IsBackAttack != (hit.Angle == 1) || old.IsFrontAttack != (hit.Angle == 2)) DirectionDifferences++;
        } catch (Exception ex) when (ex is InvalidDataException or OverflowException) {
            Rejected++; Errors[ex.Message] = Errors.GetValueOrDefault(ex.Message) + 1;
        }
    }
    internal object Report() => new { Records, Parsed, Rejected, PositiveAmounts, ExistingAmountMatches, ExistingAmountDiffers,
        ExistingMissing, CriticalFlagDifferences, DirectionDifferences, Errors, MissingEffects,
        Note = "Structural comparison only. Existing parser can classify a primary amount as healing; effect-ID semantics need a separate audit." };
}
