using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ConversionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        byte[] Body(bool full, ushort result = 0, bool empty = false) {
            var w = new PartyWireTests.Writer(); w.Raw(BitConverter.GetBytes(result));
            if (full) {
                w.Raw(0, 0); w.Var(empty ? 0 : 1); if (!empty) { w.Int(31070001); w.Int(5); }
                w.Var(empty ? 0 : 1); if (!empty) { w.Int(31070002); w.Int(2); }
            } else {
                w.Var(empty ? 0 : 2);
                if (!empty) foreach (int id in new[] { 31070001, 31070002 }) { w.Int(id); w.Zero(26); w.Int(6); w.Int(3); }
            }
            return w.Bytes.ToArray();
        }
        byte[] Frame(bool full, byte[] body) { var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(full ? (byte)0x8f : (byte)0x8b, 0x8d); w.Raw(body); return w.Bytes.ToArray(); }
        DecodeResult Decode(bool full, byte[] body) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000, Frame(full, body)) { TimestampMs = 100 });
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-conversion-synthetic.jsonl"));
        foreach (bool full in new[] { true, false }) {
            byte[] body = Body(full);
            check(Decode(full, body).Events.Count == 1 && Enumerable.Range(0, body.Length).All(n => Decode(full, body[..n]).Events.Count == 0) && Decode(full, [..body, 0]).Events.Count == 0,
                "Conversion counters reject every truncation/trailer for layout " + full);
            check(Decode(full, Body(full, 1)).Events.Count == 0 && Decode(full, Body(full, empty: true)).Events.Count == (full ? 0 : 1),
                "Conversion failures and empty-list notification behavior match reference " + full);
            foreach (var data in new[] { body, Body(full, 1), Body(full, empty: true) }) fixtures.Add(new CapturedPayload(13328, 50000, Frame(full, data)) { TimestampMs = 100 });
        }
        string path = Path.Combine(temp, "crafting-checklist.json"); var tracker = new CraftingChecklist(path);
        long now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        using var checklist = new StreamWriter(Path.Combine(temp, "reference-crafting-checklist.jsonl"));
        void Apply(string name, int server, params ConversionCount[] counts) {
            var batch = new ConversionBatch(counts); tracker.Apply(new("morph-counts", now++, "test", JsonSerializer.SerializeToElement(batch)), new(1, name, true, true, 0, "Cleric", server));
            checklist.WriteLine(JsonSerializer.Serialize(new { Kind = "morph-counts", Server = server, Character = name, Timestamp = now - 1, Batch = batch,
                Expected = tracker.Snapshot().Where(r => r.ResetAt > now - 1).Select(r => new { Owner = r.Character == "" ? $"server:{r.Server}" : $"{r.Server}:{r.Character}", TaskId = r.Task, r.Count, r.ResetAt }) }));
        }
        Apply("First", 2301, new ConversionCount(31070001, 5, 99), new ConversionCount(31070002, 99, 2));
        Apply("Second", 2301, new ConversionCount(31070001, 6, 99), new ConversionCount(31070002, 99, 1));
        check(tracker.Snapshot().Length == 3 && tracker.Snapshot().Single(r => r.Character == "").Count == 6 && tracker.Snapshot().Single(r => r.Character == "First").Count == 2,
            "Crafting checklist selects account vs character counts without mixing character scope");
        Apply("First", 2301, new ConversionCount(32070002, 99, 3), new ConversionCount(31070002, 99, 4));
        check(tracker.Snapshot().Single(r => r.Character == "First").Count == 4, "Crafting recipe aliases replace rather than sum a task total in packet order");
        Apply("First", 2301, new ConversionCount(31070002, 99, null), new ConversionCount(9999, 50, 50));
        Apply("First", 1301, new ConversionCount(31070001, 2, 99), new ConversionCount(31070002, 99, 0));
        tracker.Save(); check(new CraftingChecklist(path).Snapshot().SequenceEqual(tracker.Snapshot()), "Crafting counters persist with scope and reset period");
        long reset = ChecklistTasks.WeeklyReset(2301, now)!.Value; now = reset;
        Apply("First", 2301, new ConversionCount(31070002, 99, 1));
        check(tracker.Rows(now).Single(r => r.Server == 2301 && r.Owner == "First").Count == 1 && tracker.Rows(now).Any(r => r.Count == null),
            "Crafting rollover replaces the updated task and leaves uncaptured new-period totals unknown");
        var engine = new CombatEngine(crafting: tracker); engine.Apply(new("local-player", Actor: new(1, "First", true, true, 0, "Cleric", 2301)));
        foreach (var e in Decode(false, Body(false)).Events) engine.Apply(e);
        check(engine.Loot.Rows().Length == 0 && engine.Fight.Network.Count == 0, "Conversion counters do not create loot or retain raw packet history");
        string invalid = Path.Combine(temp, "invalid-crafting.json"); File.WriteAllText(invalid, "[{\"Server\":-1}]");
        check(new CraftingChecklist(invalid).Snapshot().Length == 0 && File.ReadAllText(invalid) == "[{\"Server\":-1}]",
            "Malformed crafting cache cannot crash startup or delete the source file");
        var bounded = new CraftingChecklist();
        for (int i = 0; i < 1100; i++) bounded.Apply(new("morph-counts", now + i, "test", JsonSerializer.SerializeToElement(new ConversionBatch([new(31070002, null, 1)]))),
            new(1, "Character " + i, true, true, 0, "Cleric", 2301));
        check(bounded.Snapshot().Length == 1024, "Crafting checklist retention is bounded across character changes");
    }
}
