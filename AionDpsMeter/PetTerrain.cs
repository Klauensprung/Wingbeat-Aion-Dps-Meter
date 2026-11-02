using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AionDpsMeter.Local;

internal sealed record TerrainTile(int Column, int Row, string Sha256);
internal sealed record TerrainMap(string Key, string MapKey, int Tile, int Grid, double Scale, double OffsetX, double OffsetY, TerrainTile[] Tiles)
{
    internal Point World(double x, double y) => new((x - OffsetX) / Scale, (y - OffsetY) / Scale);
}
internal sealed record TerrainData(string Source, string CalibrationSha256, TerrainMap[] Maps);
internal static class PetTerrain
{
    static readonly Lazy<TerrainMap[]> maps = new(Load);
    internal static TerrainMap? Find(string key) => maps.Value.FirstOrDefault(m => m.MapKey == key);
    static TerrainMap[] Load()
    {
        try {
            using var stream = typeof(PetTerrain).Assembly.GetManifestResourceStream("Wingbeat.Terrain.json");
            var data = JsonSerializer.Deserialize<TerrainData>(stream ?? throw new InvalidDataException());
            if (data?.Maps is not { Length: 3 } || data.Maps.Any(m => m == null || m.Key is not ("verteron" or "altgard" or "abyss") ||
                m.Tile is < 128 or > 2048 || m.Grid is < 1 or > 16 || !double.IsFinite(m.Scale) || m.Scale <= 0 ||
                !double.IsFinite(m.OffsetX) || !double.IsFinite(m.OffsetY) || m.Tiles is not { Length: > 0 and <= 256 } ||
                m.Tiles.Any(t => t == null || t.Column < 0 || t.Column >= m.Grid || t.Row < 0 || t.Row >= m.Grid))) return [];
            return data.Maps;
        } catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException) { return []; }
    }
}

// Owned by one open plot. Decode only visible tiles at a bounded resolution.
internal sealed class TerrainTileCache
{
    readonly Dictionary<string, (BitmapSource Image, long Used, long Bytes)> images = new();
    long bytes, clock;
    internal long Bytes => bytes;
    internal void Clear() { images.Clear(); bytes = 0; }
    internal BitmapSource? Get(TerrainMap map, TerrainTile tile, double screenWidth)
    {
        int width = screenWidth <= 128 ? 128 : screenWidth <= 256 ? 256 : 512;
        string resource = $"Wingbeat.MapTiles.{map.Key}.{tile.Column}_{tile.Row}.jpg";
        string key = resource + ":" + width;
        if (images.TryGetValue(key, out var cached)) { images[key] = (cached.Image, ++clock, cached.Bytes); return cached.Image; }
        try {
            using var stream = typeof(TerrainTileCache).Assembly.GetManifestResourceStream(resource);
            if (stream == null) return null;
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = width; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            long cost = bitmap.PixelWidth * (long)bitmap.PixelHeight * 4;
            while (images.Count > 0 && bytes + cost > 16 * 1024 * 1024) {
                var oldest = images.MinBy(p => p.Value.Used); images.Remove(oldest.Key); bytes -= oldest.Value.Bytes;
            }
            images[key] = (bitmap, ++clock, cost); bytes += cost; return bitmap;
        } catch (Exception ex) when (ex is IOException or FileFormatException or NotSupportedException or ArgumentException) { return null; }
    }
}
