using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void OpenTools() => tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Tools");

    FrameworkElement BuildTools()
    {
        var sections = new WrapPanel { Margin = new(8) };
        void Group(string title, string description, params (string Label, UiIcon Icon, Action Open)[] actions)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) });
            var hint = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(hint);
            foreach (var action in actions) {
                var button = IconButton(action.Icon, action.Label, () => {
                    try { action.Open(); }
                    catch (Exception ex) { LocalLog.Write(action.Label + " failed: " + ex); MessageBox.Show(this, ex.Message, "Wingbeat", MessageBoxButton.OK, MessageBoxImage.Error); }
                });
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Margin = new(0, 0, 0, 6); content.Children.Add(button);
            }
            var card = UiTheme.Card(content, new Thickness(16)); card.Width = 270; card.Margin = new(0, 0, 12, 12);
            sections.Children.Add(card);
        }
        Group("Make it yours", "Display, alerts and capture setup.",
            ("Overlay settings", UiIcon.Meter, () => ShowSettings("Overlays")),
            ("Appearance", UiIcon.Palette, () => ShowSettings("Appearance")),
            ("Event and party alerts", UiIcon.Cooldowns, ShowReminderSettings),
            ("Capture and startup", UiIcon.Capture, () => ShowSettings("Capture")));
        Group("Explore your fights", "Inspect a fight or compare your attempts.",
            ("Buffs and effects", UiIcon.Healing, ShowEffects),
            ("Cast outcomes", UiIcon.Cooldowns, ShowCastOutcomes),
            ("Visible equipment", UiIcon.Catalogue, ShowVisibleEquipment),
            ("Equipped gear", UiIcon.Catalogue, ShowEquippedGear),
            ("Daevanion boards", UiIcon.Catalogue, ShowDaevanion),
            ("Wings", UiIcon.Catalogue, ShowWings),
            ("Titles", UiIcon.Catalogue, ShowTitles),
            ("Character collections", UiIcon.Catalogue, ShowCollections),
            ("Dungeon runs", UiIcon.Encounter, ShowObservedRuns),
            ("Compare training", UiIcon.Chart, ShowTrainingComparison),
            ("Copy fight summary", UiIcon.Report, ShowChatSummary));
        Group("Collect and explore", "Find pets, review loot and track bosses.",
            ("Pets and souls", UiIcon.Catalogue, ShowPetCatalogue),
            ("Pet genus bonuses", UiIcon.Catalogue, ShowPetGenus),
            ("Nearby pets", UiIcon.Catalogue, () => ShowNearbyPets(this, viewed)),
            ("Nearby world objects", UiIcon.Catalogue, ShowWorldObjects),
            ("Map events", UiIcon.Catalogue, ShowMapEvents),
            ("Gathering summary", UiIcon.Catalogue, ShowGathering),
            ("Farming tracking", UiIcon.Chart, ShowFarming),
            ("Duty missions", UiIcon.Catalogue, ShowDutyMissions),
            ("Saved characters", UiIcon.Catalogue, ShowAccountCharacters),
            ("Shop purchase counters", UiIcon.Catalogue, ShowShopPurchases),
            ("Black market", UiIcon.Catalogue, ShowBlackMarket),
            ("Appearance presets", UiIcon.Catalogue, ShowAppearancePresets),
            ("Party finder", UiIcon.Catalogue, ShowPartyFinder),
            ("Abyss season", UiIcon.Catalogue, ShowAbyss),
            ("Abyss siege", UiIcon.Catalogue, ShowSiege),
            ("Battle scoreboard", UiIcon.Catalogue, ShowScoreboard),
            ("Weekly tasks", UiIcon.Catalogue, ShowWeeklyPurchases),
            ("Loot history", UiIcon.Folder, () => ShowLoot()),
            ("Field boss timers", UiIcon.Boss, ShowFieldBosses));
        Group("Analysis library", "More ways to understand your performance.", MoreViews.Where(t => t != "Network events").Select(title =>
            (title, UiIcons.ForView(title), (Action)(() => { character.Clear(); tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == title); }))).ToArray());
        Group("Your files", "Save and move fights on this PC.",
            ("Save current fight", UiIcon.Save, () => { SaveFight(Current); ReloadArchive(); Refresh(); }),
            ("Import fights", UiIcon.Folder, Import),
            ("Export current fight", UiIcon.Save, Export),
            ("Local data settings", UiIcon.Settings, () => ShowSettings("Local data")));
        Group("Support", "Updates and troubleshooting.",
            ("Check for updates", UiIcon.Refresh, async () => await CheckForUpdates(true)),
            ("Advanced settings", UiIcon.Settings, () => ShowSettings("Advanced")),
            ("Network events", UiIcon.Network, () => tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Network events")),
            ("Import events", UiIcon.Folder, ImportEvents));
        return new ScrollViewer { Content = sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
