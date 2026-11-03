using System.Text;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class PartyWireTests
{
    internal sealed class Writer
    {
        internal readonly List<byte> Bytes = new(); int bitIndex = -1, bit;
        internal void Raw(params byte[] bytes) => Bytes.AddRange(bytes);
        internal void Zero(int n) => Raw(new byte[n]);
        internal void Int(int value) => Raw(BitConverter.GetBytes(value));
        internal void Long(long value) => Raw(BitConverter.GetBytes(value));
        internal void Var(long value) { do { Raw((byte)((value & 127L) | (value >= 128 ? 128L : 0L))); value >>= 7; } while (value != 0); }
        internal void Text(string text) { var b = Encoding.UTF8.GetBytes(text); Var(b.Length); Raw(b); }
        internal void Bool(bool value) { if (bitIndex < 0 || bit == 8) { bitIndex = Bytes.Count; Raw(0); bit = 0; } if (value) Bytes[bitIndex] |= (byte)(1 << bit); bit++; }
        internal void Member(long id, int entity, string name, int template = 25, int server = 1)
        {
            Raw(7, 1); Int(entity); Raw((byte)server, (byte)(server >> 8), 2, 0); Text("discarded-account"); Long(id); Text(name); Int(template);
            Var(5000000000); Var(6000000000); Int(100); Int(200); Zero(40); Bool(true);
            Text("Guild"); Int(0); Int(50); Int(0); Int(100); Bool(true); Bool(true);
            Var(1); Raw(1); Int(17090000); Raw(1); Var(1000); Long(12345); Raw(1);
        }
        internal void ForceMember(long id, int entity)
        {
            Raw(3, 1); Int(entity); Raw(1, 0, 2, 0); Text("discarded-account"); Long(id); Text("Member " + entity); Int(25);
            Var(5000000000); Var(6000000000); Zero(12); Bool(true); Text("Guild"); Int(0);
            Int(50); Int(0); Bool(true); Int(100); Zero(12); Long(12345); Raw(1);
        }
    }
    static byte[] Frame(byte high, byte low, byte[] body) { var w = new Writer(); w.Var(body.Length + 6); w.Raw(high, low); w.Raw(body); return w.Bytes.ToArray(); }
    internal static byte[] Full(int server = 1)
    {
        var w = new Writer(); w.Raw(31); w.Long(123); w.Long(6000000000); w.Int(4); w.Zero(4);
        w.Raw(1); w.Zero(12); // Focus target.
        w.Var(1); w.Raw(1, 2); w.Int(1); w.Text("Marker"); w.Int(0);
        w.Var(1); w.Raw(1); w.Zero(16); w.Bool(true);
        w.Var(1); w.Raw(0); w.Var(1); w.Text("Scan"); w.Int(0); w.Text(""); w.Zero(16);
        w.Var(2); w.Member(6000000000, 1, "First", server: server); w.Member(6000000001, 2, "Second", server: server);
        w.Var(1); w.Raw(1, 1); w.Text("Pin"); w.Zero(32); w.Var(1); w.Long(6000000000); w.Raw(0);
        return w.Bytes.ToArray();
    }
    internal static void Run(Action<bool, string> check)
    {
        var decoder = new EuGameDecoder();
        DecodeResult Decode(byte high, byte low, byte[] body, EuGameDecoder? d = null) => (d ?? decoder).Decode(new CapturedPayload(13328, 50000, Frame(high, low, body)) { TimestampMs = 1000 });
        var full = Full(); var result = Decode(0, 0x92, full); var engine = new CombatEngine();
        foreach (var e in result.Events) engine.Apply(e);
        check(engine.Fight.PartyLeaderName == "First", "Full party roster resolves its 64-bit leader identity to the captured name");
        check(engine.Fight.Actors[1].Metadata is { Level: 50, Guild: "Guild" }, "Party snapshot supplies member level and guild before nearby-player appearance");
        var leader = new Writer(); leader.Long(6000000001);
        var change = Decode(0x11, 0x92, leader.Bytes.ToArray());
        foreach (var e in change.Events) engine.Apply(e);
        check(engine.Fight.PartyLeaderName == "Second" && change.Events.Count == 1,
            "Leader change updates the roster without replaying member vitals");
        check(Enumerable.Range(0, 8).All(n => Decode(0x11, 0x92, leader.Bytes.Take(n).ToArray()).Events.Count == 0) &&
            Decode(0x11, 0x92, [..leader.Bytes, 0]).Events.Count == 0,
            "Party leader rejects every truncated packet and extra trailer");
        engine.NewFight("Next");
        check(engine.Fight.PartyLeaderName == "Second" && engine.Snapshot().PartyLeaderName == "Second", "Party leader survives manual reset and snapshot copies");
        foreach (var e in result.Events) engine.Apply(e);
        check(engine.Fight.PartyKnown && engine.Fight.Actors.Count == 2 && engine.Fight.Actors.Values.All(a => a.Player && a.Party && a.Cp == 12345 && a.Server == 1 && a.Job == "Sorcerer") &&
            CapturedHealth.Build(engine.Fight)[2] is { Value: 5000000000, Maximum: 6000000000 },
            "Full party roster decodes optional lists and packed member flags, identifies players and supplies wide health");
        var added = new Writer(); added.Member(6000000002, 3, "Third");
        var addition = Decode(0x0d, 0x92, added.Bytes.ToArray());
        check(addition.Events.Count(e => e.Network?.Kind == "hp") == 1 && addition.Events.Single(e => e.Party != null).Party!.Members.Length == 3,
            "Party additions retain membership without replaying stale health of existing members");
        check(Decode(0x13, 0x92, [1, 0]).Events.Count == 0 && Decode(0x13, 0x92, [0, 0]).Events.Single().Party!.Members.Length == 0,
            "Failed leave reply keeps party membership and successful leave clears it");
        check(Enumerable.Range(0, full.Length).All(n => Decode(0, 0x92, full[..n], new()).Events.Count == 0) &&
            Decode(0, 0x92, [..full, 0], new()).Events.Count == 0,
            "Every full roster truncation and unknown trailer is rejected before publishing members");
        var partial = Decode(0x22, 0x92, added.Bytes.ToArray(), new());
        var partialEngine = new CombatEngine(); foreach (var e in partial.Events) partialEngine.Apply(e);
        check(!partialEngine.Fight.PartyKnown && partialEngine.Fight.Actors[3].Party,
            "Capture starting on a reconnect knows that member without claiming a complete party roster");
        var partialDecoder = new EuGameDecoder();
        var reconnectEngine = new CombatEngine();
        foreach (var e in Decode(0x22, 0x92, added.Bytes.ToArray(), partialDecoder).Events) reconnectEngine.Apply(e);
        var remapped = new Writer(); remapped.Member(6000000002, 30, "Third");
        foreach (var e in Decode(0x22, 0x92, remapped.Bytes.ToArray(), partialDecoder).Events) reconnectEngine.Apply(e);
        check(!reconnectEngine.Fight.PartyKnown && !reconnectEngine.Fight.Actors[3].Party && reconnectEngine.Fight.Actors[30].Party &&
            PartyHealthView.Build(reconnectEngine.Fight, CapturedHealth.Build(reconnectEngine.Fight), []).Select(r => r.Entity).SequenceEqual(new[] { 30 }),
            "Partial roster reconnect replaces old entity membership rather than leaving phantom HP rows");
        var malformed = added.Bytes.ToArray()[..^1]; Decode(0, 0x92, full);
        Decode(0x0d, 0x92, malformed); var next = Decode(0x22, 0x92, added.Bytes.ToArray());
        check(next.Events.Single(e => e.Party != null).Party!.Members.Length == 3,
            "Malformed member update cannot partially mutate the retained roster");
        var switched = decoder.Decode(new CapturedPayload(13328, 50000, Frame(0x22, 0x92, added.Bytes.ToArray())) { Connection = "new-session", TimestampMs = 2000 });
        foreach (var e in switched.Events) engine.Apply(e);
        check(!engine.Fight.PartyKnown && engine.Fight.Actors[3].Party && !engine.Fight.Actors[1].Party && !engine.Fight.Actors[2].Party,
            "Connection changes invalidate old membership before applying a partial new-session roster");
        var zoneDecoder = new EuGameDecoder(); var zoneEngine = new CombatEngine();
        void Send(byte op, byte low, byte[] body) {
            var decoded = Decode(op, low, body, zoneDecoder);
            foreach (var item in decoded.Events) zoneEngine.Apply(item);
        }
        Send(0x33, 0x36, LocalCharacterWireTests.Body(1, "First"));
        Send(0, 0x92, Full(2301));
        Send(0x33, 0x36, LocalCharacterWireTests.Body(1, "First"));
        check(zoneEngine.Fight.PartyKnown && zoneEngine.Fight.Actors[2].Party,
            "Same-character refresh retains the confirmed party without waiting for another roster packet");
        var zone = new byte[46]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(zone.AsSpan(4), 1010);
        Send(0x21, 0x36, zone);
        check(!zoneEngine.Fight.Actors[2].Party, "Zone preparation retires old entity mappings without trusting reused IDs");
        Send(0x33, 0x36, LocalCharacterWireTests.Body(10, "First"));
        Send(0x45, 0x36, PlayerSpawnWireTests.Body(2, "Outsider", persistentId: 6000000999));
        Send(0x45, 0x36, PlayerSpawnWireTests.Body(20, "Second", persistentId: 6000000001));
        check(zoneEngine.Fight.PartyKnown && !zoneEngine.Fight.Actors[2].Party && zoneEngine.Fight.Actors[20].Party &&
            PartyHealthView.Build(zoneEngine.Fight, CapturedHealth.Build(zoneEngine.Fight), []).Select(r => r.Entity).SequenceEqual(new[] { 10, 20 }),
            "Teleport restores HP membership by persistent identity and excludes an outsider reusing an old entity ID");
        Send(0x13, 0x92, [0, 0]);
        Send(0x45, 0x36, PlayerSpawnWireTests.Body(21, "Second", persistentId: 6000000001));
        check(!zoneEngine.Fight.Actors[21].Party, "A successful party departure prevents subsequent spawns from restoring former members");
        Send(0, 0x92, Full(2301));
        Send(0x33, 0x36, LocalCharacterWireTests.Body(30, "Other", persistentId: 6000000100));
        Send(0x45, 0x36, PlayerSpawnWireTests.Body(22, "Second", persistentId: 6000000001));
        check(!zoneEngine.Fight.Actors[22].Party, "Changing character cannot inherit the previous character's roster");
    }
}
