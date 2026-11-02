namespace AionDpsMeter.Local;

internal sealed class FarmingPetNotices
{
    Dictionary<int,long> registered=[],levels=[];
    (long Session,long Registered,long Levels)? boundary;
    internal FarmingNotice[] Update(FarmingCounterSnapshot registrations,FarmingCounterSnapshot upgrades,
        PetCollectionSnapshot? collection,long? session,Func<int,string?> name)
    {
        var next=session.HasValue?(session.Value,registrations.Since,upgrades.Since):((long,long,long)?)null;
        var output=new List<FarmingNotice>();
        if(next.HasValue&&next==boundary) {
            int gained=0;
            foreach(var pair in registrations.Counts.OrderBy(p=>p.Key))
                if(pair.Value>registered.GetValueOrDefault(pair.Key)) {
                    gained++;
                    if(name(pair.Key) is {} label)output.Add(new("registered",$"pet:{pair.Key}",label,null,null));
                }
            var roster=collection?.Pets.ToDictionary(p=>p.PetId);
            foreach(var pair in upgrades.Counts.OrderBy(p=>p.Key))
                if(pair.Value>levels.GetValueOrDefault(pair.Key)&&name(pair.Key) is {} label)
                    output.Add(new("levelup",$"pet:{pair.Key}",label,roster?.GetValueOrDefault(pair.Key)?.Level??1,null));
            int owned=collection?.Pets.Count(p=>p.Level>=1)??0;
            int milestone=gained>0?Milestones.FirstOrDefault(n=>n>owned-gained&&n<=owned):0;
            if(milestone>0)output.Add(new("milestone",$"collection:{milestone}",$"{milestone} pets collected",milestone,null));
        }
        // Ledger snapshots are bounded; copy so later caller mutation cannot change the baseline.
        registered=new(registrations.Counts);levels=new(upgrades.Counts);boundary=next;
        return output.ToArray();
    }
    static readonly int[] Milestones=[5,10,20,30,40,..Enumerable.Range(5,46).Select(n=>n*10)];
}
