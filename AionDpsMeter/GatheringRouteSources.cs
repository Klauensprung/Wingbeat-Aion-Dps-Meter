namespace AionDpsMeter.Local;

internal static class GatheringRouteSources
{
    // A route selection key only; never sent to inventory or loot tracking.
    internal const int HiddenCubes = int.MaxValue;
    internal static MapMarkerGroup[] Groups(MapMarkers catalogue,int world,int selection){
        if(selection==HiddenCubes)return catalogue.Groups.Where(g=>g.World==world&&g.Category=="hidden-cube").ToArray();
        var sources=ItemFarmLinks.Default.Find(selection)?.Gatherables??[];
        return catalogue.Groups.Where(g=>g.World==world&&g.DatabaseType=="gatherable"&&sources.Contains(g.DatabaseId)).ToArray();
    }
}
