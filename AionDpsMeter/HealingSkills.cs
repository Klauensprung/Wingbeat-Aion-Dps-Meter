using System.Collections.Frozen;

namespace AionDpsMeter.Local;

// Protocol metadata, separate from translated display names. Family IDs are
// adapted from the MIT-licensed public catalog identified in THIRD-PARTY-NOTICES.txt;
// see licenses/SkeeveTV-MIT.txt. Entity IDs always come from the current packet.
internal static class HealingSkills
{
    // Cleric and Chanter recovery families. These may heal another recipient.
    static readonly FrozenSet<int> Support = new[] {
        17090000, 17100000, 17120000, 17160000, 17240000, 17270000, 17290000,
        17320000, 17390000, 17720000, 17800000,
        18120000, 18140000, 18160000, 18170000, 18200000, 18240000, 18420000,
        18440000, 18720000
    }.ToFrozenSet();

    // Mixed offensive/recovery families only count as recovery on the caster.
    // In particular, Blood Absorption hitting an enemy remains damage.
    static readonly FrozenSet<int> SelfRecovery = new[] {
        11110000, 11120000, 11260000, 11270000, 11380000, 11720000, 11730000,
        12110000, 12230000, 12250000, 12260000, 12320000, 12720000,
        13080000, 13260000, 13710000, 13790000, 14260000, 14710000, 14790000,
        15240000, 15410000, 15790000, 16190000, 16200000, 16270000, 16750000,
        16770000, 16790000, 19350000, 19360000, 19450000, 19710000, 19790000
    }.ToFrozenSet();

    // Exact resource-only variants documented by public protocol research.
    // Never classify MP recovery, companion initialization, or an arbitrary
    // self-targeted action as HP healing. More variants may remain unsupported.
    public static bool ResourceOnly(int skill) => skill is
        11110008 or 12110008 or 13080008 or 15160007 or 15760007 or 15760008 or
        16060001 or 16060031 or 16990002 or 18140007 or 19350002;

    public static bool Contains(int skill, bool self)
    {
        if (skill is < 11000000 or >= 20000000 || ResourceOnly(skill)) return false;
        int family = skill / 10000 * 10000;
        return Support.Contains(family) || self && SelfRecovery.Contains(family);
    }
}
