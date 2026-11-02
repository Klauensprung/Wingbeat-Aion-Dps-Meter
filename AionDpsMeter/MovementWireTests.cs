using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal static class MovementWireTests
{
    internal static void Run(Action<bool, string> check)
    {
        byte[] Body(int flags, bool stopped) {
            var b = new List<byte> { 1, (byte)flags }; int bitAt = -1;
            void Bit() { if (bitAt < 0) { bitAt = b.Count; b.Add(0); } b[bitAt]++; }
            if (stopped) { b.Add(2); if ((flags & 1) != 0) b.Add(3); }
            else if ((flags & 1) != 0) Bit();
            b.AddRange(BitConverter.GetBytes(10f)); b.AddRange(BitConverter.GetBytes(-20f)); b.AddRange(BitConverter.GetBytes(30f));
            if (!stopped) b.AddRange(new byte[2]); b.AddRange(BitConverter.GetBytes((ushort)123));
            if ((flags & 2) != 0) {
                b.AddRange(BitConverter.GetBytes(4)); b.AddRange(new byte[12]);
                if (!stopped) b.Add(5); b.Add(6);
            }
            if ((flags & 4) != 0) Bit();
            if (stopped && (flags & 8) != 0) b.AddRange(new byte[8]);
            return b.ToArray();
        }
        bool layouts = true, truncation = true;
        foreach (bool stopped in new[] { false, true }) {
            for (int flags = 0; flags < (stopped ? 16 : 8); flags++) {
                var r = new WireReader(Body(flags, stopped)); var v = MovementWire.Read(ref r, stopped);
                layouts &= v is { ActorId: 1, X: 10, Y: -20, Z: 30, Rotation: 123 } && v.Stopped == stopped;
            }
            byte[] full = Body(stopped ? 15 : 7, stopped);
            for (int n = 0; n < full.Length; n++) {
                try { var r = new WireReader(full.AsSpan(0, n)); MovementWire.Read(ref r, stopped); truncation = false; }
                catch (InvalidDataException) { }
            }
        }
        check(layouts, "Movement start/stop flag combinations preserve world coordinates independently of vessel coordinates");
        check(truncation, "Every truncated complete movement record is rejected");
        var engine = new CombatEngine(); engine.Apply(new("local-player", Actor: new(1, "Local", true, false, 0, "Cleric", 1)));
        void Event(string kind, long time, object value) => engine.Apply(new("network", Network: new(kind, time, "test", JsonSerializer.SerializeToElement(value))));
        Event("local-zone", 100, new { MapId = 1010, X = 0, Y = 0, Z = 0 });
        Event("actor-position", 200, new MovementReading(2, 100, 200, 300, false, 0));
        Event("actor-position", 300, new MovementReading(1, 10, -20, 30, false, 0));
        Event("actor-position", 250, new MovementReading(1, 99, 99, 99, true, 0));
        check(engine.Position.Latest is { X: 10, Y: -20, Z: 30, Timestamp: 300 } &&
            engine.Snapshot().Network.All(e => e.Kind != "actor-position"),
            "Only current local movement updates the map marker; raw movement packets are not retained in fight history");
    }
}
