using System.IO;
using System.Text;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record PlayerMetadataSnapshot(int? Level = null, string? Guild = null, long LevelAt = -1, long GuildAt = -1, int? GearScore = null, long GearScoreAt = -1);
internal static class PlayerMetadata
{
    internal static string Guild(ref WireReader r)
    {
        r.Take(8); string name = Text(ref r); r.Take(17); Text(ref r); return name;
    }
    static string Text(ref WireReader r) {
        int length = r.Var(); if (length > 4096) throw new InvalidDataException("Guild name exceeds limit.");
        return new UTF8Encoding(false, true).GetString(r.Take(length)).TrimEnd('\0');
    }
    internal static void Validate(PlayerMetadataSnapshot value) {
        if (value.GearScore < 0 || value.GearScoreAt < -1 || (value.GearScore != null) != (value.GearScoreAt >= 0) || value.Level < 0 || value.Guild?.Length > 4096 || value.LevelAt < -1 || value.GuildAt < -1 ||
            (value.Level != null) != (value.LevelAt >= 0) || (value.Guild != null) != (value.GuildAt >= 0))
            throw new InvalidDataException("Invalid player metadata.");
    }
    internal static PlayerMetadataSnapshot? Merge(PlayerMetadataSnapshot? previous, PlayerMetadataSnapshot? incoming)
    {
        if (incoming == null) return previous; Validate(incoming);
        if (previous == null) return incoming;
        bool level = incoming.LevelAt >= previous.LevelAt && incoming.Level != null;
        bool guild = incoming.GuildAt >= previous.GuildAt && incoming.Guild != null;
        bool gear = incoming.GearScoreAt >= previous.GearScoreAt && incoming.GearScore != null;
        return new(level ? incoming.Level : previous.Level, guild ? incoming.Guild : previous.Guild,
            level ? incoming.LevelAt : previous.LevelAt, guild ? incoming.GuildAt : previous.GuildAt,
            gear ? incoming.GearScore : previous.GearScore, gear ? incoming.GearScoreAt : previous.GearScoreAt);
    }
    internal static PlayerMetadataSnapshot? Apply(PlayerMetadataSnapshot? previous, NetworkEvent message)
    {
        if (message.Timestamp < 0) return previous;
        return Merge(previous, message.Kind == "player-level" ? new(Level: message.Fields.GetProperty("Level").GetInt32(), LevelAt: message.Timestamp) :
            new(Guild: message.Fields.GetProperty("Guild").GetString() ?? "", GuildAt: message.Timestamp));
    }
}
