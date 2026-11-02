using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

internal sealed record FinderMember([property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id, string Name, int Server, string Class, int Level, int GearScore, long CombatPower, bool Ready);
internal sealed record FinderGroup(int Id, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Leader, FinderMember[] Members);
internal sealed record FinderReady([property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long? Id, bool Ready);
internal static class PartyFinderWire
{
    static string Text(ref WireReader r) {
        int n = r.Var(); if (n > 4096) throw new InvalidDataException("Party finder string exceeds bounds.");
        return new UTF8Encoding(false, true).GetString(r.Take(n)).TrimEnd('\0');
    }
    internal static FinderMember Member(ref WireReader r)
    {
        byte flags = r.Byte(); r.Byte(); long id = unchecked((long)r.U64()); string name = Text(ref r);
        int template = unchecked((int)r.U32()), level = unchecked((int)r.U32()), gear = unchecked((int)r.U32());
        r.Bit(); bool ready = (flags & 2) != 0 && r.Bit(); int server = 0;
        if ((flags & 4) != 0) { server = r.U16(); r.U16(); }
        r.Byte(); long cp = unchecked((long)r.U64()); r.Byte();
        if ((flags & 16) != 0) r.U64(); r.Take(2);
        if (name.Length > 128 || id != 0 && (name.Length == 0 || level is < 1 or > 200)) throw new InvalidDataException("Invalid party finder member identity.");
        return new(id, name, server, CapturedClass.Name(template), level, gear, cp, ready);
    }
    internal static FinderGroup Group(ref WireReader r)
    {
        int id = unchecked((int)r.U32()); Text(ref r); r.Byte(); r.Take(6); long leader = unchecked((long)r.U64());
        r.Bit(); r.Take(2); int count = r.Var();
        if (count > 128) throw new InvalidDataException("Party finder roster exceeds bounds.");
        var members = new List<FinderMember>(count);
        for (int i = 0; i < count; i++) { var member = Member(ref r); if (member.Id != 0) members.Add(member); }
        // Installed P4 explicitly discards the trailing extension after the member list.
        r.Take(r.Remaining); return new(id, leader, members.ToArray());
    }
    internal static (string Kind, object Data)? Update(ref WireReader r, ushort opcode)
    {
        (string, object)? result = opcode switch {
            0x0b97 => ("party-finder-joined", Member(ref r)),
            0x1597 => SelfReady(ref r),
            0x1697 => ("party-finder-ready", new FinderReady(unchecked((long)r.U64()), r.Bit())),
            0x1d97 => r.U16() == 0 ? ("party-finder-left", new { }) : null,
            0x2a97 => Left(ref r),
            _ => throw new InvalidDataException("Unknown party finder update.")
        };
        if (r.Remaining != 0) throw new InvalidDataException("Unexpected party finder update trailer.");
        return result;
    }
    static (string, object)? SelfReady(ref WireReader r) { ushort result = r.U16(); bool ready = r.Bit(); return result == 0 ? ("party-finder-ready", new FinderReady(null, ready)) : null; }
    static (string, object) Left(ref WireReader r) { r.Byte(); return ("party-finder-left", new { }); }
}

internal sealed class PartyFinderTracker
{
    FinderGroup? group;
    (int Server, string Name)? self;
    long selfId;
    int? consumed;
    int rosterCount;
    long? accountAt;
    int selfCp;
    int selfGear;
    bool worldIdentityAvailable;
    long timestamp = -1;
    internal void Follow(Actor actor)
    {
        var next = (actor.Server, actor.Name);
        bool changed = selfId > 0 && actor.PersistentId > 0 ? selfId != actor.PersistentId : self is { } prior && prior != next;
        if (changed) Clear();
        self = next;
        worldIdentityAvailable = true;
        selfCp = actor.Cp;
        selfGear = actor.Metadata?.GearScore ?? 0;
        if (actor.PersistentId > 0) selfId = actor.PersistentId;
    }
    internal void Clear() { group = null; self = null; selfId = 0; selfCp = 0; selfGear = 0; worldIdentityAvailable = false; consumed = null; rosterCount = 0; accountAt = null; timestamp = -1; }
    bool IsSelf(FinderMember member) => selfId > 0 ? member.Id == selfId : self is { } owner && member.Name == owner.Name && (member.Server == 0 || owner.Server == 0 || member.Server == owner.Server);
    internal void Roster(int count) { rosterCount = count; accountAt = null; }
    internal void AccountCharacters(long at) { if (at >= 0 && at >= timestamp) accountAt = at; }
    internal void Map()
    {
        // The world reducer forgets the current entity at map change. Keep the
        // name for recruitment matching, but do not reuse stale world stats.
        worldIdentityAvailable = false; selfId = 0; selfCp = 0; selfGear = 0;
        if (group != null && group.Members.Length <= rosterCount) { consumed = group.Id; group = null; }
    }
    internal void Apply(NetworkEvent e)
    {
        if (!e.Kind.StartsWith("party-finder", StringComparison.Ordinal) || e.Timestamp < timestamp) return;
        timestamp = e.Timestamp;
        switch (e.Kind) {
            case "party-finder":
                var incoming = e.Fields.Deserialize<FinderGroup>();
                if (incoming == null || incoming.Members.Length > 128 || incoming.Id == consumed) return;
                if (incoming.Members.Any(IsSelf)) group = incoming with { Members = incoming.Members.ToArray() };
                else if (group?.Id == incoming.Id) group = null;
                break;
            case "party-finder-joined" when group != null:
                var member = e.Fields.Deserialize<FinderMember>()!;
                if (group.Members.Length < 128 || group.Members.Any(m => m.Id == member.Id)) group = group with { Members = group.Members.Where(m => m.Id != member.Id).Append(member).ToArray() };
                break;
            case "party-finder-ready" when group != null:
                var ready = e.Fields.Deserialize<FinderReady>()!;
                group = group with { Members = group.Members.Select(m => (ready.Id is { } id ? id == m.Id : IsSelf(m)) ? m with { Ready = ready.Ready } : m).ToArray() };
                break;
            case "party-finder-left": group = null; break;
        }
    }
    internal FinderGroup? Snapshot(long? now = null, Actor? local = null) {
        if (accountAt is long account && (now ?? timestamp) - account > 5000) { rosterCount = 0; accountAt = null; }
        if (group is not { } current || current.Members.Length <= rosterCount || !current.Members.Any(IsSelf)) return null;
        bool matching = worldIdentityAvailable && local != null && (selfId > 0 ? local.PersistentId == selfId : self == (local.Server, local.Name));
        int cp = matching ? local!.Cp : selfCp;
        int gear = matching ? local!.Metadata?.GearScore ?? 0 : selfGear;
        return current with { Members = current.Members.Select(m => IsSelf(m) ? m with {
            CombatPower = m.CombatPower == 0 ? Math.Max(0, cp) : m.CombatPower,
            GearScore = m.GearScore == 0 ? Math.Max(0, gear) : m.GearScore } : m).ToArray() };
    }
}
