using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed class CombatEngine
{
    public readonly object Gate = new();
    readonly IGamePacketDecoder decoder;
    readonly Queue<Fight> completed = new();
    readonly object saveGate = new();
    readonly Dictionary<int, long> lastDeaths = new();
    long unsupportedPayloads;
    long revision;
    long identityEvents;
    public long IdentityEvents { get { lock (Gate) return identityEvents; } }
    public long UnsupportedPayloads => Interlocked.Read(ref unsupportedPayloads);
    public string DecoderStatus => decoder.Target;
    public string DecoderDetails
    {
        get { lock (Gate) return decoder is EuGameDecoder eu ? $"records {eu.Records:N0}, unsupported {eu.UnknownRecords:N0}, rejected {eu.RejectedRecords:N0}, framing errors {eu.FramingErrors:N0}" : $"undecoded payloads {UnsupportedPayloads:N0}"; }
    }
    public Fight Fight { get; private set; } = new();
    public CombatEngine(IGamePacketDecoder? decoder = null) { this.decoder = decoder ?? new UnconfiguredGameDecoder(); }

    public void Process(CapturedPayload payload)
    {
        lock (Gate)
        {
            var result = decoder.Decode(payload);
            if (result.State != DecodeState.Recognized) { Interlocked.Increment(ref unsupportedPayloads); return; }
            foreach (var item in result.Events) Apply(item);
        }
    }
    public void Apply(DecodedCombatEvent item)
    {
        lock (Gate)
        {
            switch (item.Kind)
            {
                case "actor" when item.Actor != null: UpdateActor(item.Actor); break;
                case "local-player" when item.Actor is { Player: true } local:
                    // A changed known local identity starts a scene/session.
                    // Repeated records for the same character are updates, and
                    // must not discard other players' names or split their fight.
                    if (Fight.LocalPlayer > 0 && (Fight.LocalPlayer != local.Id || !Fight.Actors.TryGetValue(local.Id, out var knownLocal) ||
                        knownLocal.Name != local.Name || (knownLocal.Server > 0 && local.Server > 0 && knownLocal.Server != local.Server)))
                    {
                        if (Fight.HasRecordedData) completed.Enqueue(Snapshot());
                        NewFight(Fight.Label);
                        Fight.Actors.Clear(); Fight.Specs.Clear(); Fight.Builds.Clear(); Fight.PartyProfiles = []; Fight.LocalPing = null; Fight.LocalStats = null; Fight.PartyKnown = false; Fight.PartySource = "Not captured"; Fight.LocalPlayer = local.Id;
                    }
                    Fight.LocalPlayer = local.Id;
                    UpdateActor(local); break;
                case "damage" when item.Damage != null: RecordDamage(item.Damage, false); break;
                case "heal" when item.Damage != null: RecordDamage(item.Damage, true); break;
                case "defense" when item.Defense != null:
                    EnsureActor(item.Defense.ActorId); EnsureActor(item.Defense.TargetId); Fight.Defense.Add(item.Defense); revision++; break;
                case "energy" when item.Energy != null: Fight.Energy = item.Energy; revision++; break;
                case "player-stats" when item.Stats != null:
                    CharacterReadings.Validate(item.Stats);
                    if (Fight.LocalStats == null || item.Stats.Timestamp >= Fight.LocalStats.Timestamp) {
                        Fight.LocalStats = CharacterReadings.Copy(item.Stats); revision++;
                    }
                    break;
                case "character-link" when item.Link is { EntityId: > 0, CharacterId: > 0 } link:
                    EnsureActor(link.EntityId);
                    if (Fight.Actors[link.EntityId].NpcId == 0) {
                        Fight.Actors[link.EntityId] = Fight.Actors[link.EntityId] with { CharacterId = link.CharacterId };
                        PlayerTelemetry.ApplyProfiles(Fight); revision++;
                    }
                    break;
                case "party-profiles" when item.Profiles != null:
                    PlayerTelemetry.Validate(item.Profiles); Fight.PartyProfiles = [..item.Profiles]; PlayerTelemetry.ApplyProfiles(Fight); revision++; break;
                case "ping" when item.Ping is { Milliseconds: >= 0 and <= 1000, Timestamp: >= 0, Source.Length: > 0 } ping:
                    if (Fight.LocalPing == null || ping.Timestamp >= Fight.LocalPing.Timestamp) { Fight.LocalPing = ping; revision++; }
                    break;
                case "party" when item.Party != null: FightMetadata.SetRoster(Fight, item.Party); revision++; break;
                case "build" when item.Build != null: FightMetadata.SetBuild(Fight, item.Build); revision++; break;
                case "network" when item.Network != null:
                    if (item.Network.Fields.ValueKind != System.Text.Json.JsonValueKind.Object) break;
                    if (item.Network.Kind == "entity-death" && item.Network.Timestamp >= 0 &&
                        item.Network.Fields.TryGetProperty("EntityId", out var deathId) && deathId.ValueKind == System.Text.Json.JsonValueKind.Number && deathId.TryGetInt32(out int dead) && dead > 0 &&
                        (!lastDeaths.TryGetValue(dead, out long previousDeath) || item.Network.Timestamp - previousDeath > 1000)) {
                        Fight.Deaths.Add(new(dead, item.Network.Timestamp)); lastDeaths[dead] = item.Network.Timestamp;
                    }
                    Fight.Network.Add(item.Network); revision++; break;
                default: throw new ArgumentException("Incomplete or unsupported decoded event.");
            }
        }
    }
    void UpdateActor(Actor actor)
    {
        if (actor.Id <= 0 || actor.NpcId < 0 || (actor.NpcId > 0 && actor.Player) || string.IsNullOrWhiteSpace(actor.Name)) throw new ArgumentException("Invalid actor identity.");
        if (Fight.Actors.TryGetValue(actor.Id, out var previous))
        {
            bool replaced = previous.Player && actor.Player && previous.Name != actor.Name ||
                previous.NpcId > 0 && actor.NpcId > 0 && previous.NpcId != actor.NpcId ||
                previous.Player && actor.NpcId > 0 || previous.NpcId > 0 && actor.Player;
            if (replaced)
            {
                // A reused entity ID must not rename a different creature/player in saved hits.
                bool participated = Fight.Damage.Concat(Fight.Healing).Any(e => e.ActorId == actor.Id || e.TargetId == actor.Id) ||
                    Fight.Defense.Any(e => e.ActorId == actor.Id || e.TargetId == actor.Id) || Fight.Deaths.Any(e => e.EntityId == actor.Id);
                if (participated) ArchiveActor(actor.Id);
                Fight.Builds.Remove(actor.Id);
            }
            else
            {
                if (previous.Cp > 0 && (previous.CpSource == "Entered locally" ||
                    (previous.CpSource == "Unknown" && previous.ProfileSource == "Entered locally")))
                    actor = actor with { Cp = previous.Cp, CpSource = previous.CpSource };
                actor = actor with { Player = actor.Player || previous.Player, Party = actor.Party || previous.Party,
                    Cp = actor.Cp > 0 ? actor.Cp : previous.Cp, Job = actor.Job == "Unknown" ? previous.Job : actor.Job,
                    CpSource = actor.Cp > 0 ? actor.CpSource : previous.CpSource,
                    CharacterId = actor.CharacterId > 0 ? actor.CharacterId : previous.CharacterId,
                    Server = actor.Server > 0 ? actor.Server : previous.Server, NpcId = actor.NpcId > 0 ? actor.NpcId : previous.NpcId,
                    ProfileSource = actor.Cp == 0 && actor.Job == "Unknown" ? previous.ProfileSource : actor.ProfileSource };
            }
        }
        Fight.Actors[actor.Id] = actor; PlayerTelemetry.ApplyProfiles(Fight); identityEvents++; revision++;
    }
    void ArchiveActor(int id)
    {
        int archived = int.MaxValue;
        while (Fight.Actors.ContainsKey(archived)) archived--;
        Fight.Actors[archived] = Fight.Actors[id] with { Id = archived, OriginalEntityId = id, Party = false };
        for (int i = 0; i < Fight.Damage.Count; i++) {
            var e = Fight.Damage[i];
            if (e.ActorId == id || e.TargetId == id) Fight.Damage[i] = e with { ActorId = e.ActorId == id ? archived : e.ActorId, TargetId = e.TargetId == id ? archived : e.TargetId };
        }
        for (int i = 0; i < Fight.Healing.Count; i++) {
            var e = Fight.Healing[i];
            if (e.ActorId == id || e.TargetId == id) Fight.Healing[i] = e with { ActorId = e.ActorId == id ? archived : e.ActorId, TargetId = e.TargetId == id ? archived : e.TargetId };
        }
        for (int i = 0; i < Fight.Defense.Count; i++) {
            var e = Fight.Defense[i];
            if (e.ActorId == id || e.TargetId == id) Fight.Defense[i] = e with { ActorId = e.ActorId == id ? archived : e.ActorId, TargetId = e.TargetId == id ? archived : e.TargetId };
        }
        for (int i = 0; i < Fight.Deaths.Count; i++)
            if (Fight.Deaths[i].EntityId == id) Fight.Deaths[i] = Fight.Deaths[i] with { EntityId = archived };
        lastDeaths.Remove(id);
        if (Fight.Builds.Remove(id, out var build)) Fight.Builds[archived] = FightMetadata.Copy(build) with { ActorId = archived };
    }
    internal void RecordDamage(DamageEvent damage, bool healing)
    {
        if (damage.Timestamp < 0 || damage.Damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
        lock (Gate)
        {
            EnsureActor(damage.ActorId); EnsureActor(damage.TargetId);
            (healing ? Fight.Healing : Fight.Damage).Add(damage with { IsHeal = healing });
            revision++;
        }
    }
    void EnsureActor(int id) { if (id > 0) Fight.Actors.TryAdd(id, new(id, $"Entity {id}", false, false, 0, "Unknown", 0)); }
    public Fight Snapshot()
    {
        lock (Gate) return Fight.Copy();
    }
    // UI-owned snapshots are read-only. Reuse one until the engine changes instead of
    // copying every event list every refresh, including when nothing was captured.
    public Fight SnapshotIfChanged(Fight? previous, ref long observedRevision)
    {
        lock (Gate)
        {
            if (previous != null && observedRevision == revision) return previous;
            var snapshot = Snapshot(); observedRevision = revision; return snapshot;
        }
    }
    public void SetLabel(string label) { lock (Gate) { Fight.Label = label; revision++; } }
    public void SetMetadata(Fight edited)
    {
        lock (Gate)
        {
            if (Fight.Id != edited.Id) throw new InvalidOperationException("The fight changed while editing. Open character details again for the current fight.");
            foreach (var (id, actor) in edited.Actors)
            {
                if (!Fight.Actors.TryGetValue(id, out var current) || current.Name != actor.Name || current.NpcId != actor.NpcId)
                    throw new InvalidOperationException("A character identity changed while editing. Open character details again.");
            }
            foreach (var (id, actor) in edited.Actors) Fight.Actors[id] = actor;
            Fight.Builds = edited.Builds.ToDictionary(p => p.Key, p => FightMetadata.Copy(p.Value));
            Fight.Specs = edited.Specs.ToDictionary(p => p.Key, p => p.Value with { Choices = p.Value.Choices.ToArray() });
            Fight.PartyKnown = edited.PartyKnown; Fight.PartySource = edited.PartySource; revision++;
        }
    }
    public void NewFight(string label)
    {
        lock (Gate)
        {
            var previous = Fight;
            Fight = new() { Label = label, LocalPlayer = previous.LocalPlayer, Actors = previous.Actors.Where(p => p.Value.OriginalEntityId == 0).ToDictionary(), Specs = new(previous.Specs), Energy = previous.Energy, PartyProfiles = [..previous.PartyProfiles], LocalPing = previous.LocalPing, LocalStats = CharacterReadings.Copy(previous.LocalStats),
                Builds = previous.Builds.Where(p => previous.Actors.GetValueOrDefault(p.Key)?.OriginalEntityId == 0).ToDictionary(p => p.Key, p => FightMetadata.Copy(p.Value)), PartyKnown = previous.PartyKnown, PartySource = previous.PartySource };
            lastDeaths.Clear();
            revision++;
        }
    }
    public void EndFight(string label)
    {
        lock (Gate)
        {
            if (Fight.HasRecordedData) completed.Enqueue(Snapshot());
            NewFight(label);
        }
    }
    public Fight[] SaveCompleted(FightStore store) => SaveCompleted(store.Save);
    internal Fight[] SaveCompleted(Action<Fight> save)
    {
        // Serialize writers, but never keep ingestion waiting on disk I/O. A failed
        // write leaves the snapshot at the head for the next attempt.
        lock (saveGate)
        {
            var saved = new List<Fight>();
            while (true)
            {
                Fight fight;
                lock (Gate) { if (!completed.TryPeek(out fight!)) break; }
                save(fight);
                lock (Gate) completed.Dequeue();
                saved.Add(fight);
            }
            return saved.ToArray();
        }
    }
}
