using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record CapturedPosition(int MapId, double X, double Y, double Z, long Timestamp);
internal static class LocalPositionWire
{
    internal static object Prepare(ref WireReader reader)
    {
        reader.Take(4); int map = checked((int)reader.U32()); reader.Take(8);
        if (map <= 0) throw new InvalidDataException("Invalid zone ID.");
        float x = Coordinate(ref reader), y = Coordinate(ref reader), z = Coordinate(ref reader);
        Coordinate(ref reader); // Direction.
        reader.Take(10); // Reason, environment state and environment start time.
        int groups = reader.Var();
        if (groups > 4096) throw new InvalidDataException("Too many environment groups.");
        reader.Take(checked(groups * 13));
        reader.Var(); // Teleport effect.
        int textLength = reader.Var();
        if (textLength > 4096) throw new InvalidDataException("Cutscene name exceeds limit.");
        reader.Take(textLength); reader.Byte(); // Cutscene name and inform type.
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown zone entry trailer.");
        return new { MapId = map, X = x, Y = y, Z = z, AwaitingIdentity = true };
    }
    internal static float Coordinate(ref WireReader reader)
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(reader.Take(4));
        if (!float.IsFinite(value) || Math.Abs(value) > 10000000) throw new InvalidDataException("Invalid position coordinate.");
        return value;
    }
    internal static object Loaded(ref WireReader reader)
    {
        if (reader.Remaining is not (23 or 24) || reader.Take(6).IndexOfAnyExcept((byte)0) >= 0) throw new InvalidDataException("Unknown loaded-position layout.");
        float x = Coordinate(ref reader), y = Coordinate(ref reader), z = Coordinate(ref reader);
        uint state = reader.U32(); int token = reader.Var();
        if (state is < 1 or > 5 || token is < 0 or > 16383 || reader.Remaining != 0) throw new InvalidDataException("Unknown loaded-position trailer.");
        return new { X = x, Y = y, Z = z }; // The trailer does not establish a map or channel.
    }
}
internal sealed class LocalPositionTracker
{
    internal static void Validate(CapturedPosition position)
    {
        if (position.MapId <= 0 || position.Timestamp < 0 || !Valid(position.X) || !Valid(position.Y) || !Valid(position.Z))
            throw new InvalidDataException("Invalid saved player position.");
        static bool Valid(double value) => double.IsFinite(value) && Math.Abs(value) <= 10000000;
    }
    int map;
    long zoneTime = -1;
    internal CapturedPosition? Latest { get; private set; }
    internal CapturedPosition? RelativeLatest { get; private set; }
    internal bool MovementStopped {get;private set;}
    internal long RelocationRevision {get;private set;}
    internal void Clear(bool zone)
    {
        Latest = null; RelativeLatest = null; MovementStopped=false;
        if (zone) { map = 0; zoneTime = -1; }
    }
    internal void Apply(NetworkEvent message, int localPlayer = 0)
    {
        bool effect = message.Kind is "abnormal-apply" or "abnormal-refresh";
        if ((!effect && message.Kind is not ("local-zone" or "local-position" or "actor-position" or "displacement")) || message.Timestamp < 0 || message.Timestamp < zoneTime ||
            message.Timestamp < RelativeLatest?.Timestamp || message.Fields.ValueKind != JsonValueKind.Object) return;
        var fields = message.Fields;
        if (message.Kind is "actor-position" or "displacement" && (localPlayer <= 0 || !fields.TryGetProperty("ActorId", out var actor) ||
            actor.ValueKind != JsonValueKind.Number || !actor.TryGetInt32(out int actorId) || actorId != localPlayer)) return;
        if (effect && (localPlayer <= 0 || !fields.TryGetProperty("CasterId", out var caster) || caster.ValueKind != JsonValueKind.Number ||
            !caster.TryGetInt32(out int casterId) || casterId != localPlayer ||
            !fields.TryGetProperty("TargetId", out var recipient) || recipient.ValueKind != JsonValueKind.Number ||
            !recipient.TryGetInt32(out int recipientId) || recipientId != localPlayer)) return;
        if (message.Kind == "local-zone") {
            if (!fields.TryGetProperty("MapId", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out int value) || value <= 0) return;
            map = value; zoneTime = message.Timestamp; Latest = null; RelativeLatest = null;
        }
        bool Coordinate(string name, out double value) {
            value = 0;
            return fields.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetDouble(out value) &&
                double.IsFinite(value) && Math.Abs(value) <= 10000000;
        }
        string prefix = effect ? "Caster" : "";
        if (Coordinate(prefix + "X", out double x) && Coordinate(prefix + "Y", out double y) && Coordinate(prefix + "Z", out double z) &&
            (!effect || x != 0 || y != 0))
        {
            if(message.Kind is "local-zone" or "local-position" or "displacement")RelocationRevision++;
            RelativeLatest = new(map, x, y, z, message.Timestamp);
            if (map > 0) Latest = RelativeLatest;
            MovementStopped=message.Kind is "local-zone" or "local-position" or "displacement" || fields.TryGetProperty("Stopped",out var stopped)&&stopped.ValueKind==JsonValueKind.True;
        }
    }
    internal static string? MapKey(int zone) => zone switch {
        1010 or 902 => "World/World_L/World_L_A",
        1110 or 901 => "World/World_D/World_D_A",
        _ => null
    };
    internal static bool Matches(CapturedPosition? position, string? mapKey) => position != null && mapKey != null && MapKey(position.MapId) == mapKey;
}
