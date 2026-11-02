using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class EntityRemovalTests
{
    internal static void Run(Action<bool, string> check)
    {
        var decoder = new EuGameDecoder();
        DecodeResult Decode(byte opcode, byte[] body, long time = 100) => decoder.Decode(new CapturedPayload(13328, 50000,
            [(byte)(body.Length + 6), opcode, 0x36, ..body]) { TimestampMs = time });
        var removed = Decode(0x42, [1, 1, 7, ..BitConverter.GetBytes(99)]).Events.Single().Network!;
        check(removed.Kind == "entity-removed" && removed.Fields.GetProperty("Removal").GetString() == "despawned" &&
            Decode(0x47, [2, 3, 0]).Events.Single().Network!.Fields.GetProperty("Removal").GetString() == "died",
            "NPC and player disappearance retain reason without assuming every removal is a death");
        check(Decode(0x42, [1, 1, 7, 0]).Events.Count == 0 && Decode(0x47, [2, 3]).Events.Count == 0,
            "Incomplete disappearance packets cannot invalidate actor state");
        check(LocalTarget.Apply(new(1, 50), removed)?.EntityId == 0 && LocalTarget.Apply(new(1, 150), removed)?.EntityId == 1 &&
            LocalTarget.Apply(new(2, 50), removed)?.EntityId == 2,
            "Removal clears only the selected entity and cannot overwrite a newer target selection");
        var buffs = new AbnormalAttribution();
        var buff = new AbnormalReading(7, 1000, 1000, 0, 2, 1, 17090000, false, 0, 0, null, null, null);
        buffs.Add(1, buff, 100); buffs.Add(2, buff, 100); buffs.RemoveEntity(1, 200); buffs.Add(1, buff, 150);
        check(buffs.Find(1, 7, 201) == null && buffs.Find(2, 7, 201) != null,
            "Actor removal invalidates its buff identities and blocks stale resurrection without clearing other actors");
        buffs.Add(1, buff, 300); buffs.RemoveEntity(1, 250);
        check(buffs.Find(1, 7, 301) != null, "Delayed removal cannot invalidate a newer entity incarnation's captured buff");
    }
}
