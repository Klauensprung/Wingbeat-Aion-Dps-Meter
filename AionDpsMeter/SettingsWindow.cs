using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace AionDpsMeter.Local;

internal sealed class SettingsWindow : Window
{
    sealed record Page(Button Button, FrameworkElement Content, string Description);
    readonly Dictionary<string, Page> pages = new();
    readonly StackPanel navigation = new();
    readonly TextBlock heading = new() { FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) };
    readonly TextBlock description = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 18), FontSize = 12 };
    readonly TextBlock feedback = new() { Text = "Switches save immediately. Use Apply for text and colour changes.", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 20, 0) };
    readonly ContentControl body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly ScrollViewer scroll;
    bool closing;
    internal string? SelectedPage { get; private set; }

    internal SettingsWindow(Preferences settings)
    {
        Title = "Wingbeat settings"; Width = 830; Height = 690; MinWidth = 730; MinHeight = 520;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Segoe UI"); FontSize = 12; UseLayoutRounding = true; ApplyTheme(settings);
        var root = new DockPanel { Margin = new(20) }; Content = root;
        var header = new StackPanel { Margin = new(4, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = "Make Wingbeat yours", FontSize = 23, FontWeight = FontWeights.SemiBold });
        var hint = new TextBlock { Text = "Your setup, overlays and appearance. Saved on this PC.", Margin = new(0, 6, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); header.Children.Add(hint);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        var done = new Button { Padding = new(15, 8, 15, 8), HorizontalAlignment = HorizontalAlignment.Right };
        UiIcons.SetLabel(done, UiIcon.Close, "Done"); done.Click += (_, _) => Hide(); DockPanel.SetDock(done, Dock.Right); footer.Children.Add(done);
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); footer.Children.Add(feedback);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new GridLength(165) }); layout.ColumnDefinitions.Add(new()); root.Children.Add(layout);
        var nav = new ScrollViewer { Content = navigation, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(0, 0, 16, 0) }; layout.Children.Add(nav);
        var content = new StackPanel { Margin = new(4, 0, 8, 0) }; content.Children.Add(heading); content.Children.Add(description); content.Children.Add(body);
        description.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1); layout.Children.Add(scroll);
        Closing += (_, e) => { if (!closing) { e.Cancel = true; Hide(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Hide(); } };
    }
    internal void AddPage(string title, UiIcon icon, string help, FrameworkElement content)
    {
        var button = new Button { Margin = new(0, 0, 0, 6), Padding = new(12, 10, 12, 10), HorizontalContentAlignment = HorizontalAlignment.Left };
        UiIcons.SetLabel(button, icon, title); button.Click += (_, _) => SelectPage(title);
        navigation.Children.Add(button); pages.Add(title, new(button, content, help));
        if (SelectedPage == null) SelectPage(title);
    }
    internal void SelectPage(string title)
    {
        if (!pages.TryGetValue(title, out var page)) return;
        SelectedPage = title; heading.Text = title; description.Text = page.Description; body.Content = page.Content;
        feedback.Text = "Switches save immediately. Use Apply for text and colour changes.";
        foreach (var entry in pages) {
            bool selected = entry.Key == title;
            entry.Value.Button.SetResourceReference(Control.ForegroundProperty, selected ? "Accent" : "Muted");
            entry.Value.Button.SetResourceReference(Control.BackgroundProperty, selected ? "Raised" : "Panel");
            entry.Value.Button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Panel");
        }
        scroll.ScrollToTop();
    }
    internal void Shutdown() { closing = true; Close(); }
    internal void ShowStatus(string text) => feedback.Text = text;
    internal void ApplyTheme(Preferences settings)
    {
        UiTheme.Apply(this, settings);
        Resources[typeof(CheckBox)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox">
              <Border x:Name="Focus" BorderBrush="Transparent" BorderThickness="1" CornerRadius="5" Padding="0,5" Background="Transparent">
                <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <ContentPresenter VerticalAlignment="Center" Margin="0,0,14,0">
                    <ContentPresenter.ContentTemplate><DataTemplate><TextBlock Text="{Binding}" TextWrapping="Wrap"/></DataTemplate></ContentPresenter.ContentTemplate>
                  </ContentPresenter>
                  <Border x:Name="Track" Grid.Column="1" Width="34" Height="20" CornerRadius="10" Background="{DynamicResource Edge}" VerticalAlignment="Center">
                    <Ellipse x:Name="Knob" Width="14" Height="14" Fill="{DynamicResource Muted}" Margin="3" HorizontalAlignment="Left"/>
                  </Border>
                </Grid>
              </Border><ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="Track" Property="Background" Value="{DynamicResource Accent}"/><Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/><Setter TargetName="Knob" Property="Fill" Value="#10151F"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Focus" Property="Background" Value="{DynamicResource Raised}"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Focus" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
    }
}
