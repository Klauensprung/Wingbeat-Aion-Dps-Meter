namespace AionDpsMeter.Local;
internal static class RadarTracking
{
    internal static Dictionary<int,string> Materials(Preferences settings, MapTarget[] tracked, IEnumerable<MapMarkerGroup> groups, IEnumerable<int> itemGatherables)
    {
        if (!settings.RadarShowMaterials) return new();
        var ids = tracked.Where(t => t.Kind == "gatherable").Select(t => t.Id).Concat(itemGatherables).ToHashSet();
        var custom = (settings.RadarMaterialObjects ?? []).ToHashSet();
        return groups.Where(g => g.DatabaseType == "gatherable" && g.ObjectId > 0 &&
            (settings.RadarMaterialMode == "All" || (settings.RadarMaterialMode == "Custom" ? custom.Contains(g.ObjectId) : ids.Contains(g.DatabaseId))))
            .GroupBy(g => g.ObjectId).ToDictionary(g => g.Key, g => g.First().Name);
    }
}
