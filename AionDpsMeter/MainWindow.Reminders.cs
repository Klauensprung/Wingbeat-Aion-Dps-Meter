using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    readonly EventReminders reminders = new();
    readonly ReminderQueue reminderQueue = new();
    readonly MediaPlayer reminderSound = new();
    WindowsNotifications? windowsNotifications;
    Window? reminderToast;
    long reminderToastUntil;
    void RefreshReminders()
    {
        if (reminderToast != null && Environment.TickCount64 >= reminderToastUntil) { reminderToast.Close(); reminderToast = null; }
        var due = reminders.Poll(DateTimeOffset.UtcNow, preferences.ReminderEvents ?? [], preferences.ReminderLeadSeconds);
        var petChanges = engine.DrainPetNotices(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var messages = due.Select(r => $"{r.Name} · {r.StartsAt.ToLocalTime():HH:mm}").ToList();
        if (capture != null)
            messages.AddRange(petChanges.Where(p => p.Acquired ? preferences.PetCollectionAlerts : preferences.PetLevelAlerts)
                .Select(p => $"{PetCatalogue.Default.Find(p.PetId)?.Name ?? $"Pet {p.PetId}"} " + (p.Acquired ? "collected" : $"reached level {p.Level}")));
        if (messages.Count > 0) ShowReminder(string.Join("\n", messages));
    }
    void ShowReminder(string text) => reminderQueue.Add(text, Environment.TickCount64);
    void DeliverReminders()
    {
        if (capture != null) { lock (engine.Gate) if (engine.SessionSignals.InCutscene) return; }
        long now = Environment.TickCount64;
        var text = reminderQueue.Take(now, now < reminderToastUntil);
        if (text == null) return;
        reminderToastUntil = now + Math.Clamp(preferences.ReminderDurationSeconds, 3, 60) * 1000L;
        if (preferences.ReminderWindowsNotifications && !GameForeground.AllowsOverlays()) {
            windowsNotifications ??= new WindowsNotifications(this, PlayReminderSound);
            if (windowsNotifications.Show("Wingbeat notification", text)) return;
        }
        var message = new TextBlock { Text = text, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new(16), Foreground = UiTheme.Brush("#E7EDF7") };
        var window = new Window { Title = "Wingbeat reminder", Width = 330, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Content = message, Background = UiTheme.Brush("#182231"), Left = SystemParameters.WorkArea.Right - 350, Top = SystemParameters.WorkArea.Top + 30 };
        window.MouseLeftButtonDown += (_, _) => window.Close();
        reminderToast = window;
        window.Closed += (_, _) => { if (ReferenceEquals(reminderToast, window)) { reminderToast = null; reminderToastUntil = 0; } };
        window.Show();
        PlayReminderSound();
    }
    void PlayReminderSound()
    {
        reminderSound.Stop();
        if (!preferences.ReminderSoundEnabled || string.IsNullOrWhiteSpace(preferences.ReminderSoundPath) || !File.Exists(preferences.ReminderSoundPath)) return;
        try {
            reminderSound.Open(new Uri(Path.GetFullPath(preferences.ReminderSoundPath)));
            reminderSound.Volume = double.IsFinite(preferences.ReminderVolume) ? Math.Clamp(preferences.ReminderVolume, 0, 1) : .5;
            reminderSound.Play();
        } catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { LocalLog.Write("Reminder sound could not be played."); }
    }
    void ShowReminderSettings()
    {
        var window = new Window { Title = "Event reminders", Owner = this, Width = 940, Height = 720, MinWidth = 480, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        UiTheme.Apply(window, preferences);
        var body = new StackPanel { Margin = new(20) };
        body.Children.Add(UiSections.Heading("Event reminders", "Choose your events and how you want to be notified. Changes save immediately.",
            "Reported Global schedule, reviewed 31 Oct 2026. Times use your Windows time zone. Wingbeat must be open; reminders do not confirm a server reset or event start. Source: aion2data.com/timers."));
        var sections = new WrapPanel(); body.Children.Add(sections);
        var schedulesPanel = new StackPanel(); var options = new StackPanel();
        var schedulesCard = UiSections.TableSection("Scheduled events", schedulesPanel); schedulesCard.Width = 400; schedulesCard.Margin = new(0, 0, 12, 12);
        var optionsCard = UiSections.TableSection("Delivery and extra alerts", options); optionsCard.Width = 400; optionsCard.Margin = new(0, 0, 0, 12);
        sections.Children.Add(schedulesCard); sections.Children.Add(optionsCard);
        var upcoming = new List<(EventSchedule Schedule, TextBlock Text)>();
        foreach (var schedule in EventReminders.Schedules) {
            var toggle = new CheckBox { Content = schedule.Name, IsChecked = preferences.ReminderEvents?.Contains(schedule.Id) == true, Margin = new(0, 5, 0, 0), ToolTip = schedule.Basis };
            toggle.Click += (_, _) => {
                var enabled = (preferences.ReminderEvents ?? []).ToHashSet();
                if (toggle.IsChecked == true) enabled.Add(schedule.Id); else enabled.Remove(schedule.Id);
                preferences.ReminderEvents = enabled.ToArray(); SavePreferences();
            };
            schedulesPanel.Children.Add(toggle);
            var next = new TextBlock { Margin = new(20, 2, 0, 5), ToolTip = schedule.Basis }; schedulesPanel.Children.Add(next); upcoming.Add((schedule, next));
        }
        void Choice(string label, int[] values, int current, Action<int> set) {
            options.Children.Add(new TextBlock { Text = label, Margin = new(0, 10, 0, 4) });
            var combo = new ComboBox { ItemsSource = values, SelectedItem = values.Contains(current) ? current : values[0] };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is int value) { set(value); SavePreferences(); } }; options.Children.Add(combo);
        }
        Choice("Notify before event (seconds)", [0, 30, 60, 120, 300, 600], preferences.ReminderLeadSeconds, v => preferences.ReminderLeadSeconds = v);
        Choice("Notice duration (seconds)", [5, 10, 15, 30, 60], preferences.ReminderDurationSeconds, v => preferences.ReminderDurationSeconds = v);
        var party = new CheckBox { Content = "Alert when a debuff affects several party members", IsChecked = preferences.PartyDebuffAlerts, Margin = new(0, 12, 0, 0),
            ToolTip = "Counts unique known party members (including you) with the same catalogued debuff. Friendly casts are excluded; unknown caster identity is not attributed to an enemy. Uses the notice and sound settings below." };
        party.Click += (_, _) => { preferences.PartyDebuffAlerts = party.IsChecked == true; SavePreferences(); }; options.Children.Add(party);
        Choice("Party members needed for a debuff alert", [2, 3, 4, 5, 6, 8, 10], preferences.PartyDebuffThreshold, v => preferences.PartyDebuffThreshold = v);
        var pets = new CheckBox { Content = "Notify when a captured pet level increases", IsChecked = preferences.PetLevelAlerts, Margin = new(0, 12, 0, 0),
            ToolTip = "Requires a previously captured lower level and an explicit level update. Roster loading and unknown prior levels do not trigger notices. Uses the sound and delivery settings below." };
        pets.Click += (_, _) => { preferences.PetLevelAlerts = pets.IsChecked == true; SavePreferences(); }; options.Children.Add(pets);
        var collectedPets = new CheckBox { Content = "Notify when a pet joins your collection", IsChecked = preferences.PetCollectionAlerts, Margin = new(0, 8, 0, 0),
            ToolTip = "Uses explicit captured acquisition messages. Loading a roster does not announce existing pets. Uses the delivery and sound settings below." };
        collectedPets.Click += (_, _) => { preferences.PetCollectionAlerts = collectedPets.IsChecked == true; SavePreferences(); }; options.Children.Add(collectedPets);
        var native = new CheckBox { Content = "Use Windows notifications outside Aion 2 / Wingbeat", IsChecked = preferences.ReminderWindowsNotifications,
            Margin = new(0, 12, 0, 0), ToolTip = "Windows controls native notification duration and delivery. The duration above applies to overlay notices." };
        native.Click += (_, _) => { preferences.ReminderWindowsNotifications = native.IsChecked == true; SavePreferences(); }; options.Children.Add(native);
        var sound = new CheckBox { Content = "Play selected sound", IsChecked = preferences.ReminderSoundEnabled, Margin = new(0, 12, 0, 6) };
        sound.Click += (_, _) => { preferences.ReminderSoundEnabled = sound.IsChecked == true; SavePreferences(); }; options.Children.Add(sound);
        options.Children.Add(new TextBlock { Text = "Sound volume" });
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = double.IsFinite(preferences.ReminderVolume) ? Math.Clamp(preferences.ReminderVolume, 0, 1) : .5 };
        volume.ValueChanged += (_, _) => { preferences.ReminderVolume = volume.Value; SavePreferences(); }; options.Children.Add(volume);
        var actions = new WrapPanel { Margin = new(0, 8, 0, 0) };
        var choose = new Button { Content = "Choose sound…", Padding = new(10, 6, 10, 6) };
        choose.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "Audio files|*.wav;*.mp3;*.wma", CheckFileExists = true }; if (dialog.ShowDialog(window) == true) { preferences.ReminderSoundPath = dialog.FileName; SavePreferences(); } };
        var preview = new Button { Content = "Test reminder", Margin = new(8, 0, 0, 0), Padding = new(10, 6, 10, 6) };
        preview.Click += (_, _) => ShowReminder("Example event reminder"); actions.Children.Add(choose); actions.Children.Add(preview); options.Children.Add(actions);
        void Update() { foreach (var item in upcoming) item.Text.Text = "Next: " + item.Schedule.Next(DateTimeOffset.UtcNow).ToLocalTime().ToString("ddd dd MMM HH:mm"); }
        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; clock.Tick += (_, _) => Update();
        window.Closed += (_, _) => clock.Stop(); window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Update(); clock.Start(); window.Show();
    }
}
