using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AionDpsMeter.Models;
using PacketDotNet;

namespace AionDpsMeter.Local;

public static class PacketReplay
{
    // Reads classic PCAP directly; replay works without installing or opening Npcap.
    public static IEnumerable<CapturedPayload> Read(string path, int serverPort)
    {
        using var input = File.OpenRead(path);
        if (input.Length > 256L * 1024 * 1024) throw new InvalidDataException("Replay capture exceeds 256 MB.");
        byte[] header = new byte[24]; input.ReadExactly(header);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        bool little = magic is 0xa1b2c3d4 or 0xa1b23c4d, nano = magic is 0xa1b23c4d or 0x4d3cb2a1;
        if (magic is not (0xa1b2c3d4 or 0xd4c3b2a1 or 0xa1b23c4d or 0x4d3cb2a1)) throw new InvalidDataException("Expected classic PCAP, not PCAPNG.");
        uint U32(byte[] bytes, int at) => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at, 4));
        uint link = U32(header, 20);
        if (link != 1) throw new InvalidDataException("Replay currently supports Ethernet PCAPs only.");
        var streams = new Dictionary<string, TcpAssembler>();
        byte[] packetHeader = new byte[16];
        while (input.Position < input.Length)
        {
            input.ReadExactly(packetHeader);
            uint length = U32(packetHeader, 8), original = U32(packetHeader, 12);
            if (length > 1024 * 1024 || length > original || length > input.Length - input.Position) throw new InvalidDataException("Invalid PCAP packet size.");
            if (length != original) throw new InvalidDataException("Capture contains truncated packets; cannot verify decoder completeness.");
            byte[] data = new byte[length]; input.ReadExactly(data);
            var packet = Packet.ParsePacket(LinkLayers.Ethernet, data);
            var tcp = packet.Extract<TcpPacket>(); var ip = packet.Extract<IPPacket>();
            if (tcp == null || ip == null || tcp.SourcePort != serverPort) continue;
            string key = $"{ip.SourceAddress}:{tcp.SourcePort}>{ip.DestinationAddress}:{tcp.DestinationPort}";
            if (tcp.Synchronize) streams.Remove(key);
            if (!streams.TryGetValue(key, out var stream))
            {
                if (streams.Count >= 256) throw new InvalidDataException("Too many replay connections.");
                streams[key] = stream = new();
            }
            long time = U32(packetHeader, 0) * 1000L + U32(packetHeader, 4) / (nano ? 1000000 : 1000);
            if (tcp.PayloadData.Length > 0)
                foreach (var chunk in stream.PushTimed(tcp.SequenceNumber + (tcp.Synchronize ? 1u : 0u), tcp.PayloadData, time))
                    yield return new(tcp.SourcePort, tcp.DestinationPort, chunk.Data, "pcap") { Connection = key, TimestampMs = chunk.TimestampMs };
            if (tcp.Finished || tcp.Reset) streams.Remove(key);
        }
        if (streams.Values.Any(s => s.HasPending)) throw new InvalidDataException("Unresolved TCP gaps remain at end of capture.");
    }

    public static int Run(string[] args)
    {
        string Required(string option) { int at = Array.IndexOf(args, option); return at >= 0 && at + 1 < args.Length ? args[at + 1] : throw new ArgumentException($"Missing {option}"); }
        string output = Path.GetFullPath(Required("--output")); Directory.CreateDirectory(output);
        try
        {
            string path = Path.GetFullPath(Required("--pcap"));
            int portAt = Array.IndexOf(args, "--server-port"), port = portAt >= 0 ? int.Parse(args[portAt + 1]) : 13328;
            if (port is < 1 or > 65535) throw new ArgumentException("Invalid game server port.");
            var decoder = new EuGameDecoder(port); var splitDecoder = new EuGameDecoder(port);
            var hitAudit = args.Contains("--audit-hit-layout") ? new HitWireAudit() : null;
            using var referenceFixtures = args.Contains("--export-reference-fixtures") ? new ReferencePacketFixtures(Path.Combine(output, "reference-fixtures.jsonl")) : null;
            bool auditRewards = args.Contains("--audit-rewards");
            using var nearbyCheckpoints = args.Contains("--export-nearby-checkpoints") ? new StreamWriter(Path.Combine(output,"nearby-checkpoints.jsonl"),false,new UTF8Encoding(false)) : null;
            using var lootCheckpoints = args.Contains("--export-loot-checkpoints") ? new StreamWriter(Path.Combine(output,"loot-checkpoints.jsonl"),false,new UTF8Encoding(false)) : null;
            using var petCheckpoints = args.Contains("--export-pet-checkpoints") ? new StreamWriter(Path.Combine(output,"pet-checkpoints.jsonl"),false,new UTF8Encoding(false)) : null;
            var attributionEngine = args.Contains("--audit-attribution") || nearbyCheckpoints!=null || lootCheckpoints!=null || petCheckpoints!=null ? new CombatEngine() : null;
            long summonDamageHits = 0, summonHealingHits = 0, summonDamage = 0, summonHealing = 0;
            long summonEffects = 0, summonShields = 0;
            var lootTracker = new InventoryLootSession(); Actor? rewardActor = null;
            var inventoryAtIdentity = new List<int>();
            var rewardTotals = new Dictionary<int, long>();
            PetCollectionSnapshot? rewardPets = null;
            using var baselineHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var splitHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var eventsFile = new StreamWriter(Path.Combine(output, "events.jsonl"), false, new UTF8Encoding(false));
            long payloads = 0, bytes = 0, eventCount = 0;
            foreach (var payload in Read(path, port))
            {
                payloads++; bytes += payload.Data.Length;
                hitAudit?.Add(payload);
                referenceFixtures?.Add(payload);
                var result = decoder.Decode(payload);
                foreach (var item in result.Events)
                {
                    if (attributionEngine != null) {
                        attributionEngine.Apply(item);
                        if(nearbyCheckpoints!=null&&(item.Kind is "damage" or "party"||item.Kind=="local-player"||item.Kind=="actor"&&item.Actor?.Player==true||item.Network?.Kind is "account-login" or "local-zone" or "npc-spawn" or "hp" or "entity-death" or "entity-removed" or "world-object" or "world-object-removed"))
                            nearbyCheckpoints.WriteLine(JsonSerializer.Serialize(new {EventIndex=eventCount,Event=item,Expected=attributionEngine.CurrentFarmingNearby(),ExpectedKills=attributionEngine.KillTracking.Snapshot().Counts}));
                        if(lootCheckpoints!=null&&(item.Kind=="local-player"||item.Network?.Kind is "account-login" or "inventory" or "inventory-update"))
                            lootCheckpoints.WriteLine(JsonSerializer.Serialize(new {EventIndex=eventCount,Event=item,Expected=attributionEngine.LootTracking.Snapshot().Counts}));
                        if(petCheckpoints!=null&&(item.Kind=="local-player"||item.Network?.Kind is "account-login" or "pet-roster" or "pet-progress" or "pet-progress-decrease" or "pet-added" or "pet-level"))
                            petCheckpoints.WriteLine(JsonSerializer.Serialize(new {EventIndex=eventCount,Event=item,HasRoster=attributionEngine.Fight.Pets?.HasRoster??false,
                                Pets=attributionEngine.Fight.Pets?.Pets.OrderBy(p=>p.PetId).Select(p=>new {p.PetId,p.Level,p.Souls}).ToArray(),
                                Gains=attributionEngine.SoulTracking.Snapshot().Counts,Registered=attributionEngine.PetRegistrationTracking.Snapshot().Counts,LevelUps=attributionEngine.PetLevelTracking.Snapshot().Counts}));
                        if (item.Network is { Kind: "barrier" or "abnormal-apply" or "abnormal-refresh" } support &&
                            attributionEngine.Fight.Network.LastOrDefault() is { } captured &&
                            SupportAttribution.Captured(captured) is > 0 and var attributed &&
                            support.Fields.TryGetProperty(support.Kind == "barrier" ? "ActorId" : "CasterId", out var caster) &&
                            caster.TryGetInt32(out int source) && source > 0 && source != attributed) {
                            if (support.Kind == "barrier") summonShields++; else summonEffects++;
                        }
                        if (item.Kind is "damage" or "heal") {
                            bool heal = item.Kind == "heal";
                            var hit = (heal ? attributionEngine.Fight.Healing : attributionEngine.Fight.Damage).LastOrDefault();
                            if (hit is { SourceActorId: > 0 }) {
                                if (heal) { summonHealingHits++; summonHealing += hit.Damage; }
                                else { summonDamageHits++; summonDamage += hit.Damage; }
                            }
                        }
                    }
                    if (auditRewards) {
                        if (item.Kind == "local-player") {
                            rewardActor = item.Actor;
                            if (rewardActor != null) foreach(var receipt in lootTracker.Bind(rewardActor))
                                if(receipt.Gain is {} deferredGain)rewardTotals[deferredGain.ItemId]=checked(rewardTotals.GetValueOrDefault(deferredGain.ItemId)+deferredGain.Quantity);
                            inventoryAtIdentity.Add(lootTracker.Count); rewardPets = null;
                        }
                        if (item.Network is { } network) {
                            if (network.Kind == "local-zone" && network.Fields.TryGetProperty("AwaitingIdentity", out var awaiting) && awaiting.ValueKind == JsonValueKind.True)
                                lootTracker.Prepare(network.Timestamp);
                            foreach (var gain in lootTracker.Apply(network, rewardActor))
                                rewardTotals[gain.ItemId] = checked(rewardTotals.GetValueOrDefault(gain.ItemId) + gain.Quantity);
                            rewardPets = PetCollection.Apply(rewardPets, network, rewardActor?.Id ?? 0);
                        }
                    }
                    string line = JsonSerializer.Serialize(item); eventsFile.WriteLine(line); baselineHash.AppendData(Encoding.UTF8.GetBytes(line + "\n")); eventCount++;
                }
                // Deliberately split length prefixes, opcodes and compressed blocks across calls.
                for (int at = 0; at < payload.Data.Length;)
                {
                    int count = Math.Min(1 + (at % 17), payload.Data.Length - at);
                    var fragment = payload with { Data = payload.Data.AsSpan(at, count).ToArray() };
                    foreach (var item in splitDecoder.Decode(fragment).Events)
                        splitHash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item) + "\n"));
                    at += count;
                }
            }
            eventsFile.Flush();
            bool sameEvents = baselineHash.GetHashAndReset().SequenceEqual(splitHash.GetHashAndReset());
            bool sameMetrics = decoder.Records == splitDecoder.Records && decoder.RejectedRecords == splitDecoder.RejectedRecords && decoder.UnknownRecords == splitDecoder.UnknownRecords && decoder.FramingErrors == splitDecoder.FramingErrors && decoder.PendingBytes == splitDecoder.PendingBytes;
            var finalCombat = attributionEngine?.Snapshot();
            var storedCombat = finalCombat?.ForStorage();
            var report = new {
                Capture = Path.GetFileName(path), CaptureSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                ServerPort = port, Payloads = payloads, PayloadBytes = bytes, Events = eventCount,
                decoder.Records, decoder.Bundles, decoder.UnknownRecords, decoder.RejectedRecords, decoder.FramingErrors, decoder.PendingBytes,
                FragmentationInvariant = sameEvents && sameMetrics, decoder.Opcodes, decoder.EventKinds, decoder.Rejections,
                FullEventParityVerified = false,
                RetentionAudit = finalCombat == null ? null : new {
                    FinalFightId = finalCombat.Id,
                    LiveKinds = finalCombat.Network.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count()),
                    StoredKinds = storedCombat!.Network.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count()),
                    LiveNetworkJsonBytes = JsonSerializer.SerializeToUtf8Bytes(finalCombat.Network).Length,
                    StoredNetworkJsonBytes = JsonSerializer.SerializeToUtf8Bytes(storedCombat.Network).Length,
                    Limitation = "Offline final-fight retained network evidence only; serialized byte sizes are not process RAM, and prior completed fights are excluded."
                },
                StaggerBreakAudit = finalCombat == null ? null : new {
                    FinalFightId = finalCombat.Id, finalCombat.HasEvents,
                    Breaks = finalCombat.StaggerBreaks ?? [], finalCombat.StaggerBreaksTruncated,
                    Limitation = "Production final-fight snapshot only; prior completed fights and reference host lifecycle are not compared."
                },
                HitLayoutAudit = hitAudit?.Report(),
                EngineLootAudit = attributionEngine == null ? null : new {
                    CurrentFight = LootLedger.FromFight(attributionEngine.Fight).Summary(),
                    SessionHistory = attributionEngine.Loot.Summary(),
                    RawPickupEvents = attributionEngine.Fight.Network.Count(e => e.Kind == "loot-pickup"),
                    PendingInventoryOmissions = attributionEngine.PendingInventoryOmissions,
                    Limitation = "Production loot summaries; current fight excludes earlier encounters. Not an independent live identity or on-screen quantity comparison."
                },
                EngineCastAudit = attributionEngine == null ? null : new {
                    Summary = CastOutcomes.Build(attributionEngine.Fight),
                    RawCastEvents = attributionEngine.Fight.Network.Count(e => e.Kind is "cast-start" or "charge-start" or "cast-stop" or "charge-stop"),
                    Limitation = "Current production fight only; prior encounters and resets are excluded. Not an independent boss-eligibility or live UI comparison."
                },
                EngineDutyMissionAudit = attributionEngine == null ? null : new {
                    Readings = attributionEngine.DutyMissions.Snapshot(),
                    Limitation = "Production server counters; expired reset timestamps are ignored, following the reference checklist guard. Not an on-screen comparison."
                },
                EngineGearAudit = attributionEngine == null ? null : new {
                    Complete = attributionEngine.Fight.Actors.GetValueOrDefault(attributionEngine.Fight.LocalPlayer)?.Gear?.Complete ?? false,
                    EquippedItems = attributionEngine.Fight.Actors.GetValueOrDefault(attributionEngine.Fight.LocalPlayer)?.Gear?.Items.Length ?? 0,
                    Limitation = "Production equipped-instance state replay, not an independent stateful or on-screen gear comparison."
                },
                FarmingTrackingAudit = attributionEngine == null ? null : new {
                    Kills = attributionEngine.KillTracking.Snapshot(),
                    Gathering = attributionEngine.GatheringTracking.Snapshot(),
                    Loot = attributionEngine.LootTracking.Snapshot(),
                    Souls = attributionEngine.SoulTracking.Snapshot(),
                    Registrations = attributionEngine.PetRegistrationTracking.Snapshot(),
                    LevelUps = attributionEngine.PetLevelTracking.Snapshot(),
                    Nearby = attributionEngine.CurrentFarmingNearby(),
                    Limitation = "Production replay counters, not an independent live or on-screen oracle. Tracking starts empty for this replay; first unknown inventory counts are not gains."
                },
                EnginePetAudit = attributionEngine == null ? null : new {
                    HasRoster = attributionEngine.Fight.Pets?.HasRoster ?? false,
                    OwnedPets = attributionEngine.Fight.Pets?.Pets.Count(p => p.Level > 0) ?? 0,
                    Pets = attributionEngine.Fight.Pets?.Pets.Select(p => new { p.PetId, p.Level, p.Souls, p.ObservedGains }).ToArray(),
                    Limitation = "Production engine including identity transitions; not an independent reference or on-screen comparison."
                },
                LocalSkillAudit = attributionEngine == null ? null : new {
                    LearnedSkills = attributionEngine.Fight.Actors.GetValueOrDefault(attributionEngine.Fight.LocalPlayer)?.CapturedSkills?.Levels.Length ?? 0,
                    SpecializedSkills = attributionEngine.Fight.Actors.GetValueOrDefault(attributionEngine.Fight.LocalPlayer)?.CapturedSkills?.Choices.Length ?? 0,
                    Complete = FightMetadata.BuildFor(attributionEngine.Fight, attributionEngine.Fight.LocalPlayer)?.Complete ?? false,
                    Limitation = "Production state replay, not an independent on-screen build comparison."
                },
                SummonAttributionAudit = attributionEngine == null ? null : new {
                    DamageHits = summonDamageHits, Damage = summonDamage, HealingHits = summonHealingHits, Healing = summonHealing,
                    EffectUpdates = summonEffects, ShieldUpdates = summonShields,
                    Limitation = "Production engine replay, not an independent owner or on-screen totals oracle. Ownership observed after a hit does not retroactively attribute that hit."
                },
                RewardAudit = !auditRewards ? null : new {
                    InventoryStacksAtIdentity = inventoryAtIdentity,
                    Items = rewardTotals.OrderBy(p => p.Key).Select(p => new { ItemId = p.Key, Name = ItemNames.Default.Find(p.Key), Quantity = p.Value }).ToArray(),
                    Pets = rewardPets?.Pets.Select(p => new { p.PetId, p.Souls, p.ObservedGains }).ToArray(),
                    HasPetRoster = rewardPets?.HasRoster ?? false,
                    Limitation = "First unknown stack update establishes a baseline, not a gain. Pet values here use a standalone tracker without engine identity-transition handling; use EnginePetAudit for production session behavior. This is not an independent on-screen oracle."
                },
                Limitations = "Experimental subset only. No reference-oracle or on-screen totals comparison. Player identities and NPC spawn template/name bindings are supported subsets; full lifecycle, DoT, roster and gear remain incomplete. NPC names require a local catalog and observed spawn. Supported direct recovery and periodic ticks are classified locally; effective healing, potions and all skill variants remain unverified. No raw captures or event dumps belong in a public source release."
            };
            File.WriteAllText(Path.Combine(output, "replay-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return payloads > 0 && sameEvents && sameMetrics && decoder.FramingErrors == 0 && decoder.PendingBytes == 0 ? (args.Contains("--require-parity") ? 2 : 0) : 2;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "replay-error.txt"), ex.ToString()); return 1; }
    }
}
