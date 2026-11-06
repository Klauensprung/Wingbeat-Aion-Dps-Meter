namespace AionDpsMeter.Local;

internal static class CooldownVisibility
{
    internal static bool ForegroundAllows(bool gameOnly, bool allowedForeground) => !gameOnly || allowedForeground;
    internal static bool ShouldShow(bool requested, bool combatOnly, bool capturing, bool combat, bool training) =>
        requested && (!combatOnly || capturing && (combat || training));
}

public sealed partial class MainWindow
{
    bool cooldownWanted, meterWanted;
    bool overlaysHidden;
    readonly GameMenuVisibility gameMenuVisibility = new();
    long menuScene = -1;
    string? lastMenuTrace;
    bool HideForGameMenu()
    {
        if(demoMode||!preferences.HideOverlaysInGameMenus){gameMenuVisibility.Reset();return false;}
        bool classic;
        lock(engine.Gate){
            if(menuScene!=engine.LiveSceneRevision){menuScene=engine.LiveSceneRevision;gameMenuVisibility.Reset();}
            classic=engine.SessionSignals.ControlMode=="classic";
        }
        bool game=GameForeground.IsGameFocused();
        var cursor=game?GameCursorState.Read():default;
        bool hovered=game&&cursor.Visible&&OverlayHideControls.PointerOverOverlay();
        bool hidden=gameMenuVisibility.Observe(Environment.TickCount64,game,cursor.Valid,cursor.Visible,cursor.Alt,cursor.MouseDown,classic,hovered);
        string trace=$"game={game} cursor={cursor.Visible} valid={cursor.Valid} asyncAlt={cursor.Alt} hover={hovered} armed={gameMenuVisibility.Armed} classic={classic} menuHidden={hidden} manualHidden={overlaysHidden}";
        if(trace!=lastMenuTrace){lastMenuTrace=trace;OverlayInputTrace.Write(trace);}
        return hidden;
    }
    OverlayHotkey? overlayHotkey;
    System.Windows.Controls.TextBlock? overlayHotkeyStatus;
    void ToggleOverlayVisibility(){overlaysHidden=!overlaysHidden;RefreshCooldownVisibility();}
    void InitializeOverlayHotkey()
    {
        if(demoMode)return;
        overlayHotkey ??= new(this,ToggleOverlayVisibility);
        ApplyOverlayHotkey();
    }
    void ApplyOverlayHotkey()
    {
        string? error=overlayHotkey?.Register(preferences.OverlayHideKey,preferences.OverlayHideModifiers);
        if(overlayHotkeyStatus!=null)overlayHotkeyStatus.Text=error ?? global::AionDpsMeter.Local.Localization.T("Hide/show all overlays: ")+OverlayHotkey.Label(preferences.OverlayHideKey,preferences.OverlayHideModifiers);
        if(error!=null)LocalLog.Write(error);
    }
    void RefreshCooldownVisibility()
    {
        bool menuHidden=HideForGameMenu();
        bool suppressOverlays=overlaysHidden||menuHidden;
        bool foregroundAllowed=demoMode||(!(preferences.OverlaysGameOnly||refreshGatheringMapVisibility!=null||preferences.BossAlertsEnabled))||GameForeground.AllowsOverlays();
        refreshGatheringMapVisibility?.Invoke(foregroundAllowed&&!suppressOverlays);
        RefreshBossAlerts(foregroundAllowed&&!suppressOverlays&&OverlayHideControls.Allows(bossAlertOverlay));
        bool allowed = !suppressOverlays && CooldownVisibility.ForegroundAllows(preferences.OverlaysGameOnly,foregroundAllowed);
        RefreshNearbyEnemies(allowed&&OverlayHideControls.Allows(nearbyEnemiesOverlay));
        RefreshPingOverlay(allowed&&OverlayHideControls.Allows(pingOverlay));
        RefreshLootOverlay(allowed&&OverlayHideControls.Allows(lootOverlay));
        RefreshFarmingOverlay(allowed&&OverlayHideControls.Allows(farmingOverlay));
        RefreshCurrencyOverlay(allowed&&OverlayHideControls.Allows(currencyOverlay));
        RefreshTicketOverlay(allowed&&OverlayHideControls.Allows(ticketOverlay));
        RefreshPetTracker(allowed);
        RefreshControlOverlay(allowed&&OverlayHideControls.Allows(controlOverlay));
        UpdateToolbarState();
        bool active=demoMode||engine.OverlayActive(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if(overlay!=null)OverlayMotion.Number(overlay,System.Windows.UIElement.OpacityProperty,active||overlay.IsMouseOver?1:.4);
        if(secondMeter!=null)OverlayMotion.Number(secondMeter,System.Windows.UIElement.OpacityProperty,active||secondMeter.IsMouseOver?1:.4);
        if (overlay != null) {
            bool meterAllowed=allowed&&OverlayHideControls.Allows(overlay);
            if (meterWanted && meterAllowed && !overlay.IsVisible) overlay.Show();
            else if ((!meterWanted || !meterAllowed) && overlay.IsVisible) overlay.Hide();
        }
        if(secondMeter!=null){
            SyncSecondMeterAppearance();
            bool secondAllowed=allowed&&OverlayHideControls.Allows(secondMeter);
            if(SecondMeterOptions.Enabled&&secondAllowed&&!secondMeter.IsVisible)secondMeter.Show();
            else if((!SecondMeterOptions.Enabled||!secondAllowed)&&secondMeter.IsVisible)secondMeter.Hide();
        }
        if (cooldownOverlay == null) return;
        bool show = CooldownVisibility.ShouldShow(cooldownWanted, preferences.CooldownCombatOnly, capture != null,
            engine.HasRecentPartyCombat(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), engine.TrainingActive) && allowed && OverlayHideControls.Allows(cooldownOverlay);
        if (show && !cooldownOverlay.IsVisible) cooldownOverlay.Show();
        else if (!show && cooldownOverlay.IsVisible) cooldownOverlay.Hide();
    }
}
