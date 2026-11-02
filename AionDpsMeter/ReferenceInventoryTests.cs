using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ReferenceInventoryTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        string path = Path.Combine(temp, "reference-inventory-synthetic.jsonl");
        using (var fixtures = new ReferencePacketFixtures(path)) {
            long time = 1;
            void Send(long count, byte modification, byte reason, long instance = 100, bool snapshot = false) {
                var w = new PartyWireTests.Writer();
                if (snapshot) w.Raw(0x10, 0x02); // Observed result 528.
                w.Var(1); if (!snapshot) w.Raw(modification);
                w.Raw(0); w.Long(instance); w.Int(110120003); w.Long(count); w.Raw(1); w.Int(0);
                w.Bool(false); w.Text(""); w.Long(0); w.Bool(false);
                if (snapshot) w.Zero(6); else w.Raw(reason, 0, 0);
                var frame = new PartyWireTests.Writer(); frame.Var(w.Bytes.Count + 6); frame.Raw(snapshot ? (byte)0x11 : (byte)0x1b, 0x56); frame.Raw(w.Bytes.ToArray());
                fixtures.Add(new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = time++ });
            }
            Send(50, 1, 5); Send(50, 1, 5); Send(52, 1, 5); // Unknown baseline, duplicate, positive delta.
            Send(60, 1, 19); Send(61, 1, 5); // Purchase changes baseline, next drop gains one.
            Send(61, 2, 0); Send(3, 0, 5); // Removal then explicit addition.
            Send(20, 1, 0, snapshot: true); Send(23, 1, 5);
            for (int reason = 0; reason <= 255; reason++) Send(1, 0, (byte)reason, 1000 + reason);
            // A later depot list does not erase the reference reducer's known
            // counts for instances omitted from that list.
            Send(9, 1, 0, 200, snapshot: true); Send(24, 1, 5);
            Send(30, 1, 0, snapshot: true); Send(31, 1, 5);
            Send(10, 1, 0, 200, snapshot: true); Send(31, 1, 5);
        }
        var rows = File.ReadLines(path).Select(line => JsonDocument.Parse(line)).ToArray();
        try {
            check(rows.Length == 271 && rows[0].RootElement.GetProperty("Loot").GetArrayLength() == 0 &&
                rows[2].RootElement.GetProperty("Loot")[0].GetProperty("Quantity").GetInt64() == 2 &&
                rows[4].RootElement.GetProperty("Loot")[0].GetProperty("Quantity").GetInt64() == 1 &&
                rows[8].RootElement.GetProperty("Loot")[0].GetProperty("Quantity").GetInt64() == 3,
                "Independent-reference inventory fixtures cover unknown and full baselines, duplicates, purchases, removals, additions and all 256 increase reasons");
            check(rows[266].RootElement.GetProperty("Loot")[0].GetProperty("Quantity").GetInt64() == 1 &&
                rows[268].RootElement.GetProperty("Loot")[0].GetProperty("Quantity").GetInt64() == 1 &&
                rows[270].RootElement.GetProperty("Loot").GetArrayLength() == 0,
                "Repeated depot lists preserve omitted-instance counts and refresh included counts without inventing loot");
        } finally { foreach (var row in rows) row.Dispose(); }
        string gearPath = Path.Combine(temp, "reference-gear-synthetic.jsonl");
        using (var fixtures = new ReferencePacketFixtures(gearPath)) {
            long time = 1;
            void Send(bool snapshot, params (long Instance, byte Container, byte Modification, byte Enchant)[] items) {
                var w = new PartyWireTests.Writer();
                if (snapshot) w.Raw(0x10, 0x02);
                w.Var(items.Length);
                foreach (var item in items) {
                    if (!snapshot) w.Raw(item.Modification);
                    w.Raw(1); w.Long(item.Instance); w.Int(110120003); w.Long(1); w.Raw(item.Container); w.Int(0);
                    w.Bool(false); w.Text(""); w.Long(0); w.Bool(false);
                    w.Raw(item.Enchant); w.Zero(8); w.Raw(3); w.Int(0); w.Int(100); w.Bool(false); w.Raw(5);
                    w.Var(1); w.Int(123); w.Raw(19, 0, 2); w.Zero(9);
                    w.Var(1); w.Int(456);
                    for (int i = 0; i < 3; i++) { w.Var(1); w.Raw(19, 0); w.Int(-5); }
                    for (int i = 0; i < 2; i++) { w.Var(1); w.Int(17010000); w.Raw(3); }
                    w.Var(1); w.Raw(19, 0); w.Int(-5);
                    w.Var(1); w.Int(17010000); w.Raw(3); w.Raw(0);
                    if (!snapshot) w.Raw(0, 0, 0);
                }
                if (snapshot) w.Zero(6);
                var frame = new PartyWireTests.Writer(); frame.Var(w.Bytes.Count + 6); frame.Raw(snapshot ? (byte)0x11 : (byte)0x1b, 0x56); frame.Raw(w.Bytes.ToArray());
                fixtures.Add(new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = time++ });
            }
            Send(false, (100, 11, 0, 1)); // Equipped delta before any full inventory.
            Send(true, (200, 11, 1, 2), (300, 1, 1, 0)); // Full replacement.
            Send(false, (200, 11, 1, 4)); // Enchant update.
            Send(false, (300, 11, 1, 5)); // Equip from bag.
            Send(false, (200, 1, 1, 4)); // Unequip into bag.
            Send(false, (300, 11, 2, 5)); // Remove equipped instance.
            Send(false, (400, 11, 0, 6));
            Send(true); // Full empty replacement.
        }
        var gearRows = File.ReadLines(gearPath).Select(line => JsonDocument.Parse(line)).ToArray();
        try {
            check(gearRows.Select(row => row.RootElement.GetProperty("Gear").GetArrayLength()).SequenceEqual(new[] { 1, 1, 1, 2, 1, 0, 1, 0 }) &&
                gearRows[2].RootElement.GetProperty("Gear")[0].GetProperty("Details").GetProperty("Enchant").GetByte() == 4,
                "Reference gear fixtures exercise initial delta, full replacement, enchant update, equip, unequip, removal and empty inventory");
        } finally { foreach (var row in gearRows) row.Dispose(); }
    }
}
