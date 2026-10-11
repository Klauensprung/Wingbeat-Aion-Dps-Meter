namespace AionDpsMeter.Local;

internal static class FightPersistence
{
    // The caller runs this on its one save worker. Capture may roll over after the
    // live snapshot was taken: save completed fights LAST so the final snapshot
    // always replaces an earlier autosave of the same fight.
    public static Fight[] Save(CombatEngine engine, Fight? current, Action<Fight> save)
    {
        var saved = new List<Fight>();
        if (current?.HasRecordedData == true) { save(current); saved.Add(current); }
        saved.AddRange(engine.SaveCompleted(save));
        return saved.ToArray();
    }
}
