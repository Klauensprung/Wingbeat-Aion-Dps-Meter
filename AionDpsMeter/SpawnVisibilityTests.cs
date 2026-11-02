using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class SpawnVisibilityTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        using var fixtures = new ReferencePacketFixtures(Path.Combine(temp, "reference-spawn-visibility.jsonl"));
        bool preserved = true;
        foreach (byte reason in Enumerable.Range(0, 17).Select(i => (byte)i))
        foreach (bool full in new[] { false, true })
        foreach (bool npc in new[] { false, true }) {
            var body = npc ? NpcSpawnWireTests.Body(100, 2000001, all: full, reason: reason) : PlayerSpawnWireTests.Body(100, "Synthetic", full, reason: reason);
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(npc ? (byte)0x41 : (byte)0x45, 0x36); w.Raw(body);
            var packet = new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 5000 };
            fixtures.Add(packet);
            var fields = new EuGameDecoder().Decode(packet).Events.Single(e => e.Network?.Kind == "actor-position").Network!.Fields;
            preserved &= fields.GetProperty("VisibilityReason").GetInt32() == reason && fields.GetProperty("EntityKind").GetString() == (npc ? "npc" : "player");
        }
        check(preserved, "Minimal and fully populated NPC/player spawns preserve all 17 appearance reasons for visibility inference");
    }
}
