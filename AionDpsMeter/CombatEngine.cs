using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed class CombatEngine
{
    public readonly object Gate = new();
    readonly IGamePacketDecoder decoder;
    readonly Queue<Fight> completed = new();
    readonly object saveGate = new();
    long lastEvent;
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
                    // The first or a changed local identity starts a scene/session.
                    // Repeated records for the same character are updates, and
                    // must not discard other players' names or split their fight.
                    if (Fight.LocalPlayer != local.Id || !Fight.Actors.TryGetValue(local.Id, out var knownLocal) ||
                        knownLocal.Name != local.Name || knownLocal.Server != local.Server)
                    {
                        if (Fight.HasRecordedData) completed.Enqueue(Snapshot());
                        NewFight(Fight.Label);
                        Fight.Actors.Clear(); Fight.Specs.Clear(); Fight.Builds.Clear(); Fight.PartyKnown = false; Fight.PartySource = "Not captured"; Fight.LocalPlayer = local.Id;
                    }
                    UpdateActor(local); break;
                case "damage" when item.Damage != null: RecordDamage(item.Damage, false); break;
                case "heal" when item.Damage != null: RecordDamage(item.Damage, true); break;
                case "defense" when item.Defense != null:
                    BeginEvent(item.Defense.Timestamp); EnsureActor(item.Defense.ActorId); EnsureActor(item.Defense.TargetId); Fight.Defense.Add(item.Defense); revision++; break;
                case "energy" when item.Energy != null: Fight.Energy = item.Energy; revision++; break;
                case "party" when item.Party != null: FightMetadata.SetRoster(Fight, item.Party); revision++; break;
                case "build" when item.Build != null: FightMetadata.SetBuild(Fight, item.Build); revision++; break;
                case "network" when item.Network != null:
                    if (Fight.Network.Count >= 250000) { completed.Enqueue(Snapshot()); NewFight(Fight.Label); }
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
                if (Fight.HasRecordedData) { completed.Enqueue(Snapshot()); NewFight(Fight.Label); }
                Fight.Builds.Remove(actor.Id);
            }
            else
                actor = actor with { Player = actor.Player || previous.Player, Party = actor.Party || previous.Party,
                    Cp = actor.Cp > 0 ? actor.Cp : previous.Cp, Job = actor.Job == "Unknown" ? previous.Job : actor.Job,
                    Server = actor.Server > 0 ? actor.Server : previous.Server, NpcId = actor.NpcId > 0 ? actor.NpcId : previous.NpcId,
                    ProfileSource = actor.Cp == 0 && actor.Job == "Unknown" ? previous.ProfileSource : actor.ProfileSource };
        }
        Fight.Actors[actor.Id] = actor; identityEvents++; revision++;
    }
    internal void RecordDamage(DamageEvent damage, bool healing)
    {
        if (damage.Timestamp < 0 || damage.Damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
        lock (Gate)
        {
            BeginEvent(damage.Timestamp); EnsureActor(damage.ActorId); EnsureActor(damage.TargetId);
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
            Fight = new() { Label = label, LocalPlayer = previous.LocalPlayer, Actors = new(previous.Actors), Specs = new(previous.Specs), Energy = previous.Energy,
                Builds = previous.Builds.ToDictionary(p => p.Key, p => FightMetadata.Copy(p.Value)), PartyKnown = previous.PartyKnown, PartySource = previous.PartySource };
            lastEvent = 0;
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
    void BeginEvent(long timestamp)
    {
        if (Fight.HasEvents && ((lastEvent > 0 && timestamp - lastEvent > 15000) || Fight.Damage.Count + Fight.Healing.Count + Fight.Defense.Count >= 250000))
        {
            completed.Enqueue(Snapshot()); NewFight(Fight.Label);
        }
        lastEvent = Math.Max(lastEvent, timestamp);
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
