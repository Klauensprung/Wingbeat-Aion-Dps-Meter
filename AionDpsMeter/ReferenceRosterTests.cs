using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ReferenceRosterTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        string path = Path.Combine(temp, "reference-roster-synthetic.jsonl");
        using (var fixtures = new ReferencePacketFixtures(path)) {
            long time = 1;
            void Send(ushort opcode, byte[] body) {
                var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(opcode >> 8), (byte)opcode); w.Raw(body);
                fixtures.Add(new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = time++ });
            }
            void Leader(long id) { var w = new PartyWireTests.Writer(); w.Long(id); Send(0x1192, w.Bytes.ToArray()); }
            Send(0x0092, PartyWireTests.Full());
            Leader(6000000001);
            Leader(6000000002); // Unknown until member addition.
            var added = new PartyWireTests.Writer(); added.Member(6000000002, 3, "Third"); Send(0x0d92, added.Bytes.ToArray());
            var reconnect = new PartyWireTests.Writer(); reconnect.Member(6000000002, 30, "Third"); Send(0x2292, reconnect.Bytes.ToArray());
            Send(0x1392, [1, 0]); // Failure does not clear.
            Send(0x1392, [0, 0]);
            Send(0x0296, ForceWireTests.Full());
            Leader(6000000004); // Alliance leader outside first subgroup.
            Send(0x0092, PartyWireTests.Full()); // Subgroup snapshot preserves alliance leader.
            var removed = new PartyWireTests.Writer(); removed.Long(6000000004); removed.Long(457); removed.Raw(0); Send(0x2296, removed.Bytes.ToArray());
            Send(0x1a96, []);
        }
        var rows = File.ReadLines(path).Select(s => JsonDocument.Parse(s)).ToArray();
        try {
            string? Leader(int index) => rows[index].RootElement.GetProperty("Party").GetProperty("LeaderName").GetString();
            check(rows.Length == 12 && Leader(0) == "First" && Leader(1) == "Second" && Leader(2) == null &&
                Leader(3) == "Third" && Leader(4) == "Third" && Leader(6) == null && Leader(7) == "Member 1" &&
                Leader(8) == "Member 5" && Leader(9) == "Member 5" && Leader(10) == null && Leader(11) == null,
                "Reference roster sequence covers wide-ID leaders, deferred resolution, reconnect, departure and alliance subgroup isolation");
        } finally { foreach (var row in rows) row.Dispose(); }
        var original = new Fight { PartyLeaderName = "Saved leader" };
        var store = new FightStore(Path.Combine(temp, "leader-save")); store.Save(original);
        check(store.Load(Path.Combine(store.Root, original.Id + ".json")).PartyLeaderName == "Saved leader",
            "Full fight file round trip preserves party leader name");
        using var statuses = new ReferencePacketFixtures(Path.Combine(temp, "reference-roster-status-synthetic.jsonl"));
        void StatusFrame(ushort op, byte[] body) {
            var frame = new PartyWireTests.Writer(); frame.Var(body.Length + 6); frame.Raw((byte)(op >> 8), (byte)op); frame.Raw(body);
            statuses.Add(new CapturedPayload(13328, 50000, frame.Bytes.ToArray()) { TimestampMs = 100 });
        }
        StatusFrame(0x0092, PartyWireTests.Full());
        foreach (bool alliance in new[] { false, true }) {
            var w = new PartyWireTests.Writer(); w.Long(6000000000); w.Int(25); w.Zero(24); w.Int(alliance ? 52 : 51); w.Zero(8);
            w.Text(alliance ? "" : "Changed guild"); w.Int(0); if (!alliance) w.Bool(true); w.Long(25000); w.Raw(0);
            StatusFrame(alliance ? (ushort)0x2c96 : (ushort)0x1c92, w.Bytes.ToArray());
            StatusFrame(alliance ? (ushort)0x2c96 : (ushort)0x1c92, w.Bytes.ToArray());
        }
    }
}
