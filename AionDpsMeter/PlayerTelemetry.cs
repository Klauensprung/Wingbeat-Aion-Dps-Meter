using System.IO;

namespace AionDpsMeter.Local;

public sealed record PartyProfile(string Name, int Server, int CombatPower, byte LatencyState) { public uint CharacterId { get; init; } }
public sealed record PingReading(int Milliseconds, long Timestamp, string Source);

internal static class PlayerTelemetry
{
    // The server echoes the client's clock. This measures the local connection,
    // never another player's ping. Counter clocks are supplied only by live capture.
    internal static PingReading? ReadPing(ref WireReader reader, long receivedUnixMs, long? receivedCounterMs)
    {
        reader.Take(2);
        ulong clock = reader.U64();
        if (clock > long.MaxValue || receivedUnixMs < 0) return null;
        long echoed = (long)clock;
        if (receivedCounterMs is >= 0 and < long.MaxValue - 16777216000L) {
            long delta = receivedCounterMs.Value + 16777216000L - echoed;
            if (delta is >= 0 and <= 1000) return new((int)delta, receivedUnixMs, "Client clock echo (estimate)");
        }
        // Legacy clients echo milliseconds since the .NET epoch.
        if (echoed >= 62135596800000L) {
            long delta = receivedUnixMs - (echoed - 62135596800000L);
            if (delta is >= 0 and <= 1000) return new((int)delta, receivedUnixMs, "Client clock echo (estimate)");
        }
        return null;
    }

    internal static PartyProfile[] ReadParty(ref WireReader reader)
    {
        reader.U32();
        reader.Take(reader.Byte()); // party label, unrelated to player identity
        int capacity = reader.Byte();
        if (capacity is < 1 or > 10) throw new InvalidDataException("Unsupported party capacity.");
        reader.U32(); reader.Take(2); reader.U64(); reader.Bit(); reader.Take(2);
        int count = reader.Var();
        if (count > capacity) throw new InvalidDataException("Invalid party member count.");
        var members = new List<PartyProfile>(count);
        var slots = new HashSet<byte>();
        for (int i = 0; i < count; i++) {
            byte mask = reader.Byte(), slot = reader.Byte();
            if ((mask & 0x80) != 0 || slot > 10 || !slots.Add(slot)) throw new InvalidDataException("Unsupported party member flags/slot.");
            ulong character = reader.U64();
            string name;
            if (mask == 0) {
                if (reader.Byte() != 0) throw new InvalidDataException("Inactive party slot has a name.");
                name = "";
            } else name = PlayerIdentity.Name(ref reader);
            reader.U32(); uint level = reader.U32();
            if (mask != 0 && (level is < 1 or > 200)) throw new InvalidDataException("Invalid party level.");
            if ((mask & 1) != 0) reader.U32();
            reader.U32(); // item level is NOT combat power
            if ((mask & 2) != 0) reader.Bit();
            reader.Bit();
            if ((mask & 4) != 0) reader.U16();
            if ((mask & 8) != 0) reader.U16();
            reader.Byte();
            ulong cp = reader.U64();
            int tickets = reader.Var();
            if (tickets > 128 || cp > 2000000) throw new InvalidDataException("Invalid party metadata bounds.");
            reader.Take(checked(tickets * 5));
            if ((mask & 16) != 0) reader.U64();
            reader.Byte(); byte state = reader.Byte();
            int server = (int)(character >> 48);
            if (mask != 0 && (server is < 1000 or > 9999 || (uint)character == 0)) throw new InvalidDataException("Invalid party character identity.");
            if (mask != 0) members.Add(new(name, server, (int)cp, state) { CharacterId = (uint)character });
        }
        // EU records include a two-byte trailer after the fixed slot list.
        // Its meaning is unknown; it does not contribute to CP or latency.
        if (reader.Remaining == 2 && reader.Byte() == 0) reader.Byte();
        if (reader.Remaining != 0) throw new InvalidDataException("Unknown trailing party fields.");
        var result = members.ToArray(); Validate(result); return result;
    }

    internal static void Validate(PartyProfile[] profiles)
    {
        if (profiles == null || profiles.Length > 10 || profiles.Any(p => p == null || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 24 || p.Server is < 1000 or > 9999 || p.CombatPower is < 0 or > 2000000) ||
            profiles.Select(p => (p.Name, p.Server)).Distinct().Count() != profiles.Length)
            throw new InvalidDataException("Invalid party profiles.");
    }

    internal static void ApplyProfiles(Fight fight)
    {
        foreach (var actor in fight.Actors.Values.ToArray()) {
            if (actor.OriginalEntityId > 0 || actor.NpcId > 0 || (!actor.Player && actor.CharacterId == 0)) continue;
            if (actor.Cp > 0 && (actor.CpSource == "Entered locally" || (actor.CpSource == "Unknown" && actor.ProfileSource == "Entered locally"))) continue;
            if (actor.Id == fight.LocalPlayer && actor.Cp > 0 && actor.CpSource == "Local character packet") continue;
            bool linked = actor.CharacterId > 0;
            var candidates = fight.PartyProfiles.Where(p =>
                (linked ? p.CharacterId == actor.CharacterId && (!actor.Player || p.Name == actor.Name) : p.Name == actor.Name) &&
                (actor.Server == 0 || p.Server == actor.Server)).ToArray();
            // Never confuse a global character ID with an in-zone entity ID.
            bool unique = candidates.Length == 1 && fight.Actors.Values.Count(a => a.OriginalEntityId == 0 && (linked ? a.CharacterId == actor.CharacterId : a.Player && a.Name == actor.Name) &&
                (a.Server == 0 || a.Server == candidates[0].Server)) == 1;
            if (unique && candidates[0].CombatPower > 0)
                fight.Actors[actor.Id] = actor with { Cp = candidates[0].CombatPower, CpSource = "Party packet",
                    Name = candidates[0].Name, Player = true, Server = candidates[0].Server };
            else if (actor.CpSource == "Party packet")
                fight.Actors[actor.Id] = actor with { Cp = 0, CpSource = "Unknown" };
        }
    }

    internal static int? PingFor(Fight fight, int actorId) => actorId == fight.LocalPlayer && actorId > 0 ? fight.LocalPing?.Milliseconds : null;
}
