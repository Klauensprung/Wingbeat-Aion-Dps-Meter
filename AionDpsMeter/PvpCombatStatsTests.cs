using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PvpCombatStatsTests
{
    internal static void Run(Action<bool, string> check)
    {
        DamageEvent Hit(long time, int amount) => new() { ActorId = 1, TargetId = 2, Timestamp = time, Damage = amount };
        check(PvpCombatStats.PeakFiveSeconds([Hit(5000, 300), Hit(0, 100), Hit(4999, 200)]) == 500,
            "PvP burst uses the strongest rolling five-second window with an exclusive endpoint and sorted timestamps");
        check(PvpCombatStats.PeakFiveSeconds([Hit(0, int.MaxValue), Hit(0, int.MaxValue)]) == 2L * int.MaxValue &&
            PvpCombatStats.PeakFiveSeconds([]) == 0, "PvP burst handles simultaneous large hits and empty exchanges");
        var f = PvpTests.Duel();
        f.Deaths.Add(new(1, 20000));
        void Death(int victim, int killer, long time) => f.Network.Add(new("entity-death", time, "04 8D",
            JsonSerializer.SerializeToElement(new { EntityId = victim, KillerId = killer })));
        Death(1, 2, 20000); Death(1, 2, 20500); Death(1, 2, 19000);
        var pvp = PvpAnalysis.Build(f);
        check(pvp.Opponents.Single().ConfirmedKills == 1 && pvp.Opponents.Single().Peak5sToUs == 2000 &&
            pvp.Opponents.Single().Peak5sFromUs == 100,
            "Confirmed PvP kills deduplicate nearby reports for the same observed death and retain directed burst totals");
        Death(2, 1, 21000);
        check(PvpAnalysis.Build(f).Combatants.Single(c => c.Id == 1).ConfirmedKills == 0,
            "A kill packet without a corresponding captured death does not manufacture a kill");
        f.Actors[3] = new(3, "Second opponent", true, false, 0, "Ranger", 1);
        f.Damage.Add(new() { ActorId = 3, TargetId = 1, Damage = 1, Timestamp = 19000 }); Death(1, 3, 20000);
        check(PvpAnalysis.Build(f).Combatants.All(c => c.ConfirmedKills == 0),
            "Conflicting killers for one observed death remain unattributed");
        f.Network.RemoveAt(f.Network.Count - 1);
        f.Actors[99] = f.Actors[2] with { Id = 99, OriginalEntityId = 2 };
        check(PvpAnalysis.Build(f).Combatants.All(c => c.ConfirmedKills == 0),
            "Raw kill packets for recycled entity IDs never award kills to replacement characters");
    }
}
