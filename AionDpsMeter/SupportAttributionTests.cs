using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class SupportAttributionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        var engine = new CombatEngine();
        void Actor(int id, string name, bool player, int? owner = null) => engine.Apply(new("actor",
            Actor: new(id, name, player, false, 0, "Unknown", player ? 1 : 0) { NpcId = player ? 0 : 2000001, OwnerId = owner }));
        void Event(string kind, long time, object fields) => engine.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(fields))));
        void Effect(long time, int caster) => Event("abnormal-apply", time,
            new { TargetId = 1, Slot = 7, AbnormalId = 1000, DurationMs = 1000, CasterId = caster, SourceSkillId = 17090000 });
        void Shield(long time, long amount, int caster) => Event("barrier", time,
            new { TargetId = 1, Uid = 7, AbnormalId = 1000, Remaining = amount, ActorId = caster, SkillCode = 17090000 });
        Actor(1, "Recipient", true); Actor(2, "Owner", true); Actor(10, "Summon", false, 2);
        engine.RecordDamage(new() { Timestamp = 1000, ActorId = 1, TargetId = 20, Damage = 1 }, false);
        Effect(1000, 10); Shield(1000, 100, 10); Shield(1200, 60, 10);
        engine.RecordDamage(new() { Timestamp = 5000, ActorId = 1, TargetId = 20, Damage = 1 }, false);
        var before = engine.Snapshot();
        check(EffectAnalysis.Summarize(before) is [{ CasterId: 2, TrackedMs: 1000 }] &&
            ShieldAnalysis.Summarize(before) is [{ CasterId: 2, Added: 100, Consumed: 40 }],
            "Summon effects and shield capacity changes credit the owner captured on observation");
        check(before.Network.First(e => e.Kind == "abnormal-apply").Fields.GetProperty("CasterId").GetInt32() == 10,
            "Support attribution preserves the actual caster for live effects and packet evidence");
        Actor(2, "Replacement", true);
        Effect(3000, 2); Shield(3000, 80, 2);
        var after = engine.Snapshot();
        var effects = EffectAnalysis.Summarize(after); var shields = ShieldAnalysis.Summarize(after);
        check(effects.Length == 2 && effects.All(e => e.TrackedMs == 1000) &&
            effects.Any(e => after.Name(e.CasterId) == "Owner") && effects.Any(e => after.Name(e.CasterId) == "Replacement") &&
            shields.Single(e => after.Name(e.CasterId) == "Owner") is { Added: 100, Consumed: 40 } &&
            shields.Single(e => e.CasterId == 2).Added == 20,
            "Owner identity reuse preserves old support attribution and accepts new effects from the replacement");
        check(EffectAnalysis.Summarize(before) is [{ CasterId: 2 }] && before.Name(2) == "Owner",
            "Support identity archival does not mutate prior snapshots");
        var store = new FightStore(Path.Combine(temp, "support-attribution")); store.Save(after);
        var saved = store.Load(Path.Combine(store.Root, after.Id + ".json"));
        check(EffectAnalysis.Summarize(saved).SequenceEqual(effects) && ShieldAnalysis.Summarize(saved).SequenceEqual(shields) &&
            saved.Network.All(e => e.Kind is not ("barrier" or "abnormal-apply")),
            "Attributed support summaries survive compact save/load without raw effect or shield histories");
    }
}
