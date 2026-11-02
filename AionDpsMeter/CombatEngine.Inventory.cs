namespace AionDpsMeter.Local;
public sealed partial class CombatEngine
{
    internal long PendingInventoryOmissions=>inventoryLoot.OmittedPending;
    internal int PendingInventoryReceipts=>inventoryLoot.PendingReceiptCount;
    internal int PendingTransferStacks=>inventoryLoot.TransferStackCount;
    void RecordInventoryGain(LootPickup pickup)
    {
        string owner=pickup.CapturedCharacter??Fight.Actors.GetValueOrDefault(Fight.LocalPlayer)?.Name??"Captured character";
        Loot.Add(pickup,owner);
        (Fight.LiveLoot??=new()).Add(pickup,owner);
        recoveryLoot?.Add(pickup,owner);
    }
}
