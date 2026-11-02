using System.Text.Json;
using System.IO;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal static class LocalPositionTests
{
    internal static void Run(Action<bool, string> check, string temp)
    {
        NetworkEvent Message(string kind, long time, object fields) => new(kind, time, "synthetic", JsonSerializer.SerializeToElement(fields));
        var tracker = new LocalPositionTracker();
        var movement = new LocalPositionTracker();
        movement.Apply(Message("local-zone",1000,new {MapId=1010,X=0,Y=0,Z=0}));
        movement.Apply(Message("actor-position",2000,new {ActorId=1,X=100,Y=0,Z=0,Stopped=false}),1);
        check(!movement.MovementStopped,"Own movement packet enables route prediction");
        movement.Apply(Message("actor-position",3000,new {ActorId=2,X=200,Y=0,Z=0,Stopped=true}),1);
        check(!movement.MovementStopped&&movement.Latest?.X==100,"Other actors cannot stop or move the route");
        movement.Apply(Message("actor-position",4000,new {ActorId=1,X=200,Y=0,Z=0,Stopped=true}),1);
        check(movement.MovementStopped,"Own stop packet stops route prediction");
        var effects = new LocalPositionTracker();
        effects.Apply(Message("local-zone", 1000, new { MapId = 1010, X = 0, Y = 0, Z = 0 }));
        effects.Apply(Message("abnormal-apply", 2000, new { CasterId = 2, TargetId = 1, CasterX = 300, CasterY = 400, CasterZ = 500 }), 1);
        check(effects.Latest?.X == 0, "Effects applied to the local player by somebody else cannot relocate the player");
        effects.Apply(Message("abnormal-refresh", 3000, new { CasterId = 1, TargetId = 2, CasterX = 300, CasterY = 400, CasterZ = 500 }), 1);
        effects.Apply(Message("abnormal-apply", 2500, new { CasterId = 1, CasterX = 900, CasterY = 900, CasterZ = 900 }), 1);
        check(effects.Latest?.X == 0, "Effects cast on another target cannot move the local marker to their range base");
        effects.Apply(Message("abnormal-refresh", 3000, new { CasterId = 1, TargetId = 1, CasterX = 300, CasterY = 400, CasterZ = 500 }), 1);
        check(effects.Latest == new CapturedPosition(1010, 300, 400, 500, 3000),
            "Self-targeted local effects can refresh the known position");
        effects.Clear(true);
        effects.Apply(Message("abnormal-refresh", 4000, new { CasterId = 1, CasterX = 300, CasterY = 400, CasterZ = 500 }), 1);
        check(effects.Latest == null, "Effect coordinates cannot resurrect a position after character map attribution is cleared");
        tracker.Apply(Message("local-position", 1000, new { X = 1, Y = 2, Z = 3 }));
        check(tracker.Latest == null, "Position-only packets cannot invent the current map");
        tracker.Apply(Message("local-zone", 2000, new { MapId = 1010, X = 10, Y = -20, Z = 30 }));
        tracker.Apply(Message("local-position", 3000, new { X = 11, Y = -21, Z = 31 }));
        tracker.Apply(Message("local-zone", 2500, new { MapId = 1110, X = 0, Y = 0, Z = 0 }));
        check(tracker.Latest == new CapturedPosition(1010, 11, -21, 31, 3000),
            "Loaded positions use the known zone and stale zone observations cannot replace newer coordinates");
        tracker.Apply(Message("local-zone", 4000, new { MapId = 1110 }));
        check(tracker.Latest == null, "A zone transition without coordinates clears the previous map marker");
        tracker.Apply(Message("local-position", 5000, new { X = "invalid", Y = 0, Z = 0 }));
        check(tracker.Latest == null && !LocalPositionTracker.Matches(new(999999, 0, 0, 0, 0), "World/World_L/World_L_A") &&
            LocalPositionTracker.Matches(new(1010, 0, 0, 0, 0), "World/World_L/World_L_A"),
            "Malformed coordinates and unmapped zones never put the player on a guessed terrain map");
        byte[] F(float value) => BitConverter.GetBytes(value);
        DecodeResult Decode(byte high, byte[] payload) => new EuGameDecoder().Decode(new CapturedPayload(13328, 50000,
            [(byte)(payload.Length + 6), high, 0x36, ..payload]) { TimestampMs = 1000 });
        byte[] loaded = [0, 0, 0, 0, 0, 0, ..F(123), ..F(-456), ..F(789), ..BitConverter.GetBytes(1u), 1];
        var reading = Decode(0x23, loaded).Events.Single().Network!;
        check(reading.Kind == "local-position" && reading.Fields.GetProperty("Y").GetSingle() == -456 &&
            Decode(0x23, loaded[..^1]).Events.Count == 0 && Decode(0x23, [..loaded, 0]).Events.Count == 0,
            "Loaded-position decoding preserves signed coordinates and rejects truncated or extended trailers");
        byte[] zone = [0, 0, 0, 0, ..BitConverter.GetBytes(1010u), ..new byte[8], ..F(123), ..F(-456), ..F(789), ..new byte[18]];
        check(Decode(0x21, zone).Events.Single().Network!.Fields.GetProperty("X").GetSingle() == 123 &&
            Decode(0x23, [..new byte[6], ..F(float.NaN), ..F(0), ..F(0), ..BitConverter.GetBytes(1u), 1]).Events.Count == 0,
            "Zone-entry position offsets agree with the reference layout and non-finite coordinates are rejected");
        var prepare = new PartyWireTests.Writer(); prepare.Raw(zone[..42]); prepare.Var(2); prepare.Zero(26);
        prepare.Var(300); prepare.Text("Entry cutscene"); prepare.Raw(1);
        var fullZone = prepare.Bytes.ToArray();
        check(Decode(0x21, fullZone).Events.Single().Network!.Fields.GetProperty("AwaitingIdentity").GetBoolean() &&
            Enumerable.Range(0, fullZone.Length).All(n => Decode(0x21, fullZone[..n]).Events.Count == 0) && Decode(0x21, [..fullZone, 0]).Events.Count == 0,
            "Map preparation validates environment groups, teleport effect and cutscene before publishing an identity boundary");
        var engine = new CombatEngine();
        engine.Apply(new("local-player", Actor: new(1, "Player 1", true, false, 0, "Cleric", 1)));
        engine.Apply(new("network", Network: Message("local-zone", 1000, new { MapId = 1010, X = 1, Y = 2, Z = 3 })));
        engine.NewFight("Reset");
        check(engine.Position.Latest?.MapId == 1010 && engine.Fight.LocalPosition == engine.Position.Latest,
            "Ordinary DPS resets preserve the last captured player position in both live tracking and fight metadata");
        var snapshot = engine.Snapshot();
        var store = new FightStore(Path.Combine(temp, "saved-position")); store.Save(snapshot);
        var restored = store.Load(Path.Combine(store.Root, snapshot.Id + ".json"));
        check(restored.LocalPosition == snapshot.LocalPosition && restored.Network.Count == 0,
            "Saved fights retain position metadata even when the zone packet preceded the latest combat reset");
        engine.Apply(new("network", Network: Message("local-position", 1500, new { X = 4, Y = 5, Z = 6 })));
        check(snapshot.LocalPosition?.X == 1 && engine.Snapshot().LocalPosition?.X == 4,
            "New position readings do not mutate a previously saved fight snapshot");
        engine.Apply(new("local-player", Actor: new(2, "Player 2", true, false, 0, "Cleric", 1)));
        engine.Apply(new("network", Network: Message("local-position", 2000, new { X = 1, Y = 2, Z = 3 })));
        check(engine.Position.Latest == null && engine.Fight.LocalPosition == null, "Changing characters clears position and map attribution until a new zone is captured");
        var invalid = snapshot.Copy(); invalid.Id = Guid.NewGuid().ToString("N"); invalid.LocalPosition = new(0, 1, 2, 3, 1000); store.Save(invalid);
        bool rejected = false;
        try { store.Load(Path.Combine(store.Root, invalid.Id + ".json")); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Imported fight metadata cannot introduce a player position with an invalid map ID");
    }
}
