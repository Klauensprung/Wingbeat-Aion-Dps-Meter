using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record SiegeLayer(int Event, string State, long? EndsAt, long? EndedAt, long? NextAt);
internal sealed record SiegeArtifact(int Artifact, string? Owner);
internal sealed record SiegeReading(SiegeLayer[] Layers, SiegeArtifact[] Artifacts);
internal sealed record ArtifactCapture(int Artifact, string? Owner, long Time);
internal static class ArtifactSiegeWire
{
    static int Count(ref WireReader r) { int n = r.Var(); if (n > 4096) throw new InvalidDataException("Siege list exceeds bounds."); return n; }
    static string? Owner(byte race) => race switch { 1 => "light", 2 => "dark", _ => null };
    static SiegeArtifact Artifact(ref WireReader r) { int id = unchecked((int)r.U32()); r.U32(); string? owner = Owner(r.Byte()); r.U64(); return new(id, owner); }
    static SiegeLayer Layer(ref WireReader r)
    {
        byte flags = r.Byte(); int id = unchecked((int)r.U32()); byte state = r.Byte(); r.Byte(); long reward = unchecked((long)r.U64()); long end = (flags & 1) != 0 ? unchecked((long)r.U64()) : 0;
        return new(id, state switch { 1 => "prepare", 2 => "progress", 3 => "end", _ => "idle" }, state == 2 && end != 0 ? end : null,
            state == 3 && reward != 0 ? reward : null, state == 3 && end != 0 ? end : null);
    }
    internal static (string Kind, object Data)? Read(ref WireReader r, ushort opcode, long now, long offset)
    {
        var layers = new List<SiegeLayer>(); var artifacts = new List<SiegeArtifact>(); ushort result = 0;
        if (opcode == 0x03e3) {
            var artifact = Artifact(ref r);
            if (r.Remaining != 0) throw new InvalidDataException("Unexpected capture trailer.");
            return ("artifact-captured", new ArtifactCapture(artifact.Artifact, artifact.Owner, checked(now + offset)));
        }
        switch (opcode) {
            case 0x02e3: r.U32(); r.U64(); break;
            case 0x05e3: case 0x08e3: layers.Add(Layer(ref r)); break;
            case 0x07e3:
                result = r.U16(); for (int n = Count(ref r); n > 0; n--) layers.Add(Layer(ref r)); break;
            default: throw new InvalidDataException("Unknown siege packet.");
        }
        if (opcode != 0x08e3) for (int n = Count(ref r); n > 0; n--) artifacts.Add(Artifact(ref r));
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected siege trailer.");
        return result == 0 ? ("artifact-siege", new SiegeReading(layers.ToArray(), artifacts.ToArray())) : null;
    }
}

internal sealed class ArtifactSiegeTracker
{
    readonly Dictionary<int, (SiegeLayer Value, long At)> layers = new();
    readonly Dictionary<int, (SiegeArtifact Value, long At, long? Capture)> artifacts = new();
    readonly List<ArtifactCaptorship> captures = new();
    (int Light, int Dark)? pair;
    internal void Clear() { layers.Clear(); artifacts.Clear(); captures.Clear(); pair = null; }
    internal void Follow(int server) { if (pair is { } p && server > 0 && server != p.Light && server != p.Dark) Clear(); }
    internal void Apply(NetworkEvent e)
    {
        if (e.Kind == "abyss-matching") {
            var servers = e.Fields.Deserialize<AbyssMatching>()!.Servers;
            int? light = servers.FirstOrDefault(s => s.Race == "light")?.Server, dark = servers.FirstOrDefault(s => s.Race == "dark")?.Server;
            if (light == null || dark == null) return;
            int Region(int server) => server / 1000 is 1 or 2 && server % 1000 / 100 is >= 1 and <= 5 ? server % 1000 / 100 : 0;
            if (Region(light.Value) == 0 || Region(light.Value) != Region(dark.Value)) return;
            var next = (light.Value, dark.Value); if (pair != null && pair != next) Clear(); pair = next;
        } else if (e.Kind == "artifact-siege") {
            var reading = e.Fields.Deserialize<SiegeReading>()!;
            foreach (var layer in reading.Layers) {
                if (layers.TryGetValue(layer.Event, out var prior) && prior.At > e.Timestamp) continue;
                if (!layers.ContainsKey(layer.Event) && layers.Count >= 128) layers.Remove(layers.MinBy(p => p.Value.At).Key);
                layers[layer.Event] = (layer, e.Timestamp);
            }
            foreach (var artifact in reading.Artifacts) Set(artifact, e.Timestamp, null);
        } else if (e.Kind == "artifact-captured") {
            var captured = e.Fields.Deserialize<ArtifactCapture>()!; Set(new(captured.Artifact, captured.Owner), e.Timestamp, captured.Time);
            if (captured.Owner != null) {
                if (captures.Count >= 64) captures.RemoveAt(0);
                captures.Add(new(captured.Artifact,captured.Owner,e.Timestamp));
            }
        } else if (e.Kind == "boss-kill") {
            var boss = e.Fields.Deserialize<BossAnnouncement>()!;
            int index = captures.FindLastIndex(c => c.Owner == boss.Race && c.Name == null && Math.Abs((decimal)c.Timestamp - e.Timestamp) <= 5000);
            if (index >= 0 && boss.Name.Length is > 0 and <= 128) captures[index] = captures[index] with { Name = boss.Name, Server = boss.Server };
        }
    }
    void Set(SiegeArtifact artifact, long at, long? capture)
    {
        bool known = artifacts.TryGetValue(artifact.Artifact, out var prior);
        if (known && prior.At > at) return;
        if (!artifacts.ContainsKey(artifact.Artifact) && artifacts.Count >= 512) artifacts.Remove(artifacts.MinBy(p => p.Value.At).Key);
        long? captureAt = capture != null ? at : known && prior.Value.Owner == artifact.Owner ? prior.Capture : null;
        artifacts[artifact.Artifact] = (artifact, at, captureAt);
    }
    internal SiegeLayer[] Layers() => layers.Values.Select(v => v.Value).OrderBy(v => v.Event).ToArray();
    internal SiegeArtifact[] Artifacts() => artifacts.Values.Select(v => v.Value).OrderBy(v => v.Artifact).ToArray();
    internal ArtifactCaptorship[] Captors() => captures.Where(c => c.Name != null && artifacts.TryGetValue(c.Artifact,out var current) && current.Capture == c.Timestamp && current.Value.Owner == c.Owner)
        .GroupBy(c => c.Artifact).Select(g => g.Last()).ToArray();
}
