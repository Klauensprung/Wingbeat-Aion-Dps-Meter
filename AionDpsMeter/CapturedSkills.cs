using System.IO;
using System.Text.Json;
using AionDpsMeter.Models;

namespace AionDpsMeter.Local;

public sealed record SkillLevelValue(int Skill, int Level);
public sealed record SkillChoiceValue(int Skill, int[] Choices);
public sealed record SkillChainValue(int Skill, int Parent, long Timestamp);
public sealed record CapturedSkillSnapshot(long LevelsAt, long ChoicesAt, bool LevelsComplete, bool ChoicesComplete,
    SkillLevelValue[] Levels, SkillChoiceValue[] Choices)
{
    public SkillChainValue[] Chains { get; init; } = [];
}
internal sealed record SkillChainReading(int Parent, int[] Skills);
internal sealed record SkillLevelReading(bool Complete, SkillLevelValue[] Skills);
internal static class CapturedSkills
{
    internal static void Validate(CapturedSkillSnapshot s)
    {
        if (s.Chains == null || s.Chains.Length > 256 || s.Chains.Any(x => x == null || x.Skill <= 0 || x.Parent <= 0 || x.Timestamp < 0) || s.Chains.Select(x => x.Skill).Distinct().Count() != s.Chains.Length ||
            s.LevelsAt < -1 || s.ChoicesAt < -1 || s.Levels == null || s.Choices == null || s.Levels.Length > 256 || s.Choices.Length > 256 ||
            s.Levels.Any(x => x == null || x.Skill <= 0 || x.Level is < 0 or > 255) ||
            s.Choices.Any(x => x == null || x.Skill <= 0 || x.Choices == null || x.Choices.Length > 32 || x.Choices.Any(c => c is < 0 or > 255) || x.Choices.Distinct().Count() != x.Choices.Length) ||
            s.Levels.Select(x => x.Skill).Distinct().Count() != s.Levels.Length || s.Choices.Select(x => x.Skill).Distinct().Count() != s.Choices.Length)
            throw new InvalidDataException("Invalid captured skill state.");
    }
    internal static CapturedSkillSnapshot? Apply(CapturedSkillSnapshot? old, NetworkEvent e)
    {
        if (e.Kind is not ("skill-levels" or "skill-choices" or "skill-chain") || e.Timestamp < 0) return old;
        var state = old ?? new(-1, -1, false, false, [], []);
        if (e.Kind == "skill-chain") {
            var value = e.Fields.Deserialize<SkillChainReading>() ?? throw new InvalidDataException("Missing skill chain.");
            if (value.Parent <= 0 || value.Skills == null || value.Skills.Length > 256 || value.Skills.Any(s => s <= 0)) throw new InvalidDataException("Invalid skill chain.");
            var chains = state.Chains.ToDictionary(x => x.Skill);
            foreach (int skill in value.Skills)
                if (!chains.TryGetValue(skill, out var previous) || e.Timestamp >= previous.Timestamp) chains[skill] = new(skill, value.Parent, e.Timestamp);
            state = state with { Chains = chains.Values.OrderBy(x => x.Skill).ToArray() };
            Validate(state); return state;
        }
        if (e.Kind == "skill-levels") {
            if (e.Timestamp < state.LevelsAt) return old;
            var value = e.Fields.Deserialize<SkillLevelReading>() ?? throw new InvalidDataException("Missing skill levels.");
            Validate(new(e.Timestamp, -1, value.Complete, false, value.Skills, []));
            var levels = value.Complete ? new Dictionary<int, SkillLevelValue>() : state.Levels.ToDictionary(x => x.Skill);
            foreach (var skill in value.Skills) levels[skill.Skill] = skill;
            state = state with { LevelsAt = e.Timestamp, LevelsComplete = value.Complete || state.LevelsComplete, Levels = levels.Values.OrderBy(x => x.Skill).ToArray() };
        } else {
            if (e.Timestamp < state.ChoicesAt) return old;
            var choices = e.Fields.GetProperty("Skills").Deserialize<SkillChoiceValue[]>() ?? throw new InvalidDataException("Missing skill choices.");
            Validate(new(-1, e.Timestamp, false, true, [], choices));
            state = state with { ChoicesAt = e.Timestamp, ChoicesComplete = true, Choices = choices.Select(x => x with { Choices = x.Choices.ToArray() }).ToArray() };
        }
        Validate(state); return state;
    }
    internal static ActorBuild? Build(Actor actor)
    {
        if (actor.CapturedSkills is not { } state) return null;
        var levels = state.Levels.ToDictionary(x => x.Skill, x => x.Level);
        var skills = state.ChoicesComplete ? state.Choices.Select(x => new Spec(x.Skill, levels.GetValueOrDefault(x.Skill), x.Choices.ToArray())).ToArray()
            : state.Levels.Select(x => new Spec(x.Skill, x.Level, [])).ToArray();
        var expanded = skills.ToDictionary(x => x.Skill);
        var parents = state.Chains.ToDictionary(x => x.Skill, x => x.Parent);
        foreach (int child in parents.Keys.Order()) {
            int root = child; var visited = new HashSet<int>();
            while (parents.TryGetValue(root, out int parent) && visited.Add(root)) root = parent;
            if (!parents.ContainsKey(root) && !expanded.ContainsKey(child) && expanded.TryGetValue(root, out var source))
                expanded[child] = source with { Skill = child, Choices = source.Choices.ToArray() };
        }
        skills = expanded.Values.OrderBy(x => x.Skill).ToArray();
        return new(actor.Id, skills, state.LevelsComplete && state.ChoicesComplete && skills.Length > 0 && skills.All(s => s.Level > 0), "Captured skill packets");
    }
}
internal static class SkillLoadoutWire
{
    internal static SkillChainReading Chain(ref WireReader r)
    {
        int parent = checked((int)r.U32()); int count = Count(ref r, 256); var skills = new int[count];
        for (int i = 0; i < count; i++) skills[i] = checked((int)r.U32());
        End(ref r);
        if (parent <= 0 || skills.Any(s => s <= 0)) throw new InvalidDataException("Invalid chain skill.");
        return new(parent, skills);
    }
    internal static SkillLevelReading Levels(ref WireReader r, ushort opcode)
    {
        bool full = opcode == 0x0051; int count = opcode == 0x0f38 ? 1 : Count(ref r, 256);
        var skills = new SkillLevelValue[count]; var keys = new HashSet<int>();
        for (int i = 0; i < count; i++) {
            if (opcode == 0x1338 && !keys.Add(checked((int)r.U32()))) throw new InvalidDataException("Duplicate level update key.");
            byte flags = r.Byte(); if ((flags & ~(full ? 15 : 1)) != 0) throw new InvalidDataException("Unknown skill flags.");
            int skill = checked((int)r.U32()); byte level = r.Byte(); r.Byte(); if ((flags & 1) != 0) r.Take(5);
            if (full) { if ((flags & 2) != 0) r.Var(); r.Take(4); if ((flags & 4) != 0) r.Byte(); if ((flags & 8) != 0) r.Var(); }
            if (skill <= 0) throw new InvalidDataException("Invalid learned skill."); skills[i] = new(skill, level);
        }
        if (!full) r.Byte(); End(ref r);
        if (skills.Select(s => s.Skill).Distinct().Count() != skills.Length) throw new InvalidDataException("Duplicate learned skill.");
        return new(full, skills);
    }
    internal static SkillChoiceValue[] Choices(ref WireReader r)
    {
        int count = Count(ref r, 256); var skills = new SkillChoiceValue[count];
        for (int i = 0; i < count; i++) {
            int skill = checked((int)r.U32()); int slots = Count(ref r, 32); var choices = new List<int>(); var seen = new HashSet<byte>();
            for (int j = 0; j < slots; j++) { byte slot = r.Byte(); int part = unchecked((int)r.U32()); if (!seen.Add(slot)) throw new InvalidDataException("Duplicate specialization slot."); if (part > 0) choices.Add(slot); }
            if (skill <= 0) throw new InvalidDataException("Invalid specialized skill."); skills[i] = new(skill, choices.Order().ToArray());
        }
        End(ref r); if (skills.Select(s => s.Skill).Distinct().Count() != count) throw new InvalidDataException("Duplicate specialized skill."); return skills;
    }
    static int Count(ref WireReader r, int max) { int n = r.Var(); if (n > max) throw new InvalidDataException("Skill list exceeds limit."); return n; }
    static void End(ref WireReader r) { if (r.Remaining != 0) throw new InvalidDataException("Unknown skill trailer."); }
}
