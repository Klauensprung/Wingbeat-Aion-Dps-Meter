namespace AionDpsMeter.Local;

internal static class BossMechanicCues
{
    // Names/IDs confirmed in the Nov 13 recordings. Instructions checked against
    // https://mobalytics.gg/gamebase/guides/aion-2-rahu-boss-guide (Nov 8).
    // Match NPC identity as well as skill: generic "Attack" names are not triggers.
    // No client cooldown is used as a recurrence timer.
    internal static (string Title,string Detail)? Cast(int npc,int skill) => (npc,skill) switch
    {
        (2301006,1802900) => ("Wheel of Torment", "Regroup on the marked player; rotate together."),
        (2301006,1802980 or 1802981) => ("Feeding Time", "Aim the marked line through a monster; keep passing the bomb."),
        _ => null
    };

    // Wire phase 1 follows Ludra's Trial and switches to the second attack set
    // in ten recorded transitions. It is not a universal one-based phase number.
    internal static (string Title,string Detail)? Phase(int npc,int phase) => (npc,phase) switch
    {
        (2301014,1) => ("Ludra: second phase", "Watch for jump rings; keep collecting assigned orbs."),
        _ => null
    };
}
