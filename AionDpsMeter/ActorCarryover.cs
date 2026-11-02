namespace AionDpsMeter.Local;

// This limits identities carried into a fresh fight, never identities referenced
// by the current fight or an already captured snapshot.
internal sealed class ActorCarryover
{
    internal const int Limit = 4096;
    readonly LinkedList<int> recent = new();
    readonly Dictionary<int, LinkedListNode<int>> index = new();
    internal void Clear() { recent.Clear(); index.Clear(); }
    internal void Observe(int id)
    {
        if (id <= 0) return;
        if (index.TryGetValue(id, out var node)) {
            if (node == recent.Last) return;
            recent.Remove(node); recent.AddLast(node); return;
        }
        index[id] = recent.AddLast(id);
        if (index.Count > Limit) { int oldest = recent.First!.Value; recent.RemoveFirst(); index.Remove(oldest); }
    }
    internal Dictionary<int, Actor> Select(Fight fight)
    {
        var selected = fight.Actors.Where(p => p.Value.OriginalEntityId == 0 &&
            (index.ContainsKey(p.Key) || p.Key == fight.LocalPlayer || p.Value.Party || fight.Builds.ContainsKey(p.Key))).ToDictionary();
        foreach (var actor in selected.Values.Where(a => a.OwnerId > 0).ToArray())
            if (!selected.ContainsKey(actor.OwnerId!.Value)) selected[actor.Id] = actor with { OwnerId = 0 };
        return selected;
    }
}
