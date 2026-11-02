using System.Windows;
using System.Windows.Controls;

namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    Button? trainingButton;
    bool trainingWasActive;
    void InitializeTraining(Panel controls)
    {
        trainingButton = Button("Training…", ShowTraining);
        trainingButton.FontSize = 11; trainingButton.Padding = new(8, 4, 8, 4);
        controls.Children.Add(trainingButton);
    }
    void RefreshTraining()
    {
        bool active = engine.TrainingActive;
        if (trainingWasActive && !active) notice = "Training finished. Your attempt is saved in fight history.";
        trainingWasActive = active;
        string label = engine.TrainingStatus(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (trainingButton != null) trainingButton.Content = label;
        if (overlay != null) overlay.TrainingLabel = label;
    }
    void ShowTraining()
    {
        if (engine.TrainingActive) {
            engine.StopTraining(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (autosave == null) StartAutosave(false);
            notice = "Training stopped. Your attempt is saved in fight history.";
            RefreshTraining(); Refresh(); return;
        }
        CreateTrainingWindow().ShowDialog();
    }
    Window CreateTrainingWindow()
    {
        var window = new Window { Title = "Training attempt", Owner = this, Width = 480, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        UiTheme.Apply(window, preferences);
        var panel = new StackPanel { Margin = new(20) };
        panel.Children.Add(UiSections.Heading("Start a training attempt", "Measure your damage over a fixed duration or until you stop."));
        var name = new TextBox { Text = "Training", MaxLength = 120, Margin = new(0, 4, 0, 10) };
        var duration = new ComboBox { ItemsSource = new[] { "30 seconds", "1 minute", "2 minutes", "3 minutes", "5 minutes", "Unlimited" }, SelectedIndex = 1, Margin = new(0, 4, 0, 10) };
        var group = new TextBox { MaxLength = 120, Margin = new(0, 4, 0, 10) };
        var notes = new TextBox { MaxLength = 4000, Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 4, 0, 10) };
        foreach (var (label, input) in new (string, UIElement)[] { ("Attempt name", name), ("Duration", duration), ("Group (optional)", group), ("Notes (optional)", notes) }) {
            panel.Children.Add(new Label { Content = label, Target = input, Padding = new(0) }); panel.Children.Add(input);
        }
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Brush("#EF8792") };
        panel.Children.Add(error);
        panel.Children.Add(Button("Start training", () => {
            try {
                if (capture == null) throw new InvalidOperationException("Start capture before starting a training attempt.");
                int[] lengths = [30, 60, 120, 180, 300, 0];
                engine.StartTraining(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), lengths[duration.SelectedIndex], name.Text, group.Text, notes.Text);
                notice = "Training started. Only your damage counts; the timer includes pauses.";
                window.Close(); RefreshTraining(); Refresh();
            } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { error.Text = ex.Message; }
        }));
        window.Content = panel; return window;
    }
}
