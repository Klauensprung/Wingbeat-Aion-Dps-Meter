using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void OpenTools() => tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "Tools");

    FrameworkElement BuildTools()
    {
        var sections = new WrapPanel { Margin = new(8) };
        var searchable=new List<(Border Card,List<(Button Button,string Text)> Buttons)>();
        void Group(string title, string description, params (string Label, UiIcon Icon, Action Open)[] actions)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = Localization.T(title), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) });
            var hint = new TextBlock { Text = Localization.T(description), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(hint);
            var entries=new List<(Button Button,string Text)>();
            foreach (var action in actions) {
                var button = IconButton(action.Icon, Localization.T(action.Label), () => {
                    try { action.Open(); }
                    catch (Exception ex) { LocalLog.Write(action.Label + " failed: " + ex); MessageBox.Show(this, ex.Message, global::AionDpsMeter.Local.Localization.T("Wingbeat"), MessageBoxButton.OK, MessageBoxImage.Error); }
                });
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Margin = new(0, 0, 0, 6); content.Children.Add(button);
                entries.Add((button,Localization.T(action.Label)+" "+Localization.T(title)));
            }
            var card = UiTheme.Card(content, new Thickness(20)); card.Width = 310; card.Margin = new(0, 0, 16, 16);
            sections.Children.Add(card);
            searchable.Add((card,entries));
        }
        Group("Make it yours", "Display, alerts and capture setup.",
            ("Overlay settings", UiIcon.Meter, () => ShowSettings("Overlays")),
            ("Appearance", UiIcon.Palette, () => ShowSettings("Appearance")),
            ("Timer setup", UiIcon.Cooldowns, ShowReminderSettings),
            ("Capture and startup", UiIcon.Capture, () => ShowSettings("Capture")));
        Group("Explore your fights", "Inspect a fight or compare your attempts.",
            ("Buffs and effects", UiIcon.Healing, ShowEffects),
            ("Cast outcomes", UiIcon.Cooldowns, ShowCastOutcomes),
            ("Dungeon runs", UiIcon.Encounter, ShowObservedRuns),
            ("Compare training", UiIcon.Chart, ShowTrainingComparison),
            ("Copy fight summary", UiIcon.Report, ShowChatSummary));
        Group("Collect and explore", "Gathering routes, farming progress and loot history.",
            ("Pets and souls", UiIcon.Catalogue, ShowPetCatalogue),
            ("Nearby pets", UiIcon.Catalogue, () => ShowNearbyPets(this, viewed)),
            ("Gathering summary", UiIcon.Catalogue, ShowGathering),
            ("Farming tracking", UiIcon.Chart, ShowFarming),
            ("Loot history", UiIcon.Folder, () => ShowLoot()),
            ("Gathering routes", UiIcon.Chart, ShowGatheringRoutes));
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
        var root=new DockPanel();
        var heading=UiSections.Heading("Find your next action","Search tools, settings and detailed analysis.");heading.Margin=new(8,8,8,12);
        var search=new TextBox{Tag="Search tools",MaxLength=100,Margin=new(0,12,0,0)};heading.Children.Add(search);DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var empty=new TextBlock{Text=Localization.T("No matching tools. Try another search."),Margin=new(8),Visibility=Visibility.Collapsed};heading.Children.Add(empty);
        search.TextChanged+=(_,_)=>{
            int matches=0;
            foreach(var group in searchable){int visible=0;foreach(var item in group.Buttons){bool match=item.Text.Contains(search.Text.Trim(),StringComparison.OrdinalIgnoreCase);item.Button.Visibility=match?Visibility.Visible:Visibility.Collapsed;if(match)visible++;}group.Card.Visibility=visible>0?Visibility.Visible:Visibility.Collapsed;matches+=visible;}
            empty.Visibility=matches==0?Visibility.Visible:Visibility.Collapsed;
        };
        root.Children.Add(new ScrollViewer { Content = sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });return root;
    }
}
