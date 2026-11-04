using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void CheckNewFeatureWindows()
    {
        var original = viewed; var originalPreferences = preferences;
        var results = new List<string>();
        var opened = new List<Window>();
        try {
            preferences = new() { ReminderWindowsNotifications = false };
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sample = FeatureTests.Sample(); sample.Label = "Synthetic feature preview";
            sample.Actors[sample.LocalPlayer] = sample.Actors[sample.LocalPlayer] with { CharacterId = 30, Server = 1 };
            sample.Pets = new(now, 1002, true, [new(1002, 1, 4, 2), new(1005, 2, 12, 3) { LevelInferred = true }]);
            var location = PetSpawns.Default.ForPet(1002)[0].Spawns[0];
            sample.LocalPosition = new(1010, location.X, location.Y, location.Z, now);
            void Network(string kind, object fields) => sample.Network.Add(new(kind, now, "synthetic", JsonSerializer.SerializeToElement(fields)));
            Network("loot-pickup", new LootPickup(now, 1, 110120003, 2, 30, 1, 1010));
            Network("loot-pickup", new LootPickup(now, 2, 534660001, 5, 30, 1, 1010));
            Network("field-boss-list", new FieldBossList(1000, [new(1, false, null, null, null, now + 300000), new(2, true, 10, 20, 30, now)]));
            sample.Network.Add(new("abnormal-apply", sample.Start, "synthetic", JsonSerializer.SerializeToElement(new {
                TargetId = sample.LocalPlayer, CasterId = sample.LocalPlayer, Slot = 1, AbnormalId = 1000, DurationMs = 30000, SourceSkillId = 17010001
            })));
            viewed = sample.ForStorage();
            viewed.Actors[sample.LocalPlayer] = viewed.Actors[sample.LocalPlayer] with { VisibleEquipment = new(now, true,
                [new VisibleItem(1, 110220003, 110230033, 5, true) { Dyes = [new(1, 255, 128, 0)] }, new(2, 110220014, 0, 3, false)]) };
            viewed.SavedShields = [new(sample.LocalPlayer, sample.LocalPlayer, 17010001, 1000, 12000, 8500, 4)];
            void WindowTest(Action show, string name, Action<Window>? inspect = null) {
                var before = Application.Current.Windows.Cast<Window>().ToHashSet(); show();
                var window = Application.Current.Windows.Cast<Window>().Single(w => !before.Contains(w)); opened.Add(window);
                try {
                    window.UpdateLayout(); Render(window, "test-artifacts/Feature-" + name + ".png"); inspect?.Invoke(window); window.UpdateLayout();
                    if (window.ResizeMode != ResizeMode.NoResize) {
                        window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
                        foreach (var table in LayoutVisuals<LiveTable>(window))
                            if (table.ActualHeight < 90) throw new InvalidOperationException(name + ": minimum size hides a complete table row.");
                        Render(window, "test-artifacts/Feature-" + name + "-small.png");
                    }
                    results.Add("PASS: " + name + " opens, renders and keeps its table usable at minimum size.");
                } finally { window.Close(); }
            }
            WindowTest(() => {
                var window = new TicketOverlayWindow(preferences);
                window.Update([new(60000001, 100, 40, 0, 0, now), new(3, 0, 0, 18227000, 25200000, now), new(1, 11, 6, 0, 0, now)], true, true, "Example character", 2301, now, new(true, [new(503700031, 5), new(503700032, 4)])); window.Show();
            }, "Tickets", window => {
                var tickets = (TicketOverlayWindow)window;
                var rows = LayoutVisuals<StackPanel>(window).Single(p => p.Name == "TicketRows");
                if (!LayoutVisuals<TextBlock>(window).Any(t => t.Text.Contains("Bag: 240"))) throw new InvalidOperationException("Ticket consumables missing from bag total.");
                if (!window.Topmost || rows.Children.Count != 3) throw new InvalidOperationException("Ticket rows or topmost behavior missing.");
                var member = LayoutVisuals<CheckBox>(window).Single(); member.IsChecked = true;
                member.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (!preferences.TicketMember || !LayoutVisuals<TextBlock>(window).Any(t => t.Text.Contains("100 / 840"))) throw new InvalidOperationException("Ticket membership change not applied.");
                var search = LayoutVisuals<TextBox>(window).Single(); search.Text = "Abyss";
                if (rows.Children.Count != 1) throw new InvalidOperationException("Ticket search does not filter rows.");
                search.Text = "";
                var chooseTickets = LayoutVisuals<Button>(window).Single(b => b.Content?.ToString()?.StartsWith("Choose tickets") == true);
                chooseTickets.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var ticketMenu = chooseTickets.ContextMenu!;
                var hideTicket = ticketMenu.Items.OfType<MenuItem>().Single(m => m.Tag is int id && id == 1);
                hideTicket.IsChecked = false; hideTicket.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var missingTicket = ticketMenu.Items.OfType<MenuItem>().Single(m => m.Tag is int id && id == 4);
                missingTicket.IsChecked = true; missingTicket.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); ticketMenu.IsOpen = false;
                if (preferences.TicketShown?.Contains(1) != false || preferences.TicketShown?.Contains(4) != true || rows.Children.Count != 3 ||
                    !LayoutVisuals<TextBlock>(window).Any(t => t.Text == "Not captured")) throw new InvalidOperationException("Ticket selection loses preference or invents missing counts.");
                var restoredTicketPreferences = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!;
                if (!restoredTicketPreferences.TicketShown!.SequenceEqual(preferences.TicketShown!)) throw new InvalidOperationException("Ticket selection does not survive preference serialization.");
                chooseTickets.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                ticketMenu = chooseTickets.ContextMenu!; ticketMenu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); ticketMenu.IsOpen = false;
                if (preferences.TicketShown != null || rows.Children.Count != 3) throw new InvalidOperationException("Show all captured tickets failed.");
                search.Text = ""; tickets.Update([], false, false, null, 0, now);
                if (rows.Children.Count != 0 || !LayoutVisuals<TextBlock>(window).Any(t => t.Text.Contains("No tickets captured"))) throw new InvalidOperationException("Ticket owner transition retains previous rows.");
                tickets.Update([new(60000001, 100, 40, 0, 0, now), new(3, 0, 0, 18227000, 25200000, now)], true, false, "Example character", 2301, now, new(true, [new(503700031, 5), new(503700032, 4)]));
            });
            WindowTest(() => {
                var window = new CurrencyOverlayWindow(preferences);
                window.Update([new(930100001, 0), new(930100003, 48720), new(930100031, 5000000000)], true, "Example character"); window.Show();
            }, "Currencies", window => {
                var currencies = (CurrencyOverlayWindow)window;
                var table = LayoutVisuals<StackPanel>(window).Single(p => p.Name == "CurrencyRows");
                if (table.Children.Count != 3 || !window.Topmost) throw new InvalidOperationException("Currency balances or topmost behavior missing.");
                var choose = LayoutVisuals<Button>(window).Single(b => b.Content?.ToString()?.StartsWith("Choose currencies") == true);
                choose.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var menu = choose.ContextMenu!;
                var hide = menu.Items.OfType<MenuItem>().Single(m => m.Tag is int id && id == 930100031);
                hide.IsChecked = false; hide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
                if (table.Children.Count != 2 || preferences.CurrencyShown?.Contains(930100031) != false)
                    throw new InvalidOperationException("Currency selection does not filter or persist the chosen IDs.");
                choose.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                menu = choose.ContextMenu!; menu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
                if (table.Children.Count != 3 || preferences.CurrencyShown != null) throw new InvalidOperationException("Show all currencies cannot restore hidden balances.");
                currencies.Update([], false, null);
                if (table.Children.Count != 0 || !LayoutVisuals<TextBlock>(window).Any(t => t.Text.Contains("unknown balances are not zero")))
                    throw new InvalidOperationException("Currency transition retains old-owner balances or hides unknown state.");
                currencies.Update([new(930100001, 0), new(930100003, 48720), new(930100031, 5000000000)], false, "Example character");
                if (table.Children.Count != 3 || !LayoutVisuals<TextBlock>(window).Any(t => t.Text.Contains("Capture stopped")))
                    throw new InvalidOperationException("Stopped currency capture is not labeled.");
            });
            WindowTest(() => {
                var window = new CurrencyOverlayWindow(preferences); window.Update([], true, null); window.Show();
            }, "Currencies-empty");
            WindowTest(() => ShowLoot(), "Loot", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count != 2) throw new InvalidOperationException("Loot view lost synthetic item totals.");
                var search = LayoutVisuals<TextBox>(window).Single(t => t.ToolTip is string help && help.StartsWith("Search item,")); search.Text = "Ludra";
                if (table.Items.Count != 1) throw new InvalidOperationException("Loot name filtering failed."); search.Clear();
            });
            viewed.SavedCasts = new([new(viewed.LocalPlayer, 17010000, "cast", 10, 2, 3, 1), new(viewed.LocalPlayer, 17020000, "charge", 3, 0, 1, 0)], 0);
            WindowTest(ShowCastOutcomes, "Cast-outcomes", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count != 2 || table.Columns.All(c => c.Header?.ToString() != "No stop recorded")) throw new InvalidOperationException("Cast summary rows or outcome distinction missing.");
                var search = LayoutVisuals<TextBox>(window).Single(); search.Text = "17020000";
                if (table.Items.Count != 1) throw new InvalidOperationException("Cast outcome filtering lost skill identity."); search.Clear();
            });
            viewed.SavedCasts = new([], 0);
            WindowTest(ShowCastOutcomes, "Cast-outcomes-empty", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 0) throw new InvalidOperationException("Empty cast summary invented attempts.");
            });
            viewed.SavedCasts = null;
            WindowTest(ShowPetCatalogue, "Pets", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count < 200) throw new InvalidOperationException("Pet catalogue did not populate.");
                if (!table.Items.OfType<PetCollectionRow>().Any(r => r.Id == 1005 && r.LevelSource == "Inferred") ||
                    !table.Columns.Any(c => c.Header?.ToString() == "Level source")) throw new InvalidOperationException("Pet level provenance is missing.");
            });
            viewed.Pets = viewed.Pets! with { Genera = [new(2, 3, 0, [new(1, 2, 19, 100, true, false), new(2, 2, 146, 1035, false, true)]), new(3, 1, 0, [])] };
            WindowTest(ShowPetGenus, "Pet-genus", window => {
                var genusRows = LayoutVisuals<LiveTable>(window).Single().Items.OfType<GenusRow>().ToArray();
                if (genusRows.Length != 3 || !genusRows.Any(r => r.Stat == "Multi-hit Chance (146)" && r.Value == "10.3%" && r.RawValue == "Raw value: 1035"))
                    throw new InvalidOperationException("Pet genus view lost formatted values, raw tooltip or an empty genus.");
            });
            var genusEngine = engine; var genusViewed = viewed;
            try {
                engine = new CombatEngine(); viewed = null;
                engine.Apply(new("local-player", Actor: new(1, "Live genus owner", true, true, 0, "Cleric", 2301)));
                void GenusEvent(string kind, long stamp, object fields) => engine.Apply(new("network", Network: new(kind, stamp, "synthetic", JsonSerializer.SerializeToElement(fields))));
                void WaitForGenusRefresh() {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    var delay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1150) };
                    delay.Tick += (_, _) => { delay.Stop(); frame.Continue = false; };
                    delay.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
                }
                GenusEvent("pet-roster", now, new CapturedPetRoster(1002, [new(1002, 1)], [], 0) {
                    Genera = [new(2, 1, 0, [new(1, 2, 146, 1035, false, true)])] });
                WindowTest(ShowPetGenus, "Pet-genus-live", window => {
                    var table = LayoutVisuals<LiveTable>(window).Single();
                    if (table.Items.OfType<GenusRow>().Single().Level != 1) throw new InvalidOperationException("Live genus baseline missing.");
                    GenusEvent("pet-added", now + 1, new CapturedPetLevel(1002, 1, 1, new(2, 4)));
                    WaitForGenusRefresh();
                    if (table.Items.OfType<GenusRow>().Single() is not { Level: 4, Value: "10.3%" })
                        throw new InvalidOperationException("Open genus view did not refresh acquisition-driven genus level.");
                    Render(window, "test-artifacts/Feature-Pet-genus-updated.png");
                    GenusEvent("account-login", now + 2, new LoginReading(0, 2301, []));
                    WaitForGenusRefresh();
                    if (table.Items.Count != 0) throw new InvalidOperationException("Open genus view exposed previous-owner bonuses during login.");
                    Render(window, "test-artifacts/Feature-Pet-genus-login-cleared.png");
                });
            } finally { engine = genusEngine; viewed = genusViewed; }
            WindowTest(() => ShowPetSpawns(1002, this, sample), "Pet-spawns");
            WindowTest(() => ShowNearbyPets(this, sample, 2), "Nearby-pets", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count == 0)
                    throw new InvalidOperationException("Nearby farming lost the known incomplete pet at the captured location.");
            });
            WindowTest(ShowFieldBosses, "Field-bosses");
            ShopPurchaseProgress[] shopRows = [new(2301, 133019901, 5, now + 3600000, true, now, "", true),
                new(2301, 233019901, 4, now + 3600000, true, now, "", true), new(2301, 1209, 1, now - 1000, true, now - 2000, "Example cleric")];
            WindowTest(() => ShowShopPurchases(shopRows), "Shop-purchases", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 3) throw new InvalidOperationException("Shop counter view lost captured goods rows.");
            });
            var weeklyRows = ChecklistTasks.Complete(ChecklistTasks.Summarize(shopRows, now).Append(new(2301, "Example cleric", "Odyle crafting (character)", 3, 4, now + 3600000, "In progress")),
                [(2301, "Example cleric"), (2301, "Other cleric")], now);
            WindowTest(() => ShowWeeklyPurchases(weeklyRows), "Weekly-purchases", window => {
                var rows = LayoutVisuals<LiveTable>(window).Single().Items.OfType<WeeklyPurchaseRow>().ToArray();
                if (rows.Length != 11 || !rows.Any(r => r.Count == 9) || !rows.Any(r => r.Count == 3 && r.Task == "Odyle crafting (character)") || !rows.Any(r => r.Count == null && r.Status == "Previous period"))
                    throw new InvalidOperationException("Weekly task view lost combined or expired-period progress.");
                var search = LayoutVisuals<TextBox>(window).Single(t => t.ToolTip?.ToString() == "Search character, server or task"); search.Text = "Other cleric";
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 2) throw new InvalidOperationException("Weekly character search mixed owner scope.");
                var table = LayoutVisuals<LiveTable>(window).Single(); table.SelectedIndex = 0;
                var amount = LayoutVisuals<TextBox>(window).Single(t => t.ToolTip?.ToString() == "Current-period progress"); amount.Text = "2";
                LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Save progress").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (!table.Items.OfType<WeeklyPurchaseRow>().Any(r => r.Count == 2 && r.Status == "Entered locally")) throw new InvalidOperationException("Weekly local entry was not displayed.");
                table.SelectedItem = table.Items.OfType<WeeklyPurchaseRow>().Single(r => r.Status == "Entered locally");
                LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Clear progress").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (!table.Items.OfType<WeeklyPurchaseRow>().Any(r => r.Count == null && r.Status == "Cleared locally")) throw new InvalidOperationException("Weekly local clear was not displayed.");
                search.Clear();
            });
            var appearanceBytes = Enumerable.Range(0, AppearanceSettings.ByteLength).Select(i => (byte)(i * 31 % 256)).ToArray(); appearanceBytes[0] = 12;
            string appearanceData = Convert.ToBase64String(appearanceBytes);
            WindowTest(() => ShowAppearanceSettings("Synthetic captured appearance", appearanceData), "Appearance-settings", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count != 34) throw new InvalidOperationException("Appearance colors are incomplete.");
                table.SelectedIndex = 0;
                if (!LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Copy value").IsEnabled) throw new InvalidOperationException("Selected appearance value cannot be copied.");
                var search = LayoutVisuals<TextBox>(window).Single(t => t.Tag?.ToString() == "Search color or setting..."); search.Text = "HairColor";
                if (table.Items.Count != 1) throw new InvalidOperationException("Appearance field search failed.");
                search.Clear(); var category = LayoutVisuals<ComboBox>(window).Single(); category.SelectedIndex = 1;
                if (table.Items.Count != 260) throw new InvalidOperationException("Appearance scalar settings missing.");
                search.Text = "Hairstyle";
                if (table.Items.OfType<AppearanceSettingRow>().Single().Field != "HairType") throw new InvalidOperationException("Readable appearance label search failed.");
                search.Text = "HairType";
                if (table.Items.OfType<AppearanceSettingRow>().Single().Setting != "Hairstyle") throw new InvalidOperationException("Protocol field search no longer retains readable labels.");
                search.Clear();
                category.SelectedIndex = 2;
                if (table.Items.Count != 42) throw new InvalidOperationException("Appearance body filter failed.");
                category.SelectedIndex = 0;
            });
            var choiceBytes = Enumerable.Repeat((byte)101, AppearanceSettings.ByteLength).ToArray(); choiceBytes[0] = 12;
            WindowTest(() => ShowAppearanceSettings("Synthetic captured skin palette", Convert.ToBase64String(choiceBytes), gender: "female"), "Appearance-skin-palette", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count != 35) throw new InvalidOperationException("Resolved skin color missing from colors filter.");
                var search = LayoutVisuals<TextBox>(window).Single(t => t.Tag?.ToString() == "Search color or setting..."); search.Text = "SkinColor";
                var row = table.Items.OfType<AppearanceSettingRow>().Single();
                if (!row.IsColor || row.Value != AppearancePalette.SkinColor("female", 101)) throw new InvalidOperationException("Skin palette swatch is not the captured gender's color.");
                table.SelectedItem = row;
                if (!LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Copy value").IsEnabled) throw new InvalidOperationException("Skin color cannot be copied.");
            });
            WindowTest(() => ShowAppearanceSettings("Synthetic captured choices", Convert.ToBase64String(choiceBytes)), "Appearance-choices", window => {
                var category = LayoutVisuals<ComboBox>(window).Single(); category.SelectedIndex = 1;
                var search = LayoutVisuals<TextBox>(window).Single(t => t.Tag?.ToString() == "Search color or setting...");
                var table = LayoutVisuals<LiveTable>(window).Single();
                search.Text = "Pointy Ears";
                if (table.Items.Count != 1) throw new InvalidOperationException("Choice labels cannot be searched.");
                search.Text = "bTattooSymmetry";
                if (table.Items.OfType<AppearanceSettingRow>().Single().Value != "On") throw new InvalidOperationException("Toggle label missing.");
                search.Text = "bUseBodyGlossiness";
                if (table.Items.OfType<AppearanceSettingRow>().Single().Value != "Individual Face/Body") throw new InvalidOperationException("Body gloss choice missing.");
            });
            WindowTest(() => ShowAppearanceSettings("Unsupported appearance", "AQID"), "Appearance-settings-invalid", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 0 || LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Copy value").IsEnabled ||
                    !LayoutVisuals<TextBlock>(window).Any(t => t.Text.Contains("unsupported format"))) throw new InvalidOperationException("Invalid appearance data was silently presented as valid settings.");
            });
            WindowTest(() => ShowAppearancePresets(() => new("Example character", 2301, "650000000000000001", now, [new(0, "Named preset", appearanceData), new(2, null, "")]) { Current = new(1, "light", "male", appearanceData, 123, 456), Equipment = [new(110220014, 110230033, [new(1, 255, 128, 0)])], EquipmentComplete = true }), "Appearance-presets", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 3 || !LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Export presets").IsEnabled) throw new InvalidOperationException("Appearance preset list or export action unavailable.");
                if (!LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "View colors and settings").IsEnabled) throw new InvalidOperationException("Appearance inspect action unavailable.");
                if (LayoutVisuals<LiveTable>(window).Single().Columns[1].ActualWidth < 200) throw new InvalidOperationException("Appearance preset names are hidden.");
            });
            WindowTest(() => ShowBlackMarket(() => [new(201, now + 3600000, now, [new(1, 110000001, 2, 1, 2, 1000, 750, false), new(2, 110000002, 5, 2, 2, 2000, 2000, true)])]), "Black-market", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 2) throw new InvalidOperationException("Black market view lost available/sold-out rows.");
            });
            WindowTest(() => ShowPartyFinder(() => new(1, 6000000001, [new(6000000001, "Example cleric", 2301, "Cleric", 50, 123, 44000, true), new(7000000001, "Example ranger", 2301, "Ranger", 50, 115, 42000, false)])), "Party-finder", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 2) throw new InvalidOperationException("Party finder lost ready/not-ready members.");
            });
            WindowTest(() => ShowAbyss(new("Example cleric", 5, 123456, 20, 0, null, -2), now, [new(2301,"light"),new(2302,"dark")]), "Abyss-season", window => {
                var tables = LayoutVisuals<LiveTable>(window).ToArray();
                if (tables.Length != 2 || tables.Sum(t => t.Items.Count) != 3) throw new InvalidOperationException("Abyss view lost season or matchup rows.");
            });
            WindowTest(() => ShowSiege([new(1001,"progress",now+600000,null,null),new(2001,"end",null,now-60000,now+3600000)], [new(1001,"light"),new(1002,null)], [new(1001,"light",now,"Example cleric",2301)]), "Abyss-siege", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 4) throw new InvalidOperationException("Siege view lost event or ownership rows.");
            });
            WindowTest(() => ShowScoreboard(() => [new("650000000000000001","Example cleric","2301","Cleric",10,2,5),new("650000000000000002","Not identified","Unknown","Unknown",4,3,2)]), "Battle-scoreboard", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 2) throw new InvalidOperationException("Battle scoreboard lost identified/unidentified players.");
            });
            WindowTest(() => ShowAccountCharacters([new(123, new(6000000001, "Example cleric", 25, 50, "Dawn Watch", 12345) { GearScore = 321, Appearance = new("light", "female", "DAID", 456, [new(123, 456, [new(1, 255, 128, 0)])]) }, now) { LiveAppearance = new("Example cleric", 123, "6000000001", now, [new(0, "Saved named preset", "AQID")]) { PresetsAt = now } }]), "Saved-characters", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 1) throw new InvalidOperationException("Saved characters view lost roster rows.");
                LayoutVisuals<LiveTable>(window).Single().SelectedIndex = 0;
                if (!LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "View appearance").IsEnabled) throw new InvalidOperationException("Saved character appearance action unavailable.");
                WindowTest(() => LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "View appearance").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)), "Saved-appearance", child => {
                    if (LayoutVisuals<LiveTable>(child).Single().Items.Count != 2) throw new InvalidOperationException("Saved appearance action lost current appearance or named preset.");
                });
            });
            WindowTest(() => ShowDutyMissions([new(123, 5, now + 3600000, now), new(456, 3, now - 1000, now - 2000)], now), "Duty-missions", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count != 2 || !table.Items.OfType<WeeklyPurchaseRow>().Any(r => r.Count == null && r.Status == "Previous period") || !table.Items.OfType<WeeklyPurchaseRow>().Any(r => r.Count == 5 && r.Status == "Target reached"))
                    throw new InvalidOperationException("Duty mission view lost current/expired status distinction.");
            });
            WindowTest(() => ShowMapEvents([new(123, "Defend the outpost", 2, 3, false, now), new(456, "Forest patrol", 4, 1, true, now)]), "Map-events", window => {
                var rows = LayoutVisuals<LiveTable>(window).Single().Items.OfType<MapEventProgress>().ToArray();
                if (rows.Length != 2 || rows.Count(r => r.Done) != 1 || rows[0].Stage != 2 || rows[0].Step != 3)
                    throw new InvalidOperationException("Map events view lost progress or completion state.");
            });
            viewed.Actors[sample.LocalPlayer] = viewed.Actors[sample.LocalPlayer] with { Metadata = new(50, "Dawn Watch", now, now) };
            WindowTest(() => new CharacterEditor(viewed, preferences) { Owner = this }.Show(), "Player-metadata", window => {
                var table = LayoutVisuals<DataGrid>(window).Single();
                var row = table.Items.OfType<CharacterEditor.CharacterEntry>().Single(r => r.Id == sample.LocalPlayer);
                if (row.Level != "50" || row.Guild != "Dawn Watch" || table.Columns.Where(c => c.Header is "Level" or "Guild").Count(c => c.IsReadOnly) != 2)
                    throw new InvalidOperationException("Character details lost read-only captured level/guild.");
            });
            viewed.GatheredObjects = new() { [1590001] = 3, [5678] = 2 };
            var gatheringObjects = new WorldObjectTracker();
            gatheringObjects.Apply(new("world-object", now, "synthetic", JsonSerializer.SerializeToElement(new WorldObjectReading(1, 1590001, 0, 0, 0, 0, true, 0, 0, 0, 0))));
            gatheringTracking.Reset(now);
            gatheringTracking.Apply(gatheringObjects, new("gathered", now, "synthetic", JsonSerializer.SerializeToElement(new { EntityId = 1 })));
            WindowTest(ShowGathering, "Gathering", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count != 2 || !table.Items.Cast<object>().Any(r => r.GetType().GetProperty("Name")?.GetValue(r)?.ToString() == "Odyle"))
                    throw new InvalidOperationException("Gathering summary lost captured counts or object names.");
                var scope = LayoutVisuals<ComboBox>(window).Single(); scope.SelectedIndex = 1; scope.SelectedIndex = 2;
                var reset = LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Reset tracking");
                if (!reset.IsEnabled || table.Items.Count != 1) throw new InvalidOperationException("Gathering tracking totals or reset are unavailable in tracking scope.");
                window.UpdateLayout(); Render(window, "test-artifacts/Feature-Gathering-tracking.png");
                reset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (table.Items.Count != 0 || gatheringTracking.Snapshot().Counts.Count != 0) throw new InvalidOperationException("Gathering tracking reset did not clear its independent totals.");
                scope.SelectedIndex = 0;
                if (table.Items.Count != 2 || reset.IsEnabled) throw new InvalidOperationException("Gathering scope toggle failed to restore the selected record.");
            });
            killTracking.Reset(now); killTracking.Record(2000001); killTracking.Record(2000001); killTracking.Record(int.MaxValue);
            gatheringTracking.Record(1590001);
            WindowTest(ShowFarming, "Farming", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                var category = LayoutVisuals<ComboBox>(window).Single();
                var search = LayoutVisuals<TextBox>(window).Single(t => t.Tag?.ToString() == "Search name or ID...");
                var reset = LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Reset kills");
                if (table.Items.Count != 2) throw new InvalidOperationException("Farming kill rows are missing.");
                search.Text = "Punching Bag";
                if (table.Items.Count != 1) throw new InvalidOperationException("Named farming search failed.");
                search.Text = "no matching NPC";
                if (table.Items.Count != 0) throw new InvalidOperationException("Farming search does not filter rows.");
                search.Clear(); category.SelectedIndex = 1;
                if (table.Items.Count != 1 || reset.Content?.ToString() != "Reset gathers") throw new InvalidOperationException("Farming gathering scope failed.");
                reset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (gatheringTracking.Snapshot().Counts.Count != 0 || killTracking.Snapshot().Counts.Count != 2) throw new InvalidOperationException("Gather reset changed kill totals.");
                gatheringTracking.Record(1590001); category.SelectedIndex = 0;
                reset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (killTracking.Snapshot().Counts.Count != 0 || gatheringTracking.Snapshot().Counts.Count != 1) throw new InvalidOperationException("Kill reset changed gather totals.");
                killTracking.Record(2000001);
                LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Refresh").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (table.Items.Count != 1) throw new InvalidOperationException("Farming refresh did not show new totals.");
                lootTracking.Reset(now); lootTracking.Record(110120003, 7); category.SelectedIndex = 2;
                if (table.Items.Count != 1 || reset.Content?.ToString() != "Reset loot" ||
                    table.Items[0].GetType().GetProperty("Name")?.GetValue(table.Items[0])?.ToString() != "Ludra's Blade of Extinction" ||
                    (long?)table.Items[0].GetType().GetProperty("Count")?.GetValue(table.Items[0]) != 7)
                    throw new InvalidOperationException("Farming loot names or quantities are incorrect.");
                window.UpdateLayout(); Render(window, "test-artifacts/Feature-Farming-loot.png");
                reset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (lootTracking.Snapshot().Counts.Count != 0 || killTracking.Snapshot().Counts.Count != 1 || gatheringTracking.Snapshot().Counts.Count != 1)
                    throw new InvalidOperationException("Farming loot reset changed other tracking scopes.");
                soulTracking.Reset(now); soulTracking.Record(1002, 7); category.SelectedIndex = 3;
                if (table.Items.Count != 1 || reset.Content?.ToString() != "Reset souls" ||
                    table.Items[0].GetType().GetProperty("Name")?.GetValue(table.Items[0])?.ToString() != PetCatalogue.Default.Find(1002)?.Name ||
                    (long?)table.Items[0].GetType().GetProperty("Count")?.GetValue(table.Items[0]) != 7)
                    throw new InvalidOperationException("Farming pet soul names or quantities are incorrect.");
                window.UpdateLayout(); Render(window, "test-artifacts/Feature-Farming-souls.png");
                reset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (soulTracking.Snapshot().Counts.Count != 0 || killTracking.Snapshot().Counts.Count != 1 || gatheringTracking.Snapshot().Counts.Count != 1)
                    throw new InvalidOperationException("Soul reset changed other farming counters.");
                petRegistrationTracking.Reset(now); petLevelTracking.Reset(now);
                petRegistrationTracking.Record(1002); petLevelTracking.Record(1002, 2);
                foreach (int index in new[] { 4, 5 }) {
                    category.SelectedIndex = index;
                    if (table.Items.Count != 1 || (long?)table.Items[0].GetType().GetProperty("Count")?.GetValue(table.Items[0]) != (index == 4 ? 1 : 2))
                        throw new InvalidOperationException("Farming pet registrations or levels are incorrect.");
                    window.UpdateLayout(); Render(window, $"test-artifacts/Feature-Farming-pets-{index}.png");
                    reset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if (index == 4 && petLevelTracking.Snapshot().Counts[1002] != 2)
                        throw new InvalidOperationException("Registration reset changed pet level totals.");
                }
                category.SelectedIndex = 0;
                window.UpdateLayout(); Render(window, "test-artifacts/Feature-Farming-refreshed.png");
                var farmingLedgers = new[] { killTracking, gatheringTracking, lootTracking, soulTracking, petRegistrationTracking, petLevelTracking };
                foreach (var ledger in farmingLedgers) ledger.Record(1002);
                LayoutVisuals<Button>(window).Single(b => b.Content?.ToString() == "Reset all").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (farmingLedgers.Any(l => l.Snapshot().Counts.Count != 0) || farmingLedgers.Select(l => l.Snapshot().Since).Distinct().Count() != 1 || table.Items.Count != 0)
                    throw new InvalidOperationException("Combined farming reset did not clear every category together.");

            });
            WindowTest(() => ShowWorldObjects([new(1, 1590001, 12.5f, -2, 3, 0, true, 0, 0, 0, 1)], 1010), "World-objects", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count != 1 || table.Items[0].GetType().GetProperty("Name")?.GetValue(table.Items[0])?.ToString() != "Odyle")
                    throw new InvalidOperationException("World object view lost its captured entry or name.");
            });
            trackedTargets=new();var trackedPet=PetFarmLinks.Default.Find(1002)!;
            killTracking.Reset(now);killTracking.Record(trackedPet.Npcs[0],5);soulTracking.Reset(now);soulTracking.Record(1002,2);
            lootTracking.Reset(now);lootTracking.Record(110120003,7);
            engine.Apply(new("local-player",Actor:new(9875,"Tracked pet demo",true,true,0,"Cleric",2301)));
            engine.Apply(new("network",Network:new("pet-roster",now,"synthetic",JsonSerializer.SerializeToElement(
                new CapturedPetRoster(1002,[new(1002,1)],[new(1002,0)],0)))));
            System.Windows.Threading.DispatcherTimer? targetRefreshTimer=null;
            WindowTest(ShowTrackedTargets,"Tracked-targets",window=>{
                targetRefreshTimer=trackedTargetsTimer;
                var sessionSummary=LayoutVisuals<TextBlock>(window).Single(t=>t.Tag?.ToString()=="Farming session summary");
                if(sessionSummary.DataContext is not FarmingSessionSummary {Kills:5,Souls:2} session||!sessionSummary.Text.Contains("Kills: 5")||!sessionSummary.Text.Contains($"{session.KillsPerHour:N0}/h")||!sessionSummary.Text.Contains("Souls: 2"))throw new InvalidOperationException("Targets session totals or hourly rate are missing.");
                if(targetRefreshTimer?.IsEnabled!=true)throw new InvalidOperationException("Targets automatic refresh did not start.");
                var kind=LayoutVisuals<ComboBox>(window).Single(c=>c.Tag?.ToString()=="Target kind");
                var search=LayoutVisuals<TextBox>(window).Single(t=>t.Tag?.ToString()=="Find target by name or ID...");
                var table=LayoutVisuals<LiveTable>(window).Single();
                kind.SelectedIndex=2;search.Text="1002";
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Track").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(table.Items.Count!=1||table.Items[0] is not TargetRow {Count:"5",Souls:"2"} row||!row.Status.StartsWith("Complete"))throw new InvalidOperationException("Tracked pet totals or completion are incorrect.");
                LayoutVisuals<ComboBox>(window).Single(c=>c.Tag?.ToString()=="Tracked pet phase").SelectedIndex=2;
                if(((TargetRow)table.Items[0]).Status.StartsWith("Complete"))throw new InvalidOperationException("Pet target phase did not update.");
                table.SelectedIndex=0;
                var showMap=LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Show on map");
                if(!showMap.IsEnabled)throw new InvalidOperationException("Pet target has no map navigation.");
                showMap.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var targetMap=markerMapWindow??throw new InvalidOperationException("Target map did not open.");
                if(!LayoutVisuals<TextBlock>(targetMap).Any(t=>t.Tag?.ToString()=="Map marker status"&&t.Text.Contains($"Target: {PetFarmLinks.Default.Find(1002)!.Name} (1002)")))throw new InvalidOperationException("Map lost the selected target.");
                targetMap.UpdateLayout();Render(targetMap,"test-artifacts/Feature-Tracked-target-map.png");
                LayoutVisuals<Button>(targetMap).Single(b=>b.Content?.ToString()=="Back to all markers").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(LayoutVisuals<TextBlock>(targetMap).Any(t=>t.Tag?.ToString()=="Map marker status"&&t.Text.Contains("Target:")))throw new InvalidOperationException("Target map focus did not clear.");
                targetMap.Close();
                var unlinkedPet=PetCatalogue.Default.Pets.First(p=>PetFarmLinks.Default.Find(p.Id)==null);
                kind.SelectedIndex=2;search.Text=unlinkedPet.Id.ToString();
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Track").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var unlinkedRow=table.Items.Cast<TargetRow>().Single(r=>r.Kind=="pet"&&r.Id==unlinkedPet.Id);
                table.SelectedItem=unlinkedRow;
                if(unlinkedRow.Count!="?"||unlinkedRow.Nearby!="?"||!unlinkedRow.Name.StartsWith(unlinkedPet.Name)||
                    unlinkedRow.Status.Contains("kills per soul")||showMap.IsEnabled)
                    throw new InvalidOperationException("Unlinked catalogue pet lost its name or invented farming data.");
                window.UpdateLayout();Render(window,"test-artifacts/Feature-Unlinked-pet-target.png");
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Remove target").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                kind.SelectedIndex=3;search.Text="110120003";
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Track").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                table.SelectedItem=table.Items.Cast<TargetRow>().Single(r=>r.Kind=="item");
                LayoutVisuals<TextBox>(window).Single(t=>t.Tag?.ToString()=="Goal").Text="5";
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Set goal").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(table.Items.Cast<TargetRow>().Single(r=>r.Kind=="item") is not {Count:"7",Goal:"5",Status:"Complete"})throw new InvalidOperationException("Tracked item goal did not complete from loot quantities.");
                window.UpdateLayout();Render(window,"test-artifacts/Feature-Tracked-targets-populated.png");
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Remove target").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(table.Items.Count!=1||lootTracking.Snapshot().Counts[110120003]!=7)throw new InvalidOperationException("Removing a target changed farming totals.");
                void TraceEvent(string type,long at,object fields)=>engine.Apply(new("network",Network:new(type,at,"synthetic",System.Text.Json.JsonSerializer.SerializeToElement(fields))));
                TraceEvent("account-login",now+100,new LoginReading(0,2301,[]));
                engine.Apply(new("local-player",Actor:new(9876,"Trace demo",true,true,0,"Cleric",2301)));
                var traceIds=MapMarkers.Default.Groups.Where(g=>g.World==1010&&g.Category=="monolith").Select(g=>g.DatabaseId).Distinct().ToArray();
                TraceEvent("achievements",now+101,new AchievementReading(false,[traceIds[0]]));
                kind.SelectedIndex=4;search.Text="1010";
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Track").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var trace=table.Items.Cast<TargetRow>().Single(r=>r.Kind=="collectable");
                if(!trace.Count.StartsWith(">=")||trace.Status!="Progress unknown")throw new InvalidOperationException("Partial trace capture was displayed as a full achievement baseline.");
                window.UpdateLayout();Render(window,"test-artifacts/Feature-Traces-partial.png");
                TraceEvent("achievements",now+102,new AchievementReading(true,traceIds));
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Refresh").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                trace=table.Items.Cast<TargetRow>().Single(r=>r.Kind=="collectable");
                if(trace.Count!=trace.Goal||trace.Status!="Complete")throw new InvalidOperationException("Full trace achievement baseline did not complete the map target.");
                window.Width=780;window.Height=520;window.UpdateLayout();Render(window,"test-artifacts/Feature-Traces-complete-small.png");
                table.SelectedItem=trace;
                showMap.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var traceMap=markerMapWindow??throw new InvalidOperationException("Trace map did not open.");
                var traceTable=LayoutVisuals<LiveTable>(traceMap).Single();
                if(traceTable.Items.Count!=0)throw new InvalidOperationException("Completed trace map retained discovered markers.");
                TraceEvent("account-login",now+103,new LoginReading(0,2301,[]));
                LayoutVisuals<Button>(traceMap).Single(b=>b.Content?.ToString()=="Refresh position").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(traceTable.Items.Cast<MarkerRow>().Sum(r=>r.Count)!=560)throw new InvalidOperationException("Trace map retained the previous character discoveries after login.");
                if(!LayoutVisuals<TextBlock>(traceMap).Any(t=>t.Tag?.ToString()=="Map marker status"&&t.Text.Contains("Achievement list incomplete")))throw new InvalidOperationException("Trace map hid its incomplete baseline warning.");
                traceMap.UpdateLayout();Render(traceMap,"test-artifacts/Feature-Traces-map-new-character.png");traceMap.Close();
                engine.Apply(new("local-player",Actor:new(9998,"Nearby demo",true,true,0,"Cleric",2301)));
                TraceEvent("local-zone",now+200,new {MapId=20});
                trackedTargets!.Toggle("npc",2600094);trackedTargets.SetGoal("npc",2600094,2);refreshTrackedTargets!();
                var noticeList=LayoutVisuals<ListBox>(window).Single(l=>l.Tag?.ToString()=="Farming notices");
                if(noticeList.Items.Count!=0)throw new InvalidOperationException("Initial target scene generated old-state notices.");
                TraceEvent("npc-spawn",now+201,new {EntityId=555,NpcId=2600094});killTracking.Record(2600094,2);refreshTrackedTargets!();
                if(table.Items.Cast<TargetRow>().Single(r=>r.Kind=="npc"&&r.Id==2600094).Nearby!="1"||noticeList.Items.Count!=2||!noticeList.Items.Cast<string>().Any(n=>n.StartsWith("Goal reached:"))||!noticeList.Items.Cast<string>().Any(n=>n.StartsWith("Nearby increase:")))throw new InvalidOperationException("Nearby count or goal/spawn notice delivery failed.");
                table.SelectedItem=table.Items.Cast<TargetRow>().Single(r=>r.Kind=="npc"&&r.Id==2600094);
                var goalEdit=LayoutVisuals<TextBox>(window).Single(t=>t.Tag?.ToString()=="Goal");goalEdit.Text="123";refreshTrackedTargets!();
                if(goalEdit.Text!="123"||noticeList.Items.Count!=2)throw new InvalidOperationException("Refresh discarded an unfinished goal or duplicated notices.");
                window.UpdateLayout();Render(window,"test-artifacts/Feature-Targets-notices-small.png");
                killTracking.Reset(now+202);killTracking.Record(2600094,50);refreshTrackedTargets!();
                if(noticeList.Items.Count!=2)throw new InvalidOperationException("Counter reset emitted an old-progress notice.");
                if(sessionSummary.DataContext is not FarmingSessionSummary {Kills:50,Souls:2} resetSession||resetSession.KillsSince!=now+202||resetSession.SoulsSince!=now||!sessionSummary.Text.Contains("Kills: 50")||!sessionSummary.Text.Contains("souls since"))throw new InvalidOperationException("Session summary lost independent kill/soul reset boundaries.");
                window.UpdateLayout();Render(window,"test-artifacts/Feature-Targets-session-reset-small.png");
                TraceEvent("account-login",now+203,new LoginReading(0,2301,[]));refreshTrackedTargets!();
                if(noticeList.Items.Count!=0||table.Items.Cast<TargetRow>().Single(r=>r.Kind=="npc"&&r.Id==2600094).Nearby!="?")throw new InvalidOperationException("Character handoff retained old notices or a confirmed nearby count.");



            });
            trackedTargets!.Remove("npc",2600094);
            if(targetRefreshTimer?.IsEnabled!=false||trackedTargetsTimer!=null||refreshTrackedTargets!=null)throw new InvalidOperationException("Closed targets window retained active refresh callbacks.");
            WindowTest(() => {
                var plot = new PetSpawnPlot { Width=500,Height=400 };
                var routePoints = new[]{new PetSpawnPoint(1,100,100,0),new PetSpawnPoint(2,300,180,0),new PetSpawnPoint(3,250,350,0)};
                var start = new CapturedPosition(1010,50,50,0,now);
                plot.SetSite(new(0,"unmapped","Route preview",routePoints));plot.SetPlayer(start);plot.FitWithPlayer();
                plot.SetRoute(FarmingRoute.Build(routePoints,start));
                new Window {Title="Farming route preview",Content=plot,SizeToContent=SizeToContent.WidthAndHeight,ResizeMode=ResizeMode.NoResize}.Show();
            }, "Farming-route");
            var routeEngine=engine;var routeTargets=trackedTargets;trackedTargets=new();
            try {
                engine=new CombatEngine();
                WindowTest(ShowGatheringRoutes,"Gathering-routes",window=>{
                    var choice=LayoutVisuals<ComboBox>(window).Single(c=>c.Tag?.ToString()=="Gathering route resource");
                    if(choice.Items.Count==0)throw new InvalidOperationException("Gathering resources missing.");
                    var start=LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Start route");
                    if(!start.IsEnabled)throw new InvalidOperationException("Static route requires a player position.");
                    start.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    var selected=(GatherRouteChoice)choice.SelectedItem;
                    if(!selected.Targets.All(id=>trackedTargets.Snapshot().Any(t=>t.Kind=="gatherable"&&t.Id==id)))throw new InvalidOperationException("Route did not save selected resource.");
                    if(LayoutVisuals<Button>(window).Any(b=>b.Content?.ToString() is "Mark visited" or "Skip stop")||LayoutVisuals<CheckBox>(window).Any(c=>c.Content?.ToString()?.Contains("Auto-visit")==true))throw new InvalidOperationException("Static route retains automatic or manual gathering actions.");
                    LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Map overlay").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    var overlay=Application.Current.Windows.OfType<GatheringMapOverlay>().Single();overlay.Width=800;overlay.Height=600;overlay.UpdateLayout();
                    var alignment=overlay.AlignmentView;int stops=overlay.StopCount;
                    LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Lock overlay").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if(!overlay.Locked||!overlay.ClickThrough||!overlay.RouteVisible||stops==0)throw new InvalidOperationException("Locked static route is hidden without a player position.");
                    overlay.SetForegroundAllowed(false);
                    if(overlay.IsVisible||overlay.RenderingActive)throw new InvalidOperationException("Map overlay did not hide and stop rendering outside game focus.");
                    overlay.SetForegroundAllowed(true);
                    if(!overlay.NativeVisible||!overlay.RouteVisible||!overlay.Locked||!overlay.ClickThrough||overlay.StopCount!=stops||overlay.AlignmentView!=alignment||!overlay.RenderingActive)throw new InvalidOperationException("Map overlay failed to restore its route and calibration on focus return.");
                    void WaitForUi(int ms){var frame=new System.Windows.Threading.DispatcherFrame();var delay=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};delay.Tick+=(_,_)=>{delay.Stop();frame.Continue=false;};delay.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);}
                    var oldState=WindowState;
                    try {
                        WindowState=WindowState.Minimized;window.WindowState=WindowState.Minimized;
                        engine.Position.Apply(new("local-zone",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),"synthetic",JsonSerializer.SerializeToElement(new {MapId=1110,X=123,Y=456,Z=0})));
                        engine.GatheringTracking.Record(selected.Objects[0],5);engine.GatheringTracking.Reset(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());engine.Position.Clear(true);
                        WaitForUi(1800);
                        if(overlay.Owner!=null||!overlay.NativeVisible||!overlay.RouteVisible||overlay.StopCount!=stops||overlay.AlignmentView!=alignment)throw new InvalidOperationException("Minimizing, position loss or capture changes altered the static route.");
                    }finally{WindowState=oldState;window.WindowState=WindowState.Normal;}
                    window.Close();WaitForUi(80);
                    if(window.IsVisible||!overlay.NativeVisible||!overlay.RouteVisible)throw new InvalidOperationException("Closing route controls removed the overlay.");
                    ShowGatheringRoutes();if(!ReferenceEquals(window,gatheringRoutesWindow)||!window.IsVisible)throw new InvalidOperationException("Could not reopen hidden route controls.");
                    Render(overlay,"test-artifacts/Feature-Static-minimap-route.png");
                    window.UpdateLayout();Render(window,"test-artifacts/Feature-Static-route-controls.png");
                    LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Unlock overlay").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if(overlay.ClickThrough)throw new InvalidOperationException("Static route could not be unlocked for alignment.");
                    LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Hide overlay").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if(Application.Current.Windows.OfType<GatheringMapOverlay>().Any())throw new InvalidOperationException("Explicit Hide overlay did not close the route layer.");
                    overlay.SetForegroundAllowed(true);
                    if(overlay.IsVisible||refreshGatheringMapVisibility!=null)throw new InvalidOperationException("Focus resurrected an explicitly closed map overlay.");
                    long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    RouteMovement movement=new(new(1010,0,0,0,now-3000),false,1,true);
                    var following=new GatheringMapOverlay(null,()=>movement){Width=800,Height=600};
                    int relocationCount=0;
                    following.Relocated+=p=>{relocationCount++;following.UpdateMap(1010,FarmingRoute.Build([new(1,1000,1000,0),new(1,2000,500,0),new(1,20000,0,0)],p,2));};
                    try {
                        following.UpdateMap(1010,[new(1,1000,1000,0),new(1,2000,500,0)]);following.Show();WaitForUi(120);
                        movement=new(new(1010,200,0,0,now),false,1,true);WaitForUi(700);
                        var first=following.AlignmentView;WaitForUi(250);var second=following.AlignmentView;
                        if(second.X<=first.X||second.Zoom!=first.Zoom||!following.RouteVisible||!following.RenderingActive)throw new InvalidOperationException("Route does not advance smoothly between position updates at the calibrated scale.");
                        movement=movement with {Position=new(1010,20000,0,0,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),Relocation=1};WaitForUi(150);
                        if(relocationCount!=2||Math.Abs(following.AlignmentView.X-20000)>1||!following.RouteVisible)throw new InvalidOperationException("Teleport did not refresh and reanchor the visible route.");
                        movement=movement with {Active=false};WaitForUi(100);var held=following.AlignmentView;WaitForUi(200);
                        if(following.AlignmentView!=held||!following.NativeVisible)throw new InvalidOperationException("Capture stop failed to hold the visible route.");
                        Render(following,"test-artifacts/Feature-Predicted-minimap-route.png");
                    }finally{following.Close();}
                    if(following.RenderingActive)throw new InvalidOperationException("Closed route retained its rendering callback.");
                });
            }finally{engine=routeEngine;trackedTargets=routeTargets;}
            WindowTest(ShowMarkerMap, "World-map", window => {
                var world=LayoutVisuals<ComboBox>(window).Single(c=>c.Tag?.ToString()=="Marker world");
                var category=LayoutVisuals<Button>(window).Single(c=>c.Tag?.ToString()=="Marker categories");
                void Categories(string label)=>category.ContextMenu.Items.OfType<MenuItem>().Single(m=>m.Header?.ToString()==label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var table=LayoutVisuals<LiveTable>(window).Single();
                var search=LayoutVisuals<TextBox>(window).Single(t=>t.Tag?.ToString()=="Search all maps...");
                if(table.Items.Count==0)throw new InvalidOperationException("World map has no reference places.");
                table.SelectedIndex=0;string name=table.Items[0].GetType().GetProperty("Name")!.GetValue(table.Items[0])!.ToString()!;
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Pin selected").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Categories("No categories");search.Text="__no_matches__";
                if(!table.Items.Cast<object>().Any(r=>r.GetType().GetProperty("Name")?.GetValue(r)?.ToString()==name && r.GetType().GetProperty("Pinned")?.GetValue(r)?.ToString()=="Yes"))
                    throw new InvalidOperationException("Pinned place disappeared through filters.");
                window.UpdateLayout();Render(window,"test-artifacts/Feature-World-map-pinned.png");
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Clear map pins").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(table.Items.Cast<object>().Any(r=>r.GetType().GetProperty("Pinned")?.GetValue(r)?.ToString()=="Yes"))throw new InvalidOperationException("Map pins were not cleared.");
                search.Text="Guardian Lord Nahma";
                var results=LayoutVisuals<ComboBox>(window).Single(c=>c.Tag?.ToString()=="Map search results");
                if(results.Items.Count!=2)throw new InvalidOperationException("Cross-map search lost known boss results.");
                results.SelectedItem=results.Items.Cast<MapSearchGroup>().Single(g=>g.World==20 && g.Name=="Guardian Lord Nahma");
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Open result").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if((world.SelectedItem as MarkerWorld)?.Id!=20 || table.Items.Count!=1 || (table.SelectedItem as MarkerRow)?.Name!="Guardian Lord Nahma")
                    throw new InvalidOperationException("Search result did not open the correct map and place.");
                var trackingChoice=LayoutVisuals<ComboBox>(window).Single(c=>c.Tag?.ToString()=="Map tracking identity");
                var trackButton=LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Track selected");
                if(trackingChoice.Items.Count!=3||trackingChoice.SelectedItem!=null||trackButton.IsEnabled)throw new InvalidOperationException("Same-name map targets were silently assigned one NPC identity.");
                trackingChoice.SelectedItem=trackingChoice.Items.Cast<TargetChoice>().Single(c=>c.Id==2600094);
                trackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(trackedTargets!.Snapshot().Count(t=>t.Kind=="npc")!=1||!trackedTargets.Snapshot().Any(t=>t.Kind=="npc"&&t.Id==2600094)||trackButton.Content?.ToString()!="Stop tracking")throw new InvalidOperationException("Map tracking added an incorrect NPC identity.");
                LayoutVisuals<Button>(window).Single(b=>b.Content?.ToString()=="Tracked targets").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var targetsWindow=trackedTargetsWindow??throw new InvalidOperationException("Map did not open the target list.");
                var targetTable=LayoutVisuals<LiveTable>(targetsWindow).Single();
                if(!targetTable.Items.Cast<TargetRow>().Any(r=>r.Kind=="npc"&&r.Id==2600094))throw new InvalidOperationException("Map target was absent from the target list.");
                trackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(targetTable.Items.Cast<TargetRow>().Any(r=>r.Kind=="npc"&&r.Id==2600094))throw new InvalidOperationException("Open targets window did not refresh after map removal.");
                targetsWindow.Close();
                window.UpdateLayout();Render(window,"test-artifacts/Feature-World-map-search.png");
                search.Text="";Categories("All categories");
                world.SelectedIndex=(world.SelectedIndex+1)%world.Items.Count;
                Categories("All categories");
                if(table.Items.Count==0)throw new InvalidOperationException("Switching map failed to load reference places.");
            });
            WindowTest(ShowVisibleEquipment, "Visible-equipment");
            viewed.Actors[sample.LocalPlayer] = viewed.Actors[sample.LocalPlayer] with { Gear = new(now, true,
                [new(1, 110220003, new(1, 5, 1, 100, [], [new(19, 30)], [], [new(17010000, 2)], [], []))]) };
            WindowTest(ShowEquippedGear, "Equipped-gear", window => {
                var tables = LayoutVisuals<LiveTable>(window).ToArray();
                if (tables.Length != 2 || tables.Sum(t => t.Items.Count) != 3) throw new InvalidOperationException("Equipped gear or its details are missing.");
            });
            viewed.Actors[sample.LocalPlayer] = viewed.Actors[sample.LocalPlayer] with { Wings = new(now, [new(100, 0), new(200, 12)], now, 200, now, 300) };
            WindowTest(ShowWings, "Wings", window => {
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 3) throw new InvalidOperationException("Wing view lost owned or cosmetic-only wings.");
            });
            viewed.Actors[sample.LocalPlayer] = viewed.Actors[sample.LocalPlayer] with { Titles = new(now, [100, 200], [new(1, 100), new(2, 200), new(3, 100)]) };
            WindowTest(ShowTitles, "Titles", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count != 2) throw new InvalidOperationException("Title view lost captured ownership.");
                var search = LayoutVisuals<TextBox>(window).Single(); search.Text = "Attack";
                if (table.Items.Count != 1) throw new InvalidOperationException("Title slot filtering failed."); search.Clear();
            });
            viewed.Actors[sample.LocalPlayer] = viewed.Actors[sample.LocalPlayer] with { Collections = new(new(now, [new(100, 3)]), new(now, [new(200, 1)]), new(now, [new(300, 5)]), new(now, false, [400])) };
            WindowTest(ShowCollections, "Collections", window => {
                var table = LayoutVisuals<LiveTable>(window).Single();
                if (table.Items.Count != 4) throw new InvalidOperationException("Collection view lost a category.");
                var search = LayoutVisuals<TextBox>(window).Single(); search.Text = "Monoliths";
                if (table.Items.Count != 1) throw new InvalidOperationException("Collection filtering failed."); search.Clear();
            });
            viewed.Actors[sample.LocalPlayer] = viewed.Actors[sample.LocalPlayer] with { Daevanion = new(now, [new(100, [1, 2, 3]), new(200, [])]) };
            WindowTest(ShowDaevanion, "Daevanion", window => {
                var tables = LayoutVisuals<LiveTable>(window).ToArray();
                var boards = tables.Single(t => t.Items.OfType<DaevanionRow>().Any()); var nodes = tables.Single(t => t != boards);
                if (boards.Items.Count != 2 || nodes.Items.Count != 3) throw new InvalidOperationException("Daevanion view lost captured boards or nodes.");
                boards.SelectedIndex = 1;
                if (nodes.Items.Count != 0) throw new InvalidOperationException("Empty Daevanion board retained previous nodes.");
                boards.SelectedIndex = 0;
            });
            WindowTest(ShowEffects, "Effects", window => {
                var effectTabs = LayoutVisuals<TabControl>(window).Single(); effectTabs.SelectedIndex = 1; window.UpdateLayout();
                if (LayoutVisuals<LiveTable>(window).Single().Items.Count != 1) throw new InvalidOperationException("Shield summary lost synthetic totals.");
                Render(window, "test-artifacts/Feature-Shields.png");
            });
            WindowTest(ShowChatSummary, "Chat-summary");
            WindowTest(() => CreateTrainingWindow().Show(), "Training-setup");
            var trainingA = FeatureTests.Sample(1); var trainingB = FeatureTests.Sample(2);
            trainingA.Training = new(trainingA.Start, trainingA.End, 30, "Example group", "Baseline attempt");
            trainingB.Training = new(trainingB.Start, trainingB.End, 30, "Example group", "Comparison attempt");
            archive.Add(trainingA); archive.Add(trainingB);
            try {
                WindowTest(ShowTrainingComparison, "Training-comparison", window => {
                    if (LayoutVisuals<LiveTable>(window).Single().Items.Count == 0)
                        throw new InvalidOperationException("Training comparison did not populate the selected attempts.");
                });
            } finally { archive.Remove(trainingA); archive.Remove(trainingB); }
            var previousTab = tabs.SelectedItem;
            try {
                viewed = PvpTests.Duel();
                tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => t.Header?.ToString() == "PvP"); Refresh();
                grids["PvP"].SelectedIndex = 0;
                WindowTest(ShowSelectedOpponent, "PvP-exchange");
            } finally { viewed = sample.ForStorage(); tabs.SelectedItem = previousTab; Refresh(); }
            var runSample = sample.Copy(); runSample.ObservedRun = new(600012, 1700000003000, 2, 123, 3, now);
            runSample.DungeonRun = new(Guid.NewGuid().ToString("N"), 600012, now - 60000, now, 3,
                StartedAt: now - 30000, ClearedAt: now - 2000, SawPrepare: true) {
                    BossKills = [new(100, now - 10000), new(101, now - 3000)], ReportedDeaths = 2, UsedItems = [new(110120003, 3)], ReceivedItems = [new(110120003, 5)],
                    Party = [new("Offline member", 2301, "Cleric", 50, 200, 90000), new("Departed member", 2301, "Ranger", 50, 190, 80000)], Skills = [17010000, 17000001], Splits = [new(now - 20000, "Open the gate", 2, 1, false), new(now - 10000, "Open the gate", 2, 2, true)] };
            var runArchive = FightArchiveSummary.Create(runSample); archive.Add(runArchive);
            try {
                WindowTest(ShowObservedRuns, "Observed-runs", window => {
                    if (LayoutVisuals<LiveTable>(window).Count(t => t.Items.Count > 0) != 7)
                        throw new InvalidOperationException("Observed run view did not populate groups, members, fights, items, checkpoints, skills and roster.");
                    var scroll = LayoutVisuals<ScrollViewer>(window).First(s => s.Content is Grid g && g.RowDefinitions.Count == 7);
                    scroll.ScrollToEnd(); window.UpdateLayout(); Render(window, "test-artifacts/Feature-Observed-runs-items.png"); scroll.ScrollToTop();
                });
            } finally { archive.Remove(runArchive); }
            WindowTest(ShowReminderSettings, "Reminders", window => {
                var scroll = LayoutVisuals<ScrollViewer>(window).First(); scroll.ScrollToEnd(); window.UpdateLayout();
                Render(window, "test-artifacts/Feature-Reminders-bottom.png"); scroll.ScrollToTop();
            });
            var ledger = LootLedger.FromFight(sample);
            var petTrackerTest = new PetTrackerOverlayWindow(preferences, () => { }, _ => { }, () => { }); opened.Add(petTrackerTest);
            try {
                var nearby = new PetNearbyTracker().Update(sample.LocalPosition, sample.Pets, 2);
                petTrackerTest.UpdateCollected([new(1002,"Unstable Spider","12 souls held · 13 more for level 2",3), new(1005,"Young Kailin","2 souls held · 3 more for collection",1)]);
                petTrackerTest.Update(nearby, sample.LocalPosition, now, true, 2); petTrackerTest.Show(); petTrackerTest.UpdateLayout();
                Render(petTrackerTest, "test-artifacts/Feature-Pet-tracker-overlay.png");
            } finally { petTrackerTest.Close(); }
            preferences.OverlaysGameOnly=false;
            ToggleFarmingOverlay();var farmingTest=farmingOverlay??throw new InvalidOperationException("Farming overlay did not open.");opened.Add(farmingTest);
            try {
                var previewRows=Enumerable.Range(1,7).Select(i=>new FarmingViewRow(new("npc",i,10),$"Farming target {i}",new("npc",i,i,10,false,[]),i)).ToArray();
                farmingTest.Update(new(previewRows,new(now,now,28,1680,3,false),[new("goal","npc:1","Example target",10,null)],true),true,2);
                farmingTest.UpdateLayout();
                if(!LayoutVisuals<TextBlock>(farmingTest).Any(t=>t.Tag?.ToString()=="Farming overlay summary"&&t.Text.Contains("Showing 5 of 7"))||LayoutVisuals<TextBlock>(farmingTest).Any(t=>t.Text=="Farming target 6"))throw new InvalidOperationException("Farming overlay row limit or summary failed.");
                if(!LayoutVisuals<TextBlock>(farmingTest).Any(t=>t.Tag?.ToString()=="Farming overlay notice"&&t.Text.Contains("Goal reached")))throw new InvalidOperationException("Farming overlay notice delivery failed.");
                Render(farmingTest,"test-artifacts/Feature-Farming-overlay.png");
                farmingTest.Update(new(previewRows,new(now,now,28,1680,3,false),[
                    new("registered","pet:1005","Unstable Spider",null,null),
                    new("levelup","pet:1005","Unstable Spider",3,null),
                    new("milestone","collection:50","50 pets collected",50,null)],false),true,2);
                farmingTest.UpdateLayout();
                var noticeText=LayoutVisuals<TextBlock>(farmingTest).Single(t=>t.Tag?.ToString()=="Farming overlay notice").Text;
                if(noticeText.Split('\n').Length!=3||!noticeText.Contains("Pet registered")||!noticeText.Contains("Pet level up")||!noticeText.Contains("10 candy")||noticeText.Contains("Goal reached"))
                    throw new InvalidOperationException("Farming overlay did not render the three latest pet notices.");
                Render(farmingTest,"test-artifacts/Feature-Farming-pet-notices.png");
                RefreshFarmingOverlay(false);if(farmingTest.IsVisible)throw new InvalidOperationException("Farming overlay ignored foreground visibility gating.");
                RefreshFarmingOverlay(true);if(!farmingTest.IsVisible)throw new InvalidOperationException("Farming overlay did not restore visibility.");
                LayoutVisuals<Button>(farmingTest).Single(b=>b.Content?.ToString()=="Targets").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(trackedTargetsWindow==null)throw new InvalidOperationException("Farming overlay Targets button failed.");trackedTargetsWindow.Close();
            } finally {farmingTest.Close();}
            if(farmingOverlay!=null||farmingOverlayView!=null)throw new InvalidOperationException("Closed farming overlay retained its state monitor.");
            var petMapTest = new PetMapOverlayWindow(preferences, () => { }); opened.Add(petMapTest);
            try {
                var nearby = new PetNearbyTracker().Update(sample.LocalPosition, sample.Pets, 2);
                petMapTest.Update(nearby, sample.LocalPosition, now, true, 2); petMapTest.Show(); petMapTest.UpdateLayout();
                Render(petMapTest, "test-artifacts/Feature-Pet-map-overlay.png");
                if (sample.LocalPosition is { } capturedPosition) {
                    var estimated = new EstimatedPosition(capturedPosition.MapId, capturedPosition.X + 500, capturedPosition.Y + 500, null, now, 2500, false);
                    var presentation = MapPositionPresentation.Select(capturedPosition, estimated, true, now, true);
                    var estimatedRows = new PetNearbyTracker().Update(presentation.Position, sample.Pets, 2);
                    petMapTest.Update(estimatedRows, presentation.Position, now, true, 2, presentation); petMapTest.UpdateLayout();
                    Render(petMapTest, "test-artifacts/Feature-Pet-map-estimated.png");
                }
                petMapTest.Update([], null, now, false, 1); petMapTest.UpdateLayout();
                Render(petMapTest, "test-artifacts/Feature-Pet-map-empty.png");
            } finally { petMapTest.Close(); }
            var lootOverlayTest = new LootOverlayWindow(preferences, () => { }, () => { }); opened.Add(lootOverlayTest);
            try { lootOverlayTest.Update(ledger.OverlaySnapshot(), true); lootOverlayTest.Show(); lootOverlayTest.UpdateLayout(); Render(lootOverlayTest, "test-artifacts/Feature-Loot-overlay.png"); }
            finally { lootOverlayTest.Close(); }
            var bossAlerts=new BossAlertOverlayWindow(preferences,()=>{});opened.Add(bossAlerts);
            try{
                bossAlerts.Update([new(1,"Example boss",123,"Casting",now,now+3000,4000,true),new(1,"Example boss",0,"Phase 2",now,now+3000,0,false)],now,id=>"Example ability");
                bossAlerts.Show();bossAlerts.UpdateLayout();
                if(!LayoutVisuals<TextBlock>(bossAlerts).Any(t=>t.Text.Contains("targeting you"))||!LayoutVisuals<ProgressBar>(bossAlerts).Any(b=>b.IsVisible&&b.Value>0))throw new InvalidOperationException("Boss warning is missing its captured target or timed cast bar.");
                Render(bossAlerts,"test-artifacts/Feature-Boss-alerts.png");
                var animatedBar=LayoutVisuals<ProgressBar>(bossAlerts).First(b=>b.IsVisible);
                CountdownBar.Update(animatedBar,.75,3);
                var animationFrame=new System.Windows.Threading.DispatcherFrame();
                var animationDelay=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
                animationDelay.Tick+=(_,_)=>{animationDelay.Stop();animationFrame.Continue=false;};
                animationDelay.Start();System.Windows.Threading.Dispatcher.PushFrame(animationFrame);
                if(animatedBar.Value>=.74||animatedBar.Value<0)throw new InvalidOperationException("Countdown gauge did not interpolate between data updates.");
                bossAlerts.Hide();
                if(animatedBar.HasAnimatedProperties)throw new InvalidOperationException("Hidden countdown gauge retained an animation clock.");
                bossAlerts.Show();
                var healthTracker=new BossAlertTracker();var healthFight=new Fight();
                healthFight.Actors[1]=new Actor(1,"Ultimate Berk",false,false,0,"Unknown",0){CapturedBoss=true};
                healthTracker.Apply(new("hp",now-100,"synthetic",System.Text.Json.JsonSerializer.SerializeToElement(new {EntityId=1,CurrentHp=1000,MaximumHp=1000})),healthFight);
                healthTracker.Apply(new("hp",now,"synthetic",System.Text.Json.JsonSerializer.SerializeToElement(new {EntityId=1,CurrentHp=530})),healthFight);
                bossAlerts.Update(healthTracker.Snapshot(now),now,id=>id.ToString());bossAlerts.UpdateLayout();
                if(!LayoutVisuals<TextBlock>(bossAlerts).Any(t=>t.Text.Contains("~50%"))||LayoutVisuals<ProgressBar>(bossAlerts).Any(b=>b.IsVisible))throw new InvalidOperationException("HP preparation must display its approximate threshold without a countdown.");
                Render(bossAlerts,"test-artifacts/Feature-Boss-hp-preparation.png");
                bossAlerts.Preview();bossAlerts.Update([],now,id=>id.ToString());
                if(!LayoutVisuals<TextBlock>(bossAlerts).Any(t=>t.Text.Contains("Drag here")))throw new InvalidOperationException("Boss alert placement preview is unavailable.");
            }finally{bossAlerts.Close();}
            var setupPreferences=preferences;var setupMeter=overlay;var setupAlerts=bossAlertOverlay;bool setupWanted=meterWanted;
            try{
                preferences=new Preferences();overlay=null;bossAlertOverlay=null;meterWanted=false;
                ShowOverlayPlacementSetup();
                if(placementSetup==null||overlay?.IsVisible!=true||bossAlertOverlay?.IsVisible!=true)throw new InvalidOperationException("First-run placement must show both overlays without capture.");
                placementSetup.UpdateLayout();Render(placementSetup,"test-artifacts/Feature-Overlay-first-setup.png");
                LayoutVisuals<Button>(placementSetup).Single(b=>b.Content?.ToString()=="Done — save positions").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if(!preferences.OverlayPlacementSetupCompleted||preferences.OverlayLeft!=overlay.Left||preferences.BossAlertsLeft!=bossAlertOverlay.Left||bossAlertOverlay.IsVisible||!preferences.OpenMeterOnStartup||!preferences.BossAlertsEnabled)
                    throw new InvalidOperationException("Completed placement must save defaults, enable restoration and hide the empty alert preview.");
                preferences.OverlayPlacementSetupCompleted=false;
                ShowOverlayPlacementSetup();placementSetup!.Close();
                if(preferences.OverlayPlacementSetupCompleted||bossAlertOverlay.PreviewActive)throw new InvalidOperationException("Dismissing setup must leave it incomplete and end preview.");
            }finally{
                placementSetup?.Close();overlay?.Close();bossAlertOverlay?.Close();
                preferences=setupPreferences;overlay=setupMeter;bossAlertOverlay=setupAlerts;meterWanted=setupWanted;SavePreferences();
            }
            var alignmentPath = System.IO.Path.Combine(AppContext.BaseDirectory, "test-artifacts", "gather-alignment-persistence.json");
            var calibrated = new GatheringMapOverlay(alignmentPath); opened.Add(calibrated);
            var calibratedView = new MapViewportState(500, 600, 20000, 20000, 2.5);
            try {
                calibrated.UpdateMap(1010, [new(1, 500, 600, 0)]); calibrated.Show();
                calibrated.Left = SystemParameters.VirtualScreenLeft + 40; calibrated.Top = SystemParameters.VirtualScreenTop + 40;
                calibrated.Width = 420; calibrated.Height = 320;
                LayoutVisuals<PetSpawnPlot>(calibrated).Single().RestoreViewport(calibratedView);
                calibrated.SetLocked(true);
            } finally { calibrated.Close(); }
            var restoredAlignment = new GatheringMapOverlay(alignmentPath); opened.Add(restoredAlignment);
            try {
                restoredAlignment.UpdateMap(1010, [new(1, 1000, 2000, 0)]); restoredAlignment.Show();
                if (restoredAlignment.Width != 420 || restoredAlignment.Height != 320 || restoredAlignment.Left != calibrated.Left || restoredAlignment.Top != calibrated.Top ||
                    restoredAlignment.AlignmentView != calibratedView || !restoredAlignment.Locked || !restoredAlignment.ClickThrough)
                    throw new InvalidOperationException("Gather overlay did not restore its saved size, position, scale and click-through lock.");
                restoredAlignment.UpdateMap(1110, [new(1, 3000, 4000, 0)]);
                if (restoredAlignment.AlignmentView != calibratedView || !restoredAlignment.Locked)
                    throw new InvalidOperationException("A new gather map discarded the last overlay calibration.");
                results.Add("PASS: gather overlay retains calibration and lock across reopen and new maps.");
            } finally { restoredAlignment.Close(); }
            var control = new ControlOverlayWindow(preferences, () => { }); opened.Add(control);
            var partyHealthOverlay=new OverlayWindow();opened.Add(partyHealthOverlay);
            try{
                var partyFight=new Fight{LocalPlayer=1,Label="Party health preview"};
                string[] partyNames=["Your Cleric","Party Templar","Party Chanter","Party Ranger","Party Sorcerer"];
                int[] partyHp=[1000,650,249,250,0];
                for(int i=0;i<partyNames.Length;i++){
                    partyFight.Actors[i+1]=new(i+1,partyNames[i],true,true,0,"Unknown",1);
                    partyFight.Network.Add(new("hp",now,"synthetic",JsonSerializer.SerializeToElement(new {EntityId=i+1,CurrentHp=partyHp[i],MaximumHp=1000})));
                }
                partyHealthOverlay.PartyDebuffs=()=>[new(3,100,"Stun",2.4),new(3,101,"Poison",12),new(2,102,"Slow",null)];
                partyHealthOverlay.PartyDistances=_=>new() { [2]="850 units away", [3]="2,400 units away", [4]="Distance unknown", [5]="4,100 units away" };
                partyHealthOverlay.Update(partyFight,new Preferences{OverlayPartyHealth=true,OverlayHeight=420});partyHealthOverlay.Show();partyHealthOverlay.UpdateLayout();
                if(partyHealthOverlay.RowCount!=5||partyHealthOverlay.CurrentScope!="Party health"||!LayoutVisuals<TextBlock>(partyHealthOverlay).Any(t=>t.Text.Contains("Stun 3s")))throw new InvalidOperationException("Party health mode must render idle members and per-member debuffs.");
                Render(partyHealthOverlay,"test-artifacts/Feature-Party-health.png");
                var oldSecondOptions=preferences.SecondMeter;
                bool firstHealth=preferences.OverlayPartyHealth;double? firstLeft=preferences.OverlayLeft;
                try{
                    preferences.SecondMeter=new();OpenSecondMeter();
                    var second=secondMeter!;
                    if(!second.IsVisible||secondMeterPreferences?.OverlayPartyHealth!=true)throw new InvalidOperationException("DPS2 must open in party-health mode.");
                    second.Update(partyFight,secondMeterPreferences!);second.UpdateLayout();
                    Render(second,"test-artifacts/Feature-DPS2-health.png");
                    second.Left=SystemParameters.WorkArea.Left+50;second.Top=SystemParameters.WorkArea.Top+80;second.Width=450;
                    secondMeterPreferences!.OverlayPartyHealth=false;secondMeterPreferences.OverlayHealing=true;
                    ToggleSecondMeter();
                    if(secondMeter!=null||SecondMeterOptions.Enabled||preferences.OverlayPartyHealth!=firstHealth||preferences.OverlayLeft!=firstLeft)
                        throw new InvalidOperationException("DPS2 toggle or settings leaked into DPS1.");
                    SecondMeterOptions.Enabled=true;
                    RestoreStartupOverlays(new StartupPlan(false,false,false,null));
                    if(secondMeter?.Width!=450||secondMeter.Left!=SystemParameters.WorkArea.Left+50||secondMeterPreferences?.OverlayHealing!=true||secondMeterPreferences.OverlayPartyHealth)
                        throw new InvalidOperationException("DPS2 did not restore its own placement and mode.");
                    var restoredSecond=secondMeter;OpenSecondMeter();
                    if(!ReferenceEquals(secondMeter,restoredSecond))throw new InvalidOperationException("DPS2 reopening created a duplicate window.");
                }finally{secondMeter?.Close();preferences.SecondMeter=oldSecondOptions;SavePreferences();}
            }finally{partyHealthOverlay.Close();}
            try { control.Update([new("Stun", 2.5), new("Sleep", 5)]); control.Show(); control.UpdateLayout(); Render(control, "test-artifacts/Feature-Control-overlay.png"); }
            finally { control.Close(); }
            var ping = new PingOverlayWindow(preferences, () => { }); opened.Add(ping);
            try { ping.Update(new(45, now, "Synthetic client echo"), now); ping.Show(); ping.UpdateLayout(); Render(ping, "test-artifacts/Feature-Ping-overlay.png"); }
            finally { ping.Close(); }
            results.Add("PASS: pet tracker/map, loot, control and ping overlays render synthetic readings, including an empty pet map.");
            var summonSkills = new SkillBreakdownView(code => code == 1 ? "Spirit strike" : "Shared skill", null);
            summonSkills.Update([
                new(1, 12000, 1200, 60, 1, 8, 25) { SummonAmount = 12000, SummonSources = "Fire spirit" },
                new(2, 8000, 800, 40, 0.67, 6, 50) { SummonAmount = 2000, SummonSources = "Fire spirit" }
            ], false, true, "#56C4FF", true);
            var summonWindow = new Window { Title = "Summon skill breakdown", Width = 360, Height = 180, Content = summonSkills };
            UiTheme.Apply(summonWindow, preferences); opened.Add(summonWindow);
            try {
                summonWindow.Show(); summonWindow.UpdateLayout();
                Render(summonWindow, "test-artifacts/Feature-Summon-skills.png");
                if (summonSkills.RowCount != 2) throw new Exception("Summon skill rows missing.");
            } finally { summonWindow.Close(); }
            results.Add("PASS: compact personal skill bars distinguish summon-only and mixed contributions.");
            File.WriteAllLines("feature-window-results.txt", results);
        } finally {
            foreach (var window in opened.Where(w => w.IsVisible)) window.Close();
            viewed = original; preferences = originalPreferences;
        }
    }
}
