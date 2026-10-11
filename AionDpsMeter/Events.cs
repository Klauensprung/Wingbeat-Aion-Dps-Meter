using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDpsMeter.Models;

// Application-level events, not a description of the game's wire protocol.
// IDs are opaque: no undocumented bit fields or class/skill encodings are assumed.
public sealed record DamageEvent
{
    public long Timestamp { get; init; }
    public int ActorId { get; init; }
    public int TargetId { get; init; }
    public int SkillCode { get; init; }
    public int Damage { get; init; }
    public bool IsHeal { get; init; }
    public bool IsCritical { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

public enum DefenseOutcome { Block = 1, Parry = 2, Evade = 3, Resist = 4, TargetAvoided = 5 }
public sealed record DefenseEvent
{
    public long Timestamp { get; init; }
    public int ActorId { get; init; }
    public int TargetId { get; init; }
    public int SkillCode { get; init; }
    public DefenseOutcome Outcome { get; init; }
}

public sealed record LootEnergyEvent(int Daily, int Bonus, bool FromLoginList, long TimestampMs);
public sealed record CapturedPayload(int SourcePort, int DestinationPort, byte[] Data, string? DeviceName = null)
{
    public string Connection { get; init; } = "unknown";
    public long? TimestampMs { get; init; }
    public long? CounterTimestampMs { get; init; }
}

public sealed record NetworkEvent(string Kind, long Timestamp, string Opcode, JsonElement Fields);
