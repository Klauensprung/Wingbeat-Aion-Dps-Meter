using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PvpTests
{
    static DamageEvent Hit(int actor, int target, long time, int amount, int skill = 12010001) =>
        new() { ActorId = actor, TargetId = target, Timestamp = time, Damage = amount, SkillCode = skill };
    internal static Fight Duel()
    {
        var fight = new Fight { LocalPlayer = 1, Demo = true, Label = "PvP display test" };
        fight.Actors[1] = new(1, "You", true, false, 0, "Cleric", 2301);
        fight.Actors[2] = new(2, "Entity 2", false, false, 0, "Unknown", 0);
        fight.Actors[9] = new(9, "NPC target", false, false, 0, "Unknown", 0) { NpcId = 2000001 };
        fight.Damage.AddRange([Hit(2, 1, 10000, 1000), Hit(1, 2, 15000, 100, 17010001), Hit(2, 1, 20000, 2000, 12020001)]);
        fight.Healing.Add(Hit(2, 2, 17000, 500, 12100001) with { IsHeal = true });
        return fight;
    }
    public static void Run(Action<bool, string> check)
    {
        var f = Duel(); var pvp = PvpAnalysis.Build(f); var meter = PvpAnalysis.Meter(f, pvp);
        check(pvp.Opponents.Single().Opponent == "Entity 2" && meter.Rows.Single(r => r.Id == 2).Name == "Entity 2",
            "Unnamed opponents display entity IDs in both the PvP table and overlay despite inferred classes");
        var blankIdentity = Duel(); blankIdentity.Actors[2] = blankIdentity.Actors[2] with { Name = " ", Player = true };
        check(blankIdentity.Name(2) == "Entity 2" && blankIdentity.Name(999) == "Entity 999" &&
            PvpAnalysis.Build(blankIdentity).Opponents.Single() is { Opponent: "Entity 2", Identity: "Name pending" },
            "Blank and absent actor names fall back to entity IDs without claiming a captured name");
        check(pvp.Opponents.Single() is { Id: 2, Identity: "Name pending", DamageToUs: 3000, DamageFromUs: 100, DPS: 300, Healing: 500 },
            "PvP retains an unnamed class-skill attacker with correctly directed damage and healing");
        check(pvp.Seconds == 10 && pvp.YourDamage == 100 && pvp.EnemyDamage == 3000 && meter.Rows[0].Id == 1 && meter.Rows[1].Rate == 300,
            "Both PvP sides share the exchange duration and the local player remains first when outdamaged");
        check(!f.Actors[2].Player && f.Actors[2].Name == "Entity 2" && f.Damage.Count == 3,
            "PvP skill evidence does not invent player identities or mutate raw combat data");
        f.Damage.AddRange([Hit(1, 9, 1, 90000, 17010001), Hit(9, 1, 60000, 99999, 12010001)]);
        f.Actors[8] = new(8, "Bystander", true, false, 0, "Unknown", 0);
        f.Damage.Add(Hit(8, 9, 70000, 500000));
        f.Healing.Add(Hit(2, 2, 71000, 1000000, 12100001) with { IsHeal = true });
        check(PvpAnalysis.Build(f) is { Seconds: 10, YourDamage: 100, EnemyDamage: 3000 } scoped && scoped.Opponents.Length == 1 && scoped.Opponents[0].Healing == 500,
            "PvE, bystander damage and healing outside the PvP window do not inflate totals or duration");
        f.Actors[2] = f.Actors[2] with { Name = "OpponentName", Player = true };
        var named = PvpAnalysis.Build(f);
        check(named.Opponents[0].Opponent == "OpponentName" && named.Opponents[0].Identity == "Named" && named.EnemyDamage == pvp.EnemyDamage,
            "Late opponent names relabel the existing PvP row without changing its totals");
        f.Actors[2] = f.Actors[2] with { Player = false, NpcId = 2920640 };
        check(!PvpAnalysis.Build(f).HasOpponents, "Captured NPC identity overrides earlier class-skill display evidence");
        var pets = new Fight { LocalPlayer = 1, Actors = new(Duel().Actors) };
        pets.Damage.AddRange([Hit(3, 1, 10000, 1000, 17150032), Hit(4, 1, 11000, 1000, 15280001), Hit(5, 1, 12000, 1000, 16100001), Hit(6, 1, 13000, 1000, 1229290)]);
        check(!PvpAnalysis.Build(pets).HasOpponents, "Unidentified Divine Aura, summoned wind, spirits and NPC attacks cannot trigger automatic PvP");
        var inbound = Duel(); inbound.Damage.RemoveAll(e => e.ActorId == 1);
        check(PvpAnalysis.Meter(inbound, PvpAnalysis.Build(inbound)).Rows is [{ Id: 1, Amount: 0 }, { Id: 2, Amount: 3000 }],
            "An incoming-only attack shows the opponent and keeps your zero-DPS row visible");
        var outgoing = Duel(); outgoing.Actors[2] = outgoing.Actors[2] with { Name = "KnownOpponent", Player = true };
        outgoing.Damage.RemoveAll(e => e.ActorId == 2);
        check(PvpAnalysis.Meter(outgoing, PvpAnalysis.Build(outgoing)).Rows is [{ Id: 1, Amount: 100 }, { Id: 2, Amount: 0 }],
            "An outgoing-only attack retains a known opponent even before they deal any damage");
        var party = Duel(); party.Actors[3] = new(3, "Ally", true, true, 0, "Chanter", 2301);
        party.Damage.AddRange([Hit(2, 3, 16000, 250), Hit(3, 2, 17000, 350), Hit(1, 3, 18000, 999)]);
        var group = PvpAnalysis.Build(party);
        check(group.Opponents.Single() is { DamageToUs: 3250, DamageFromUs: 450 } && group.YourDamage == 100 && group.Combatants.Single(c => c.Id == 3).Friendly,
            "Group PvP includes captured allies and excludes damage between members of your side");
        check(PvpAnalysis.Meter(party, group).Rows.Select(r => r.Id).SequenceEqual(new[] { 1, 2, 3 }),
            "PvP places opponents immediately below you before the remaining party rows");
        var many = Duel();
        for (int i = 10; i < 26; i++) many.Damage.Add(Hit(i, 1, 18000, i * 10));
        var all = PvpAnalysis.Build(many);
        check(all.Opponents.Length == 17 && PvpAnalysis.Meter(many, all).Rows.Length == 18,
            "PvP displays every simultaneous opponent without the party meter's ten-row cutoff");
        var healing = PvpAnalysis.Meter(inbound, PvpAnalysis.Build(inbound), healing: true);
        check(healing.Rows[0].Id == 1 && healing.Rows[0].Amount == 0 && healing.Rows[1].Amount == 500 && healing.Rows[1].Rate == 50,
            "PvP healing mode preserves opponent identities and the pinned local player");
        var noLocal = Duel(); noLocal.LocalPlayer = 0;
        check(!PvpAnalysis.Build(noLocal).HasOpponents && !PvpAnalysis.Build(new Fight()).HasOpponents,
            "Nearby combat does not invent a local player or opposing sides before local identity is known");
    }
}
