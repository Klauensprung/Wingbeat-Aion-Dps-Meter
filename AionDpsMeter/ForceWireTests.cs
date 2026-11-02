using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class ForceWireTests
{
    internal static byte[] Full(int groups = 2, int membersPerGroup = 3)
    {
        var w = new PartyWireTests.Writer(); w.Raw(3); w.Long(100); w.Long(6000000000); w.Var(1); w.Long(6000000001); w.Int(64); w.Zero(3);
        w.Var(1); w.Raw(1, 2); w.Int(1); w.Text("Marker"); w.Int(0);
        w.Var(1); w.Raw(1); w.Zero(16); w.Bool(true);
        w.Var(groups);
        for (int group = 0; group < groups; group++) {
            w.Long(group == 0 ? 123 : 456 + group); w.Raw((byte)(group + 1)); w.Int(10); w.Var(membersPerGroup);
            for (int i = 1; i <= membersPerGroup; i++) {
                int entity = group * membersPerGroup + i; w.ForceMember(6000000000 + entity - 1, entity);
            }
        }
        w.Raw(0); return w.Bytes.ToArray();
    }
    internal static void Run(Action<bool, string> check)
    {
        var decoder = new EuGameDecoder();
        DecodeResult Decode(byte high, byte low, byte[] body, EuGameDecoder? d = null) {
            var w = new PartyWireTests.Writer(); w.Var(body.Length + 6); w.Raw(high, low); w.Raw(body);
            return (d ?? decoder).Decode(new CapturedPayload(13328, 50000, w.Bytes.ToArray()) { TimestampMs = 1000 });
        }
        var full = Full(); var result = Decode(2, 0x96, full); var engine = new CombatEngine();
        foreach (var e in result.Events) engine.Apply(e);
        check(engine.Fight.PartyLeaderName == "Member 1", "Alliance full snapshot resolves leader identity across subgroups");
        check(engine.Fight.Actors[1].Metadata is { Level: 50, Guild: "Guild" }, "Alliance snapshot supplies member level and guild before nearby-player appearance");
        check(engine.Fight.PartyKnown && engine.Fight.Actors.Values.Count(a => a.Party) == 6 &&
            result.Events.All(e => e.Network?.Kind != "resources"),
            "Alliance snapshot binds all subgroups with shared booleans and does not invent MP readings");
        var subgroup = Decode(0, 0x92, PartyWireTests.Full());
        foreach (var e in subgroup.Events) engine.Apply(e);
        check(engine.Fight.Actors.Values.Count(a => a.Party) == 5 && !engine.Fight.Actors[3].Party && engine.Fight.Actors[6].Party,
            "Party snapshot inside alliance replaces only its matching subgroup");
        var remove = new PartyWireTests.Writer(); remove.Long(6000000005); remove.Long(457); remove.Raw(0);
        foreach (var e in Decode(0x22, 0x96, remove.Bytes.ToArray()).Events) engine.Apply(e);
        check(!engine.Fight.Actors[6].Party && engine.Fight.Actors[4].Party,
            "Alliance removal uses persistent identity without dropping unrelated members");
        var reconnect = new PartyWireTests.Writer(); reconnect.ForceMember(6000000003, 40);
        foreach (var e in Decode(0x1b, 0x96, reconnect.Bytes.ToArray()).Events) engine.Apply(e);
        check(engine.Fight.Actors[40].Party && !engine.Fight.Actors[4].Party,
            "Alliance reconnect replaces a member's temporary combat ID");
        foreach (bool alliance in new[] { false, true }) {
            var status = new PartyWireTests.Writer(); status.Long(6000000003); status.Int(29); status.Zero(24); status.Int(alliance ? 52 : 51); status.Zero(8); status.Text(alliance ? "" : "Changed guild"); status.Int(0);
            if (!alliance) status.Bool(true); status.Long(70000); status.Raw(1);
            var update = Decode(alliance ? (byte)0x2c : (byte)0x1c, alliance ? (byte)0x96 : (byte)0x92, status.Bytes.ToArray());
            foreach (var e in update.Events) engine.Apply(e);
            check(update.Events.Count == 1 && engine.Fight.Actors[40].Cp == 70000 && engine.Fight.Actors[40].Party,
                "Member status updates captured combat power without replaying health or replacing membership");
            check(engine.Fight.Actors[40].Metadata is { } metadata && metadata.Level == (alliance ? 52 : 51) && metadata.Guild == (alliance ? "" : "Changed guild"),
                "Party/alliance status refreshes member level and supports leaving a guild");
            check(Decode(alliance ? (byte)0x2c : (byte)0x1c, alliance ? (byte)0x96 : (byte)0x92, status.Bytes.ToArray()[..^1]).Events.Count == 0,
                "Truncated party/alliance status cannot partially update combat power");
        }
        var large = Decode(2, 0x96, Full(4, 6), new()); var largeEngine = new CombatEngine();
        foreach (var e in large.Events) largeEngine.Apply(e);
        check(largeEngine.Fight.Actors.Values.Count(a => a.Party) == 24,
            "Captured alliance membership exceeds the ordinary ten-player party bound safely");
        check(Enumerable.Range(0, full.Length).All(n => Decode(2, 0x96, full[..n], new()).Events.Count == 0) &&
            Decode(2, 0x96, [..full, 0], new()).Events.Count == 0,
            "Alliance truncations and unknown trailers publish no partial roster");
        foreach (var e in Decode(0x1a, 0x96, []).Events) engine.Apply(e);
        check(engine.Fight.Actors.Values.All(a => !a.Party) && engine.Fight.PartyKnown,
            "Alliance disband clears captured membership");
    }
}
