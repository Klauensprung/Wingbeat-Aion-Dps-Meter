using AionDpsMeter.Models;

namespace AionDpsMeter.Local;
internal sealed record PendingInventoryReceipt(long Timestamp,int MapId,string? RunId,LootPickup? Gain,InventoryUse? Use);
internal sealed record PendingInventoryLoss(string RunId,int MapId,decimal Received,decimal Used);

// Compact inventory state awaiting identity is isolated from the previous owner.
internal sealed class InventoryLootSession
{
    InventoryLootTracker current = new();
    InventoryLootTracker? pending;
    InventoryLootTracker? transferCounts;
    sealed class DeferredBuffer {
        internal readonly List<PendingInventoryReceipt> Entries=new();
        internal long OmittedGains;
        internal readonly Dictionary<(string Run,int Map),PendingInventoryLoss> Losses=new();
        internal void Clear(){Entries.Clear();OmittedGains=0;Losses.Clear();}
    }
    readonly DeferredBuffer unassigned=new(),transferred=new();
    internal long OmittedPending {get;private set;}
    internal int PendingReceiptCount=>unassigned.Entries.Count+transferred.Entries.Count;
    internal int TransferStackCount=>transferCounts?.Count??0;
    internal long ConfirmedOmittedGains {get;private set;}
    internal PendingInventoryLoss[] ConfirmedLosses {get;private set;}=[];
    void ClearDeferred(){unassigned.Clear();transferred.Clear();transferCounts=null;}
    void Retain(DeferredBuffer destination,long at,int map,string? run,LootPickup[] gains,InventoryUse[] uses) {
        foreach(var gain in gains)Add(new(at,map,run,gain with {MapId=map},null));
        foreach(var use in uses)Add(new(at,map,run,null,use));
        void Add(PendingInventoryReceipt receipt) {
            if(destination.Entries.Count<4096)destination.Entries.Add(receipt);
            else {
                if(receipt.Gain!=null)destination.OmittedGains++;
                if(receipt.RunId is {} runId&&receipt.MapId>0) {
                    var key=(runId,receipt.MapId);
                    if(destination.Losses.TryGetValue(key,out var loss)||destination.Losses.Count<64) {
                        loss??=new(runId,receipt.MapId,0,0);
                        destination.Losses[key]=loss with {Received=loss.Received+(receipt.Gain?.Quantity??0),Used=loss.Used+(receipt.Use?.Count??0)};
                    }
                }
                if(OmittedPending++==0)LocalLog.Write("Pending inventory accounting limit reached; loot/use totals are partial.");
            }
        }
    }
    long boundary = -1;
    bool loginAwaitingMap;
    bool transferBaseline;
    internal void Login(long timestamp)
    {
        if (timestamp < 0 || timestamp <= boundary) return;
        boundary = timestamp; pending = new(); loginAwaitingMap = true; transferBaseline = false;ClearDeferred();
    }
    internal InventoryUse[] Uses { get; private set; } = [];
    internal int Count => (pending ?? current).Count;
    // Pending map/login inventory belongs to an unconfirmed owner and is not displayed.
    internal CurrencyBalance[] Currencies() => pending != null || !current.HasIdentity ? [] : current.Currencies();
    internal CapturedContentTicket[] Tickets() => pending != null || !current.HasIdentity ? [] : current.Tickets();
    internal InventoryHoldingSnapshot Holdings() => pending != null || !current.HasIdentity ? new(false, []) : current.Holdings();
    internal bool TicketsComplete => pending == null && current.HasIdentity && current.TicketsComplete;
    internal LootEnergyEvent? Energy => pending != null || !current.HasIdentity ? null : current.Energy;
    internal void ApplyEnergy(LootEnergyEvent value, Actor? local)
    {
        if (value.TimestampMs < boundary) return;
        (pending ?? current).ApplyEnergy(value, pending != null ? null : local);
    }
    internal void Prepare(long timestamp)
    {
        if (timestamp < 0 || timestamp <= boundary) return;
        boundary = timestamp;
        if (loginAwaitingMap) { loginAwaitingMap = false; return; }
        transferBaseline = pending == null && current.HasIdentity;
        bool initial=pending==null&&!current.HasIdentity;
        if(!initial)ClearDeferred();
        if(transferBaseline)transferCounts=current.CopyUnassignedCounts();
        // Initial login can send the inventory before map preparation. Preserve
        // only that unassigned baseline, never a previous character's inventory.
        pending = pending == null && !current.HasIdentity ? current : new();
    }
    internal PendingInventoryReceipt[] Bind(Actor local)
    {
        loginAwaitingMap = false;
        bool same=transferBaseline&&current.IsSamePersistentOwner(local);
        var selected=same?transferred:unassigned;
        var receipts=selected.Entries.ToArray();
        ConfirmedOmittedGains=local.Server>0?selected.OmittedGains:0;
        ConfirmedLosses=local.Server>0?selected.Losses.Values.ToArray():[];
        if (pending != null) {
            if (transferBaseline && current.IsSamePersistentOwner(local)) pending.InheritTransferState(current);
            current = pending; pending = null;
        }
        transferBaseline = false;
        current.Bind(local, reconnect: true);
        ClearDeferred();
        return local.Server>0?receipts.Select(r=>r.Gain is {} gain?r with {Gain=current.Attribute(gain,local)}:r).ToArray():[];
    }
    internal LootPickup[] Apply(NetworkEvent message, Actor? local,int map=0,string? run=null)
    {
        Uses = [];
        if (message.Timestamp < boundary) return [];
        var tracker = pending ?? current;
        bool defer=pending!=null||!current.HasIdentity&&local==null;
        var gains = tracker.Apply(message, pending != null ? null : local,defer);
        if(defer) {
            Retain(unassigned,message.Timestamp,map,run,gains,tracker.Uses);
            if(transferCounts!=null) {
                var transferGains=transferCounts.Apply(message,null,true);
                Retain(transferred,message.Timestamp,map,run,transferGains,transferCounts.Uses);
            }
            return [];
        }
        Uses = tracker.Uses;
        return gains;
    }
}
