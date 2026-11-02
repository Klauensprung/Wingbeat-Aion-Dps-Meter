using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PartySummaryTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-party-summaries.jsonl"));
        var decoder = new EuGameDecoder();
        DecodeResult Send(ushort opcode, byte[] body) {
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw((byte)(opcode >> 8), (byte)opcode); w.Raw(body);
            var packet = new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 1000 };
            fixtures.Add(packet); return decoder.Decode(packet);
        }
        var full = Send(0x0092, PartyWireTests.Full());
        void CheckStatus(bool alliance) {
            byte[] Status(int gear, long id = 6000000000) {
                var w = new PartyWireTests.Writer(); w.Long(id); w.Int(25); w.Zero(24); w.Int(50); w.Int(0); w.Int(gear); w.Text("Guild"); w.Int(0);
                if (!alliance) w.Bool(true); w.Long(12345); w.Raw(1); return w.Bytes.ToArray();
            }
            ushort opcode = alliance ? (ushort)0x2c96 : (ushort)0x1c92;
            var engine = new CombatEngine();
            foreach (var e in (alliance ? Send(0x0296, ForceWireTests.Full()) : Send(0x0092, PartyWireTests.Full())).Events) engine.Apply(e);
            foreach (int score in new[] { 321, 0, 65536 }) {
                var update = Send(opcode, Status(score)); foreach (var e in update.Events) engine.Apply(e);
                check(engine.Fight.Actors[1].Metadata is { Level: 50, Guild: "Guild" } m && m.GearScore == score && m.GearScoreAt == 1000 && engine.Fight.Actors[1].Cp == 12345,
                    "Gear-only " + (alliance ? "alliance" : "party") + " status updates preserve other actor metadata: " + score);
                check(Send(opcode, Status(score)).Events.Count == 0, "Unchanged member status does not republish actor state");
            }
            check(Send(opcode, Status(999, 6999999999)).Events.Count == 0, "Status for an unknown persistent member does not invent an actor");
        }
        check(full.Events.Single(e => e.Party != null).Party is { Timestamp: 1000, Summaries.Length: 2 } roster && roster.Summaries.All(m => m.GearScore == 100 && m.Level == 50 && m.CombatPower == 12345),
            "Party snapshots preserve compact gear score, level and combat power for run roster summaries");
        var offline = new PartyWireTests.Writer(); offline.Member(6000000009, 0, "Offline member");
        var added = Send(0x0d92, offline.Bytes.ToArray());
        check(added.Events.Single(e => e.Party != null).Party is { Members.Length: 2, Summaries.Length: 3 } all && all.Summaries.Any(m => m.Name == "Offline member") && added.Events.All(e => e.Actor == null),
            "Offline members without combat entity IDs retain roster metadata without creating phantom combat actors");
        CheckStatus(false);
        var force = Send(0x0296, ForceWireTests.Full());
        check(force.Events.Single(e => e.Party != null).Party!.Summaries is { Length: 6 } members && members.All(m => m.GearScore == 100),
            "Alliance snapshots read gear score at its distinct packed-boolean field position");
        CheckStatus(true);
        Send(0x1f92, [0]);
        var classes = new HashSet<string>();
        foreach (int template in Enumerable.Range(0, 50).Append(int.MaxValue)) {
            var member = new PartyWireTests.Writer(); member.Member(7000000000, 0, "Class variants", template);
            var update = Send(0x2292, member.Bytes.ToArray());
            classes.Add(update.Events.Single(e => e.Party != null).Party!.Summaries.Single().Job);
        }
        check(classes.SetEquals(new[] { "Unknown", "Gladiator", "Templar", "Ranger", "Assassin", "Spiritmaster", "Sorcerer", "Cleric", "Chanter", "Brawler" }),
            "Independent roster fixtures cover every class group, four template variants and unknown template boundaries");
    }
}
