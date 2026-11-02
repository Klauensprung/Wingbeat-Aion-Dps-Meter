using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PetWireProgressionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        // Export both bytes and engine checkpoints so independent readers and
        // the installed progression reducer can verify the same sequence.
        using var wire = new ReferencePacketFixtures(Path.Combine(temp, "pet-progression-wire.jsonl"));
        using var checkpoints = new StreamWriter(Path.Combine(temp, "pet-progression-checkpoints.jsonl"));
        var engine = new CombatEngine(); var decoder = new EuGameDecoder(); long at = 1000;
        engine.Apply(new("local-player", Actor: new(1, "Local", true, true, 0, "Cleric", 2301)));
        byte[] U(int n) => BitConverter.GetBytes(n);
        void Send(byte opcode, byte[] body)
        {
            byte[] frame = [(byte)(body.Length + 6), opcode, 0x90, ..body];
            var payload = new CapturedPayload(13328, 50000, frame) { TimestampMs = ++at };
            wire.Add(payload);
            var decoded = decoder.Decode(payload);
            if (decoded.Events.Count != 1) throw new InvalidDataException("Expected one pet progression event.");
            var message = decoded.Events[0]; engine.Apply(message);
            checkpoints.WriteLine(JsonSerializer.Serialize(new { EventIndex = at, Event = message,
                HasRoster = engine.Fight.Pets?.HasRoster ?? false,
                Pets = engine.Fight.Pets?.Pets.OrderBy(p => p.PetId).Select(p => new { p.PetId, p.Level, p.Souls }).ToArray(),
                Gains = engine.SoulTracking.Snapshot().Counts,
                Registered = engine.PetRegistrationTracking.Snapshot().Counts,
                LevelUps = engine.PetLevelTracking.Snapshot().Counts }));
        }
        void Spend(int amount) => Send(0x0e, [1, ..U(1002), ..U(amount)]);
        void Added(int level) => Send(0x09, [1, ..U(1002), ..U(level), 0, ..U(1), ..new byte[8]]);
        for (int order = 0; order < 2; order++) {
            Send(0, [..U(0), 0, 1, ..U(1002), ..U(105), 0, 0, 0, 0]);
            if (order == 0) { Added(1); Spend(5); } else { Spend(5); Added(1); }
            check(engine.Fight.Pets!.Pets.Single() is { Level: 1, Souls: 100 } &&
                engine.PetRegistrationTracking.Snapshot().Counts[1002] == order + 1,
                "Decoded acquisition/spend ordering registers a pet once and preserves its soul balance");
            check(engine.DrainPetNotices(at) is [{ PetId: 1002, Level: 1, Acquired: true }],
                "Decoded first acquisition is announced as collected in both spending orders");
            Spend(10); Spend(15); Added(2); Added(2);
            check(engine.Fight.Pets!.Pets.Single() is { Level: 2, Souls: 75 } &&
                engine.PetLevelTracking.Snapshot().Counts[1002] == order * 2 + 1,
                "Decoded split spending infers level two without duplicate confirmation counts");
            check(engine.DrainPetNotices(at) is [{ PetId: 1002, Level: 2, Acquired: false }],
                "Decoded duplicate upgrade confirmations produce one level-up notice");
            Spend(75); Added(3); Added(3);
            check(engine.Fight.Pets!.Pets.Single() is { Level: 3, Souls: 0 } &&
                engine.PetLevelTracking.Snapshot().Counts[1002] == (order + 1) * 2,
                "Decoded final upgrade reaches zero souls and counts level three once");
            check(engine.DrainPetNotices(at) is [{ PetId: 1002, Level: 3, Acquired: false }],
                "Decoded final upgrade confirmation produces one level-up notice");
            Send(0x0d, [1, ..U(1002), ..U(2)]);
            check(engine.Fight.Pets!.Pets.Single() is { Level: 3, Souls: 2 } &&
                engine.SoulTracking.Snapshot().Counts[1002] == (order + 1) * 2,
                "Decoded gains after upgrading update balance and farming soul totals independently");
        }
    }
}
