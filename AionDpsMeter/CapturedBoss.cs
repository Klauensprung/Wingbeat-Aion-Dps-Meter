using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class CapturedBoss
{
    internal static bool IsBoss(Actor actor) => !actor.Player &&
        (actor.CapturedBoss ?? BossCatalogue.Default.IsEncounterBoss(actor.NpcId));
}
