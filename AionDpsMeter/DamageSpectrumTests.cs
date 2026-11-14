using System.Text.Json;
namespace AionDpsMeter.Local;
internal static class DamageSpectrumTests
{
    internal static void Run(Action<bool, string> check)
    {
        DamageBucket[] burst = [new(0, 1, 90), new(1, 2, 0), new(2, 3, 0), new(3, 4, 0)];
        var trend = DamageSpectrum.Trend(burst, 3);
        check(trend.Select(p => p.Dps).SequenceEqual(new[] { 90d, 45d, 30d, 0d }), "Spectrum rolling DPS uses elapsed opening time and returns to zero after the window");
        check(DamageSpectrum.Trend(burst, 1).Select(p => p.Dps).SequenceEqual(burst.Select(b => b.Dps)) && burst.Sum(b => b.Damage) == 90, "One-second spectrum retains raw samples and trend never changes range totals");
        var future = DamageSpectrum.Trend([new(0, 1, 0), new(1, 2, 0), new(2, 3, 900)], 3);
        check(future[0].Dps == 0 && future[1].Dps == 0 && future[2].Dps == 300, "Spectrum rolling line never pulls future burst damage into earlier samples");
        var partial = DamageSpectrum.Trend([new(0, 1, 90), new(1, 2, 0), new(2, 2.5, 30)], 1);
        check(partial[^1] is { From: 1.5, To: 2.5, Dps: 30 } && DamageSpectrum.Trend([], 3).Length == 0, "Spectrum rolling windows handle partial final samples and empty data");
        var fight = new Fight { LocalPlayer = 1 };
        fight.Actors[1] = new(1, "Same", true, true, 0, "Chanter", 1);
        fight.Actors[2] = new(2, "Same", true, true, 0, "Chanter", 1);
        fight.Actors[10] = new(10, "Boss", false, false, 0, "Unknown", 0) { CapturedBoss = true };
        void Hit(int actor, int target, int second, int amount, bool heal = false) => fight.Damage.Add(new() { ActorId = actor, TargetId = target, Timestamp = 1000 + second * 1000, Damage = amount, IsHeal = heal });
        Hit(1, 10, 0, 100); Hit(1, 10, 5, 250); Hit(1, 10, 10, 150);
        Hit(2, 10, 5, 600); Hit(1, 11, 6, 70); Hit(10, 1, 4, 900); Hit(1, 10, 2, 500, true);
        var people = EncounterOverview.Participants(fight);
        var all = DamageSpectrum.Build(fight, people, null, 3);
        var boss = DamageSpectrum.Build(fight, people, 10, 3);
        check(all.Series.Sum(s => s.Buckets.Sum(b => b.Damage)) == 1170 && boss.Series.Sum(s => s.Buckets.Sum(b => b.Damage)) == 1100,
            "Spectrum distinguishes boss-only damage from adds and excludes incoming hits and heals");
        check(boss.Series.Length == 2 && boss.Series.Select(s => s.Id).Distinct().Count() == 2 && boss.Series.All(s => s.Buckets.Length == 4),
            "Spectrum preserves same-name players and shares bucket boundaries across series");
        check(boss.Series.SelectMany(s => s.Buckets).All(b => double.IsFinite(b.Dps)) && Math.Abs(boss.Series.SelectMany(s => s.Buckets).Sum(b => b.Dps * (b.To - b.From)) - 1100) < .001,
            "Spectrum integrates to exact damage including partial final bucket and endpoint hits");
        var left = DamageSpectrum.Build(fight, people, 10, 1, 0, 5);
        var right = DamageSpectrum.Build(fight, people, 10, 1, 5, 10);
        check(left.Series.Sum(s => s.Buckets.Sum(b => b.Damage)) == 100 && right.Series.Sum(s => s.Buckets.Sum(b => b.Damage)) == 1000,
            "Adjacent phase ranges never double-count boundary hits and retain final fight hit");
        check(boss.Series.Single(s => s.Id == 2).Buckets.Count(b => b.Damage == 0) == 3, "Spectrum displays idle intervals at zero rather than filling missing data");
        void Phase(int second, int value, int entity = 10) => fight.Network.Add(new("actor-phase", 1000 + second * 1000, "test", JsonSerializer.SerializeToElement(new { EntityId = entity, Phase = value })));
        Phase(1, 1); Phase(2, 1); Phase(5, 2); Phase(6, 8, 99); Phase(20, 3);
        var phases = DamageSpectrum.Phases(fight, 10);
        check(phases.SequenceEqual(new[] { new SpectrumPhase(1, 5, 1), new SpectrumPhase(5, 10, 2) }), "Spectrum phase ranges use only target events, collapse repeated reports and leave unknown opening unlabeled");
        check(DamageSpectrum.Phases(fight, null).Length == 0, "Spectrum never invents phases for all-target view");
        fight.Actors[20] = fight.Actors[10] with { Id = 20, OriginalEntityId = 10 };
        check(DamageSpectrum.Phases(fight, 10).Length == 0, "Spectrum excludes ambiguous phase events after entity identity reuse");
        Hit(1, 10, 7200, 10);
        var longFight = DamageSpectrum.Build(fight, people, 10, 1);
        check(longFight.Series.All(s => s.Buckets.Length <= 1200) && longFight.Series.Sum(s => s.Buckets.Sum(b => b.Damage)) == 1110, "Long spectrum stays bounded while retaining full fight damage");
        check(DamageSpectrum.Build(new Fight(), [], null, 1).Series.Length == 0, "Empty spectrum is safe");
    }
}
