using AionDpsMeter.Models;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

public enum DecodeState { Unsupported, Recognized, Rejected }
public sealed record DecodedCombatEvent(string Kind, Actor? Actor = null, DamageEvent? Damage = null, DefenseEvent? Defense = null, LootEnergyEvent? Energy = null, NetworkEvent? Network = null, PartyRoster? Party = null, ActorBuild? Build = null);
public sealed record DecodeResult(DecodeState State, IReadOnlyList<DecodedCombatEvent> Events, string Detail);

public interface IGamePacketDecoder
{
    string Target { get; }
    DecodeResult Decode(CapturedPayload payload);
}

// Explicit diagnostics-only mode. EuGameDecoder is a separate experimental option.
// This fallback never interprets unknown bytes as combat events.
public sealed class UnconfiguredGameDecoder : IGamePacketDecoder
{
    public string Target => "Game decoding disabled; capture diagnostics only";
    public DecodeResult Decode(CapturedPayload payload) => new(DecodeState.Unsupported, [], "Game wire decoder disabled; capture diagnostics only.");
}

// Public interchange format for testing analysis independently of packet decoding.
// This JSONL format is an AionDpsMeter format, NOT the game's network format.
public static class CombatEventFile
{
    public static IReadOnlyList<DecodedCombatEvent> Read(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Event file exceeds 32 MB.");
        var events = new List<DecodedCombatEvent>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length > 65536 || events.Count >= 250000) throw new InvalidDataException("Event file exceeds supported limits.");
            var item = JsonSerializer.Deserialize<DecodedCombatEvent>(line, new JsonSerializerOptions { MaxDepth = 16 }) ?? throw new InvalidDataException("Empty event.");
            bool valid = item.Kind switch {
                "actor" => item.Actor is { Id: > 0, Name.Length: > 0, Job: not null, Cp: >= 0, NpcId: >= 0 } actor && !(actor.Player && actor.NpcId > 0),
                "local-player" => item.Actor is { Id: > 0, Player: true, Name.Length: > 0, Job: not null, Cp: >= 0, NpcId: 0 },
                "damage" or "heal" => item.Damage is { Timestamp: >= 0, Damage: >= 0 },
                "defense" => item.Defense is { Timestamp: >= 0 } d && Enum.IsDefined(d.Outcome),
                "energy" => item.Energy is { Daily: >= 0, Bonus: >= 0, TimestampMs: >= 0 },
                "network" => item.Network is { Timestamp: >= 0, Kind.Length: > 0, Opcode.Length: > 0 } n && n.Fields.ValueKind == JsonValueKind.Object,
                "party" => item.Party != null,
                "build" => item.Build != null,
                _ => false
            };
            if (!valid) throw new InvalidDataException($"Invalid normalized event at record {events.Count + 1}.");
            if (item.Party != null) FightMetadata.Validate(item.Party);
            if (item.Build != null) FightMetadata.Validate(item.Build);
            events.Add(item);
        }
        return events;
    }
}
