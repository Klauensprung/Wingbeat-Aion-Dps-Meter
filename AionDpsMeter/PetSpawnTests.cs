using System.IO;

namespace AionDpsMeter.Local;

internal static class PetSpawnTests
{
    internal static void Run(Action<bool, string> check)
    {
        var catalogue = PetSpawns.Default;
        check(catalogue.LoadError == null && catalogue.Count == 18462 && catalogue.ExcludedUnnamedAreas == 96 &&
            catalogue.ForPet(1002) is [{ Map: "Verteron", Spawns.Length: 57 }],
            "Pet spawn lookup joins exact pet/monster references with named-area coordinates and reports excluded unnamed areas");
        check(catalogue.ForPet(0).Count == 0 && ReferenceEquals(catalogue, PetSpawns.Default),
            "Unknown pets receive no guessed spawn locations and the lazy reference is shared");
        var hashes = Enumerable.Range(1, 4).ToDictionary(i => i.ToString(), _ => new string('a', 64));
        var point = new PetSpawnPoint(10, 1, 2, 3);
        var site = new PetSpawnSite(1002, "synthetic", "Synthetic area", [point]);
        bool Reject(PetSpawnSite[] rows) { try { PetSpawns.Validate(new("synthetic", hashes, 0, rows)); return false; } catch (InvalidDataException) { return true; } }
        check(Reject([site with { Spawns = [point with { X = double.NaN }] }]) && Reject([site, site]) &&
            Reject([site with { Spawns = [point with { NpcId = 0 }] }]),
            "Spawn reference validation rejects non-finite coordinates, duplicate pet/area groups and invalid source monsters");
        var viewport = new PetSpawnViewport();
        PetSpawnPoint[] points = [new(10, -10000, -5000, 0), new(11, 10000, 5000, 100)];
        var size = new System.Windows.Size(800, 500); viewport.Fit(points);
        var low = viewport.Project(points[0].X, points[0].Y, size); var high = viewport.Project(points[1].X, points[1].Y, size);
        check(low.X >= 24 && high.X <= size.Width - 24 && low.Y >= 24 && high.Y <= size.Height - 24 &&
            Math.Abs((high.X - low.X) / (high.Y - low.Y) - 2) < .000001,
            "Spawn plot fits all reference coordinates with padding and preserves their spatial aspect ratio");
        var anchor = new System.Windows.Point(300, 175); var world = viewport.Unproject(anchor, size);
        viewport.Zoom(anchor, 2, size);
        check((viewport.Project(world.X, world.Y, size) - anchor).Length < .000001,
            "Zoom keeps the world coordinate under the cursor stationary");
        var projected = viewport.Project(0, 0, size); var delta = new System.Windows.Vector(50, -30);
        viewport.Pan(delta, size);
        check((viewport.Project(0, 0, size) - projected - delta).Length < .000001,
            "Panning moves every spawn by the dragged screen distance");
        var follow=new PetSpawnViewport();follow.Restore(viewport.Snapshot());
        var beforeScale=follow.Snapshot();follow.Center(12000,-3500);
        check(follow.Project(12000,-3500,size)==new System.Windows.Point(size.Width/2,size.Height/2)&&follow.Snapshot().Zoom==beforeScale.Zoom&&follow.Snapshot().SpanX==beforeScale.SpanX,
            "Player-centered map follows movement without changing calibrated scale");
        var restored=new PetSpawnViewport();restored.Restore(viewport.Snapshot());
        check((restored.Project(123,456,size)-viewport.Project(123,456,size)).Length<.000001,"Saved map alignment preserves panning and zoom exactly");
        bool invalidAlignment=false;try{restored.Restore(new(0,0,100,100,double.NaN));}catch(ArgumentException){invalidAlignment=true;}
        check(invalidAlignment,"Invalid map calibration is rejected");
        viewport.Fit(points);
        check(viewport.Hit(points, low, size) == 0 && viewport.Hit(points, new(400, 250), size) == -1,
            "Point selection uses screen distance and does not select distant spawns on an empty click");
        viewport.Fit([point]); var single = viewport.Project(point.X, point.Y, new(1, 1));
        check(double.IsFinite(single.X) && double.IsFinite(single.Y),
            "Single-point references and very small viewports retain a finite projection");
        viewport.Fit([point, point], 20000);
        var contextLeft = viewport.Unproject(new(24, 250), size); var contextRight = viewport.Unproject(new(776, 250), size);
        check(contextRight.X - contextLeft.X >= 20000 &&
            (viewport.Project(point.X, point.Y, size) - new System.Windows.Point(400, 250)).Length < .000001,
            "Pet overlay fit preserves surrounding terrain when the only target overlaps the player");
        var terrain = PetTerrain.Find("World/World_L/World_L_A")!;
        var origin = terrain.World(terrain.OffsetX, terrain.OffsetY);
        check(terrain.Tiles.Length == 26 && origin.X == 0 && origin.Y == 0 &&
            PetTerrain.Find("World/World_D/World_D_A")?.Tiles.Length == 34 && PetTerrain.Find("unknown") == null,
            "Terrain calibration resolves exact source areas and converts native tile coordinates to the shared world space");
        var cache = new TerrainTileCache();
        bool decoded = true;
        foreach (var key in new[] { "World/World_L/World_L_A", "World/World_D/World_D_A", "Intersever/Abyss/Abyss_Reshanta_A" }) {
            var map = PetTerrain.Find(key)!;
            foreach (var tile in map.Tiles) {
                var bitmap = cache.Get(map, tile, 600);
                decoded &= bitmap is { PixelWidth: <= 512, IsFrozen: true } && cache.Bytes <= 16 * 1024 * 1024;
            }
        }
        check(decoded && cache.Bytes > 0, "All 64 terrain tiles decode headlessly with frozen images and a bounded 16 MiB cache");
        cache.Clear(); check(cache.Bytes == 0, "Closing or changing a spawn plot releases its decoded terrain cache");
    }
}
