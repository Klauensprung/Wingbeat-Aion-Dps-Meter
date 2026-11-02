using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class LiveOverlaySessionTests
{
    internal static void Run(Action<bool, string> check)
    {
        var engine = new CombatEngine();
        var actor = new Actor(1, "Same character", true, false, 0, "Cleric", 2301);
        engine.Apply(new("local-player", Actor: actor));
        var effects = new LiveEffectTracker();
        var cooldowns = new CooldownTracker(); var party = new PartyDebuffTracker(); var session = new LiveOverlaySession();
        void Observe(long at) { lock (engine.Gate) session.Observe(engine, engine.Snapshot(), cooldowns, effects, party, at, at + 990); }
        void Send(string kind, long at, object fields) => engine.Apply(new("network", Network: new(kind, at, "test", JsonSerializer.SerializeToElement(fields))));
        void Buff(long at) => Send("abnormal-apply", at, new { TargetId = 1, Slot = 1, DurationMs = 60000, AbnormalId = 10, SourceSkillId = 17010001, CasterId = 1 });
        Buff(10); Send("skill-cooldown", 10, new { ActorId = 1, SkillCode = 17010001, RemainingMs = 60000 }); Observe(10);
        check(effects.Active(1000).Length == 1, "Live overlay regression starts with an active local effect");
        check(cooldowns.Remaining(17010001, 1000) == 60 && party.RetainedSlots > 0, "Scene reset regression includes observed cooldown and party effect state");
        Send("account-login", 20, new LoginReading(0, 2301, []));
        Observe(20);
        check(effects.Active(1010).Length == 0, "Live effects clear on login even though account packets are excluded from retained history");
        check(cooldowns.ObservedSkills.Length == 0 && party.RetainedSlots == 0, "Login clears cooldowns and party alert state alongside local effects");
        Observe(21);
        check(effects.Active(1011).Length == 0, "Pending login cannot replay old effects from the still displayed fight");
        engine.Apply(new("local-player", Actor: actor)); Buff(30); Observe(30);
        check(effects.Active(1020).Length == 1, "Same-character re-entry accepts fresh effects after scene reset");
        long revision = engine.LiveSceneRevision; engine.NewFight("Manual reset"); Observe(31);
        check(engine.LiveSceneRevision == revision && effects.Active(1021).Length == 1, "Manual DPS reset preserves active local effects without advancing the scene revision");
        Send("account-login", 40, new LoginReading(0, 2301, []));
        engine.Apply(new("local-player", Actor: actor)); Observe(41);
        check(effects.Active(1031).Length == 0, "Reconnect entirely between UI refreshes still clears same-name same-ID effects");
        Buff(39); Send("skill-cooldown", 39, new { ActorId = 1, SkillCode = 17010001, RemainingMs = 60000 }); Observe(42);
        check(effects.Active(1032).Length == 0 && cooldowns.ObservedSkills.Length == 0 && party.RetainedSlots == 0,
            "Delayed old-session effects and cooldowns cannot refill freshly reset overlays");
        Buff(50); Observe(50);
        Send("local-zone", 60, new { MapId = 1010, AwaitingIdentity = true }); Observe(60);
        check(effects.Active(1050).Length == 0, "Map preparation suppresses live effects until identity confirmation");
    }
}
