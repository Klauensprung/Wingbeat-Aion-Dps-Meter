using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed class CombatEngine
{
    public readonly object Gate = new();
    readonly IGamePacketDecoder decoder;
    readonly PlayerProfileCache profiles;
    bool cachedLocalIdentity;
    readonly Queue<Fight> completed = new();
    readonly object saveGate = new();
    readonly Dictionary<int, long> lastDeaths = new();
    const long IdleResetMs = 15000;
    const long RecoveryWindowMs = 300000, AddPhaseWindowMs = 120000;
    bool recoveryBlocked;
    long? lastCombat;
    Fight? resumable;
    long resumeStoppedAt;
    HashSet<int> resumeTargets = new();
    HashSet<int> phaseAnchors = new();
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
    public bool HasCompletedFights { get { lock (Gate) return completed.Count > 0; } }
    public CombatEngine(IGamePacketDecoder? decoder = null, PlayerProfileCache? profiles = null) { this.decoder = decoder ?? new UnconfiguredGameDecoder(); this.profiles = profiles ?? new(); }
    void RefreshProfiles()
    {
        PlayerTelemetry.ApplyProfiles(Fight); profiles.Update(Fight);
        if (Fight.LocalPlayer == 0 || cachedLocalIdentity) {
            Fight.LocalPlayer = profiles.MatchLocal(Fight);
            cachedLocalIdentity = Fight.LocalPlayer > 0;
        }
    }

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
                    // The local character/CP packet (33 36) is an explicit reset
                    // boundary, even if the server, entity ID and CP are unchanged.
                    bool changedIdentity = Fight.LocalPlayer > 0 && (Fight.LocalPlayer != local.Id || !Fight.Actors.TryGetValue(local.Id, out var knownLocal) ||
                        knownLocal.Name != local.Name || (knownLocal.Server > 0 && local.Server > 0 && knownLocal.Server != local.Server));
                    if (Fight.HasRecordedData) completed.Enqueue(Snapshot());
                    NewFight(Fight.Label); // Also invalidates any idle/add-phase recovery.
                    if (changedIdentity)
                    {
                        Fight.Actors.Clear(); Fight.Specs.Clear(); Fight.Builds.Clear(); Fight.PartyProfiles = []; Fight.LocalPing = null; Fight.LocalStats = null; Fight.PartyKnown = false; Fight.PartySource = "Not captured"; Fight.LocalPlayer = local.Id;
                    }
                    Fight.LocalPlayer = local.Id; cachedLocalIdentity = false; profiles.RememberLocal(local);
                    UpdateActor(local); break;
                case "damage" when item.Damage != null: RecordDamage(item.Damage, false); break;
                case "heal" when item.Damage != null: RecordDamage(item.Damage, true); break;
                case "defense" when item.Defense != null:
                    BeginCombat(item.Defense.Timestamp, item.Defense.ActorId, item.Defense.TargetId, false);
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
                        RefreshProfiles(); revision++;
                    }
                    break;
                case "party-profiles" when item.Profiles != null:
                    PlayerTelemetry.Validate(item.Profiles); Fight.PartyProfiles = [..item.Profiles]; RefreshProfiles(); revision++; break;
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
                        resumeTargets.Remove(dead); phaseAnchors.Remove(dead);
                    }
                    if (item.Network.Kind == "dungeon-result" && item.Network.Fields.TryGetProperty("Cleared", out var cleared) && cleared.ValueKind == System.Text.Json.JsonValueKind.True) {
                        resumable = null; resumeTargets.Clear(); phaseAnchors.Clear(); recoveryBlocked = true;
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
            bool replaced = previous.Server > 0 && actor.Server > 0 && previous.Server != actor.Server || previous.Player && actor.Player && previous.Name != actor.Name ||
                previous.NpcId > 0 && actor.NpcId > 0 && previous.NpcId != actor.NpcId ||
                previous.Player && actor.NpcId > 0 || previous.NpcId > 0 && actor.Player;
            if (replaced)
            {
                resumeTargets.Remove(actor.Id); phaseAnchors.Remove(actor.Id);
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
        Fight.Actors[actor.Id] = actor; RefreshProfiles(); identityEvents++; revision++;
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
            BeginCombat(damage.Timestamp, damage.ActorId, damage.TargetId, healing);
            EnsureActor(damage.ActorId); EnsureActor(damage.TargetId);
            (healing ? Fight.Healing : Fight.Damage).Add(damage with { IsHeal = healing });
            revision++;
        }
    }
    void EnsureActor(int id) { if (id > 0) Fight.Actors.TryAdd(id, new(id, $"Entity {id}", false, false, 0, "Unknown", 0)); }
    public Fight Snapshot()
    {
        lock (Gate) { profiles.Update(Fight, inferClasses: true); return Fight.Copy(); }
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
            profiles.Update(Fight, inferClasses: true);
            var previous = Fight;
            Fight = new() { Label = label, LocalPlayer = previous.LocalPlayer, Actors = previous.Actors.Where(p => p.Value.OriginalEntityId == 0).ToDictionary(), Specs = new(previous.Specs), Energy = previous.Energy, PartyProfiles = [..previous.PartyProfiles], LocalPing = previous.LocalPing, LocalStats = CharacterReadings.Copy(previous.LocalStats),
                Builds = previous.Builds.Where(p => previous.Actors.GetValueOrDefault(p.Key)?.OriginalEntityId == 0).ToDictionary(p => p.Key, p => FightMetadata.Copy(p.Value)), PartyKnown = previous.PartyKnown, PartySource = previous.PartySource };
            lastDeaths.Clear(); lastCombat = null; recoveryBlocked = false; resumable = null; resumeTargets.Clear(); phaseAnchors.Clear();
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
    // The live UI calls this even when no packets arrive. Replay uses event timestamps.
    public void AdvanceTime(long timestamp)
    {
        lock (Gate)
        {
            if (lastCombat == null || timestamp - lastCombat.Value < IdleResetMs || !Fight.HasEvents) return;
            // A healing-only pause may also clear, without losing its contribution
            // or extending the boss recovery window indefinitely.
            if (resumable != null && Fight.Damage.Count == 0 && Fight.Defense.Count == 0) {
                var pending = resumable.Copy();
                pending.Healing.AddRange(Fight.Healing); pending.Deaths.AddRange(Fight.Deaths); pending.Network.AddRange(Fight.Network);
                foreach (var entry in Fight.Actors) pending.Actors[entry.Key] = entry.Value;
                var keptTargets = resumeTargets; var keptAnchors = phaseAnchors; long stopped = resumeStoppedAt;
                completed.Enqueue(pending);
                // NewFight clears these sets, so retain independent copies.
                keptTargets = new(keptTargets); keptAnchors = new(keptAnchors);
                NewFight(Fight.Label);
                resumable = pending; resumeTargets = keptTargets; phaseAnchors = keptAnchors; resumeStoppedAt = stopped;
                return;
            }
            var previous = Snapshot();
            bool Friendly(int id) => id == previous.LocalPlayer || previous.Actors.GetValueOrDefault(id)?.Party == true;
            var targets = previous.Damage.Select(e => (e.ActorId, e.TargetId))
                .Concat(previous.Defense.Select(e => (e.ActorId, e.TargetId)))
                .Where(e => previous.LocalPlayer == 0 || Friendly(e.ActorId) || Friendly(e.TargetId))
                .SelectMany(e => Friendly(e.TargetId) ? new[] { e.ActorId } : new[] { e.TargetId })
                .Where(id => id > 0 && !Friendly(id) && previous.Actors.GetValueOrDefault(id)?.OriginalEntityId == 0)
                .Except(previous.Deaths.Select(d => d.EntityId)).ToHashSet();
            bool blocked = recoveryBlocked; long stoppedAt = lastCombat.Value;
            completed.Enqueue(previous);
            NewFight(previous.Label);
            if (blocked) return;
            resumable = previous; resumeTargets = targets; resumeStoppedAt = stoppedAt;
            // Cast records are NPC activity hints, not an authoritative boss flag.
            // Sustained combat also allows recovery when those hints were not captured.
            bool sustained = previous.Seconds >= 30;
            var hints = previous.Network.Select(e => e.Kind switch {
                "boss-cast" => NetworkEntity(e, "CasterId"), "boss-enrage" => NetworkEntity(e, "EntityId"), _ => 0
            }).Where(id => id > 0).ToHashSet();
            phaseAnchors = targets.Where(id => previous.Actors.GetValueOrDefault(id) is { NpcId: > 0 } &&
                (sustained || hints.Contains(id) || BossCatalogue.Default.IsEncounterBoss(previous.Actors[id].NpcId))).ToHashSet();
        }
    }
    static int NetworkEntity(NetworkEvent e, string key) =>
        e.Fields.ValueKind == System.Text.Json.JsonValueKind.Object && e.Fields.TryGetProperty(key, out var value) &&
        value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out int id) ? id : 0;

    void BeginCombat(long timestamp, int actor, int target, bool healing)
    {
        AdvanceTime(timestamp);
        bool Friendly(int id) => id == Fight.LocalPlayer || Fight.Actors.GetValueOrDefault(id)?.Party == true;
        // Nearby fights must not choose the next encounter for our party.
        if (Fight.LocalPlayer > 0 && !Friendly(actor) && !Friendly(target)) return;
        if (!healing && resumable != null)
        {
            long gap = timestamp - resumeStoppedAt;
            int hostile = Friendly(target) ? actor : target;
            var catalog = BossCatalogue.Default;
            int hostileNpc = Fight.Actors.GetValueOrDefault(hostile)?.NpcId ?? 0;
            var nextBoss = catalog.IsEncounterBoss(hostileNpc) ? catalog.Find(hostileNpc) : null;
            bool differentBoss = nextBoss != null && !phaseAnchors.Any(id => catalog.Find(resumable.Actors.GetValueOrDefault(id)?.NpcId ?? 0)?.Key == nextBoss.Key);
            bool possibleAdd = !differentBoss && phaseAnchors.Count > 0 && gap <= AddPhaseWindowMs &&
                (Friendly(actor) || Friendly(target)) && !Friendly(hostile) &&
                Fight.Actors.GetValueOrDefault(hostile)?.Player != true;
            if (gap <= RecoveryWindowMs && ((!Friendly(actor) && resumeTargets.Contains(actor)) || (!Friendly(target) && resumeTargets.Contains(target)) || possibleAdd))
            {
                var current = Fight;
                Fight = resumable.Copy();
                foreach (var entry in current.Actors) Fight.Actors[entry.Key] = entry.Value;
                Fight.LocalPlayer = current.LocalPlayer; Fight.PartyKnown = current.PartyKnown; Fight.PartySource = current.PartySource;
                Fight.PartyProfiles = current.PartyProfiles; Fight.LocalPing = current.LocalPing; Fight.LocalStats = current.LocalStats;
                Fight.Energy = current.Energy; Fight.Specs = current.Specs; Fight.Label = current.Label;
                foreach (var entry in current.Builds) Fight.Builds[entry.Key] = entry.Value;
                Fight.Damage.AddRange(current.Damage); Fight.Healing.AddRange(current.Healing); Fight.Defense.AddRange(current.Defense);
                Fight.Deaths.AddRange(current.Deaths); Fight.Network.AddRange(current.Network);
                lastDeaths.Clear();
                foreach (var death in Fight.Deaths) lastDeaths[death.EntityId] = Math.Max(lastDeaths.GetValueOrDefault(death.EntityId), death.Timestamp);
                revision++;
            }
            // The next hostile exchange either resumes this encounter or commits a new one.
            resumable = null; resumeTargets.Clear(); phaseAnchors.Clear();
        }
        lastCombat = Math.Max(lastCombat ?? timestamp, timestamp);
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
                lock (Gate) {
                    if (!completed.TryPeek(out fight!)) break;
                    if (fight.Id == Fight.Id) fight = Snapshot();
                    else if (fight.Id == resumable?.Id) fight = resumable.Copy();
                }
                save(fight);
                lock (Gate) completed.Dequeue();
                saved.Add(fight);
            }
            return saved.ToArray();
        }
    }
}
