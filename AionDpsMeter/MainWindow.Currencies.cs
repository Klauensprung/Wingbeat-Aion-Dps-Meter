namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    CurrencyOverlayWindow? currencyOverlay;
    void ToggleCurrencyOverlay()
    {
        if (currencyOverlay != null) { currencyOverlay.Close(); return; }
        var window = new CurrencyOverlayWindow(preferences, SavePreferences); currencyOverlay = window;
        window.Closed += (_, _) => { if (ReferenceEquals(currencyOverlay, window)) currencyOverlay = null; };
        RefreshCooldownVisibility();
    }
    void RefreshCurrencyOverlay(bool allowed)
    {
        if (currencyOverlay == null) return;
        CurrencyBalance[] balances; string? character;
        lock (engine.Gate) {
            balances = engine.CurrentCurrencies();
            character = balances.Length > 0 && engine.Fight.Actors.TryGetValue(engine.Fight.LocalPlayer, out var owner) ? owner.Name : null;
        }
        currencyOverlay.Update(balances, capture != null, character);
        if (allowed && !currencyOverlay.IsVisible) currencyOverlay.Show();
        else if (!allowed && currencyOverlay.IsVisible) currencyOverlay.Hide();
    }
}
