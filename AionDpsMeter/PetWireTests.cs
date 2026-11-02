using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PetWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] U(uint value) => BitConverter.GetBytes(value);
        DecodeResult Decode(byte high, params byte[] payload) {
            byte[] frame = [(byte)(payload.Length + 6), high, 0x90, ..payload];
            return new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, frame) { TimestampMs = 1700000000000 });
        }
        byte[] roster = [..U(1002), 2, ..U(1002), ..U(1002), ..U(1), ..U(1005), ..U(1005), ..U(2), 2, ..U(1002), ..U(5), ..U(1005), ..U(32), 0, 0, 0, 0];
        var parsed = Decode(0, roster).Events.Single().Network!;
        check(parsed.Kind == "pet-roster" && parsed.Fields.GetProperty("ActivePetId").GetInt32() == 1002 &&
            parsed.Fields.GetProperty("Pets").GetArrayLength() == 2 && parsed.Fields.GetProperty("Progress")[1].GetProperty("Souls").GetInt32() == 32 &&
            parsed.Fields.GetProperty("TrailingBytes").GetInt32() == 0,
            "Pet roster decoding preserves levels and absolute progress after validating the complete trailer");
        byte[] empty = [..U(0), 0, 0, 0, 0, 0, 0];
        check(Decode(0, empty).Events.Single().Network!.Fields.GetProperty("Pets").GetArrayLength() == 0,
            "An empty pet roster is a valid authoritative snapshot");
        byte[] board = [..U(0), 0, 0, 1, 2, ..U(7), 1, ..U(123),
            1, 2, 2, ..U(3), ..new byte[8], 1, 0, 2,
            0, 1, 2, 0, ..new byte[8], 0x09,
            1, 2, 3, 0, ..new byte[8], 0];
        check(Decode(0, board).Events.Count == 1,
            "Pet knowledge maps and multiple stat slots use a shared packed-boolean byte");
        var genus = System.Text.Json.JsonSerializer.Deserialize<CapturedPetRoster>(Decode(0, board).Events.Single().Network!.Fields)!.Genera!.Single();
        check(genus is { Type: 2, Level: 3, Panel: 0, Effects.Length: 2 } && genus.Effects[0] is { Locked: true, Activated: false } && genus.Effects[1] is { Locked: false, Activated: true },
            "Selected pet genus panel retains independent lock/activation flags sharing a packed boolean byte");
        check(Enumerable.Range(0, board.Length).All(n => Decode(0, board[..n]).Events.Count == 0) &&
            Decode(0, [..board, 0]).Events.Count == 0,
            "Every truncated pet board and unknown trailing data is rejected atomically");
        var progress = Decode(0x0d, [2, ..U(1002), ..U(1), ..U(1005), ..U(2)]).Events.Single().Network!;
        check(progress.Kind == "pet-progress" && progress.Fields.GetProperty("Gains")[1].GetProperty("Souls").GetInt32() == 2,
            "Experimental pet progress decoding distinguishes per-pet soul gains from an absolute roster snapshot");
        var level = Decode(0x14, [0x34, 0x12, ..U(1005), ..U(3)]).Events.Single().Network!;
        check(level.Kind == "pet-level" && level.Fields.GetProperty("Level").GetInt32() == 3,
            "Experimental pet level updates preserve the reported level without interpreting prefix bytes");
        byte[] acquired = [1, ..U(1005), ..U(1), 0, ..U(1), ..new byte[8]];
        var added = Decode(0x09, acquired).Events.Single().Network!;
        check(added.Kind == "pet-added" && added.Fields.GetProperty("EntityId").GetInt32() == 1 &&
            added.Fields.GetProperty("PetId").GetInt32() == 1005 && Decode(0x09, acquired[..^1]).Events.Count == 0,
            "Pet acquisition decodes actor, pet, level and the complete creature-board trailer");
        check(Decode(0x0e, [1, ..U(1005), ..U(2000)]).Events.Single().Network!.Kind == "pet-progress-decrease",
            "Pet soul spending has a distinct event and permits amounts greater than 999");
        check(Decode(0, [..U(0), 1, ..U(1002), ..U(1005), ..U(1), 0]).Events.Count == 0 &&
            Decode(0, [..U(0), 1, ..U(1002), ..U(1002), ..U(4), 0]).Events.Count == 0 && Decode(0, roster[..20]).Events.Count == 0,
            "Pet roster decoding rejects mismatched identity fields, impossible levels and truncated records atomically");
        check(Decode(0x0d, [2, ..U(1002), ..U(1), ..U(1005), ..U(1000001)]).Events.Count == 0 &&
            Decode(0x0d, [1, ..U(1002), ..U(1), 0]).Events.Count == 0 && Decode(0x14, [0, 0, ..U(1005), ..U(0)]).Events.Count == 0,
            "Malformed progress batches, unknown trailers and invalid level changes emit no partial pet data");
    }
}
