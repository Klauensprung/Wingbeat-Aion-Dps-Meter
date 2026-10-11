using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal static class UiTheme
{
    public static ContextMenu Menu(Window owner, UIElement? target = null) => new() {
        Resources = owner.Resources, PlacementTarget = target,
        Background = (Brush)owner.Resources["Panel"], Foreground = (Brush)owner.Resources["Text"]
    };
    public static SolidColorBrush Brush(string value) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush; }
    public static string Background(string theme) => theme switch { "Slate" => "#192330", "Forest" => "#11251F", "Ember" => "#271C1B", "Violet" => "#211A2D", _ => "#10151F" };
    public static void Apply(Window window, Preferences preferences)
    {
        window.Background = Brush(Background(preferences.Theme)); window.Foreground = Brush("#E7EDF7");
        var resources = window.Resources;
        resources["Panel"] = Brush("#171F2C"); resources["Raised"] = Brush("#222D3E"); resources["Edge"] = Brush("#2B374A");
        resources["Text"] = Brush("#E7EDF7"); resources["Muted"] = Brush("#93A2B8"); resources["Accent"] = Brush(preferences.Accent);
        resources[SystemColors.WindowBrushKey] = resources["Panel"]; resources[SystemColors.WindowTextBrushKey] = resources["Text"];
        resources[SystemColors.ControlBrushKey] = resources["Raised"]; resources[SystemColors.ControlTextBrushKey] = resources["Text"];
        resources[SystemColors.HighlightBrushKey] = Brush("#33465C");
        resources[typeof(Button)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Raised}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource Edge}"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Padding" Value="12,8"/><Setter Property="FontSize" Value="12"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="5" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border><ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
              </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(TextBox)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TextBox">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Panel}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource Edge}"/><Setter Property="Padding" Value="8,7"/>
            <Setter Property="CaretBrush" Value="{DynamicResource Accent}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox">
              <Border x:Name="Outline" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="4">
                <Grid><ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"/>
                  <TextBlock x:Name="Hint" Text="{TemplateBinding Tag}" Foreground="{DynamicResource Muted}" Margin="{TemplateBinding Padding}" IsHitTestVisible="False" Visibility="Collapsed"/>
                </Grid>
              </Border><ControlTemplate.Triggers>
                <Trigger Property="Text" Value=""><Setter TargetName="Hint" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Outline" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
              </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(Expander)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Expander">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Expander">
              <StackPanel>
                <ToggleButton Content="{TemplateBinding Header}" Foreground="{TemplateBinding Foreground}" HorizontalContentAlignment="Left"
                              IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="Header" BorderBrush="Transparent" BorderThickness="1" CornerRadius="4" Padding="3,5">
                      <DockPanel><Path x:Name="Arrow" DockPanel.Dock="Left" Data="M 1 0 L 5 4 L 1 8" Stroke="{DynamicResource Muted}" StrokeThickness="1.4" Width="10" Margin="0,0,7,0" VerticalAlignment="Center"/>
                        <ContentPresenter RecognizesAccessKey="True" VerticalAlignment="Center"/>
                      </DockPanel>
                    </Border><ControlTemplate.Triggers>
                      <Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="Data" Value="M 0 2 L 4 6 L 8 2"/></Trigger>
                      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Header" Property="Background" Value="{DynamicResource Raised}"/></Trigger>
                      <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Header" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter x:Name="Body" ContentSource="Content" Margin="0,5,0,0" Visibility="Collapsed"/>
              </StackPanel><ControlTemplate.Triggers>
                <Trigger Property="IsExpanded" Value="True"><Setter TargetName="Body" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(TabItem)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TabItem">
            <Setter Property="Foreground" Value="{DynamicResource Muted}"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="VerticalContentAlignment" Value="Stretch"/><Setter Property="Width" Value="146"/>
            <Setter Property="FontSize" Value="12"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem">
              <StackPanel>
                <TextBlock x:Name="Group" Text="{TemplateBinding Tag}" Foreground="{DynamicResource Muted}" FontWeight="Normal" FontSize="9" Margin="12,6,0,2"/>
                <Border x:Name="Row" Background="{TemplateBinding Background}" BorderBrush="Transparent" BorderThickness="1" CornerRadius="6" Margin="0,1,8,1">
                  <Grid>
                    <Border x:Name="Active" Background="{DynamicResource Accent}" Width="3" Height="16" CornerRadius="1" HorizontalAlignment="Left" Visibility="Hidden"/>
                    <ContentPresenter ContentSource="Header" HorizontalAlignment="Left" Margin="12,6"/>
                  </Grid>
                </Border>
              </StackPanel><ControlTemplate.Triggers>
                <Trigger Property="Tag" Value="{x:Null}"><Setter TargetName="Group" Property="Visibility" Value="Collapsed"/></Trigger>
                <Trigger Property="IsSelected" Value="True"><Setter Property="Foreground" Value="{DynamicResource Accent}"/><Setter Property="Background" Value="{DynamicResource Raised}"/><Setter Property="FontWeight" Value="SemiBold"/><Setter TargetName="Active" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Row" Property="Background" Value="{DynamicResource Raised}"/></Trigger>
                <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Row" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
              </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(TabControl)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabControl">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabControl">
              <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="158"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                  <StackPanel IsItemsHost="True" KeyboardNavigation.TabNavigation="Once"/>
                </ScrollViewer>
                <ContentPresenter Grid.Column="1" ContentSource="SelectedContent" Margin="{TemplateBinding Padding}"/>
              </Grid>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(ScrollBar)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
            <Setter Property="Width" Value="10"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
              <Track x:Name="PART_Track" IsDirectionReversed="True">
                <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#39475C" CornerRadius="3" Margin="2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
              </Track>
            </ControlTemplate></Setter.Value></Setter>
            <Style.Triggers><Trigger Property="Orientation" Value="Horizontal">
              <Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="10"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
                <Track x:Name="PART_Track" IsDirectionReversed="False">
                  <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageLeftCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                  <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#39475C" CornerRadius="3" Margin="2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                  <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageRightCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
                </Track>
              </ControlTemplate></Setter.Value></Setter>
            </Trigger></Style.Triggers>
          </Style>
          """);
        resources[typeof(ComboBox)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Panel}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource Edge}"/><Setter Property="Padding" Value="9,7"/>
            <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
            <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
              <Grid>
                <ToggleButton x:Name="Toggle" Focusable="False" ClickMode="Press" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
                    <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="4">
                      <Path Data="M 0 0 L 4 4 L 8 0" Stroke="{DynamicResource Muted}" StrokeThickness="1.5" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0"/>
                    </Border><ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                      <Trigger Property="IsChecked" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <ContentPresenter Margin="9,7,30,7" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" IsHitTestVisible="False" VerticalAlignment="Center"/>
                <Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
                  <Border MinWidth="{TemplateBinding ActualWidth}" MaxHeight="{TemplateBinding MaxDropDownHeight}" Background="{DynamicResource Panel}" BorderBrush="{DynamicResource Edge}" BorderThickness="1" CornerRadius="4" Padding="4" Margin="0,3,0,0">
                    <ScrollViewer CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                  </Border>
                </Popup>
              </Grid><ControlTemplate.Triggers>
                <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Toggle" Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(ComboBoxItem)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBoxItem">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
              <Border Background="{TemplateBinding Background}" CornerRadius="3" Padding="9,7"><ContentPresenter/></Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="{DynamicResource Raised}"/><Setter Property="Foreground" Value="{DynamicResource Accent}"/></Trigger>
                <Trigger Property="IsHighlighted" Value="True"><Setter Property="Background" Value="#33465C"/><Setter Property="Foreground" Value="{DynamicResource Text}"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(ContextMenu)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="{DynamicResource Panel}"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu">
              <Border Background="{TemplateBinding Background}" BorderBrush="{DynamicResource Edge}" BorderThickness="1" CornerRadius="5" Padding="5">
                <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
              </Border>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(MenuItem)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="MenuItem">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem">
              <Border Background="{TemplateBinding Background}" CornerRadius="3" Padding="8,8">
                <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="22"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                  <TextBlock x:Name="Check" Text="✓" Foreground="{DynamicResource Accent}" Visibility="Hidden"/>
                  <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" Margin="0,0,12,0"/>
                  <TextBlock Grid.Column="2" Text="{TemplateBinding InputGestureText}" Foreground="{DynamicResource Muted}"/>
                </Grid>
              </Border><ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="Check" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="IsHighlighted" Value="True"><Setter Property="Background" Value="#33465C"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(Separator)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Separator">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator"><Border Background="{DynamicResource Edge}" Height="1" Margin="8,5"/></ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[MenuItem.SeparatorStyleKey] = resources[typeof(Separator)];
        resources[typeof(DataGrid)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="DataGrid">
            <Setter Property="Background" Value="#121B28"/><Setter Property="Foreground" Value="{DynamicResource Text}"/>
            <Setter Property="RowBackground" Value="#182231"/><Setter Property="AlternatingRowBackground" Value="#121B28"/>
            <Setter Property="BorderBrush" Value="{DynamicResource Edge}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="GridLinesVisibility" Value="None"/><Setter Property="RowHeight" Value="38"/>
            <Setter Property="ColumnHeaderHeight" Value="40"/><Setter Property="FontSize" Value="12"/>
            <Setter Property="SelectionUnit" Value="FullRow"/>
            <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Visible"/>
            <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Visible"/>
            <Setter Property="VirtualizingPanel.ScrollUnit" Value="Pixel"/>
            <Setter Property="VirtualizingPanel.VirtualizationMode" Value="Recycling"/>
          </Style>
          """);
        resources[typeof(DataGridColumnHeader)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="DataGridColumnHeader">
            <Setter Property="Background" Value="#1C2939"/><Setter Property="Foreground" Value="{DynamicResource Muted}"/>
            <Setter Property="Padding" Value="10,8"/><Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="DataGridColumnHeader">
              <Grid>
                <Border Background="{TemplateBinding Background}" BorderBrush="{DynamicResource Edge}" BorderThickness="0,0,0,1" Padding="{TemplateBinding Padding}">
                  <DockPanel>
                    <Path x:Name="SortArrow" DockPanel.Dock="Right" Margin="6,0,0,0" VerticalAlignment="Center" Fill="{DynamicResource Accent}" Visibility="Hidden" Data="M 0 5 L 4 0 L 8 5 Z"/>
                    <ContentPresenter VerticalAlignment="Center"/>
                  </DockPanel>
                </Border>
                <Thumb x:Name="PART_LeftHeaderGripper" Width="5" HorizontalAlignment="Left" Cursor="SizeWE"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="Transparent"/></ControlTemplate></Thumb.Template></Thumb>
                <Thumb x:Name="PART_RightHeaderGripper" Width="5" HorizontalAlignment="Right" Cursor="SizeWE"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="Transparent"/></ControlTemplate></Thumb.Template></Thumb>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="SortDirection" Value="Ascending"><Setter TargetName="SortArrow" Property="Visibility" Value="Visible"/></Trigger>
                <Trigger Property="SortDirection" Value="Descending"><Setter TargetName="SortArrow" Property="Visibility" Value="Visible"/><Setter TargetName="SortArrow" Property="Data" Value="M 0 0 L 4 5 L 8 0 Z"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#25384B"/><Setter Property="Foreground" Value="{DynamicResource Text}"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        resources[typeof(DataGridRow)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="DataGridRow">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/>
            <Style.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#25384B"/></Trigger>
              <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#29495A"/><Setter Property="Foreground" Value="White"/></Trigger>
            </Style.Triggers>
          </Style>
          """);
        resources[typeof(DataGridCell)] = XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="DataGridCell">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderBrush" Value="Transparent"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="9,4"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="DataGridCell">
              <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}" ClipToBounds="True">
                <ContentPresenter VerticalAlignment="Center"/>
              </Border>
            </ControlTemplate></Setter.Value></Setter>
            <Style.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter Property="BorderBrush" Value="{DynamicResource Accent}"/></Trigger></Style.Triggers>
          </Style>
          """);
    }
}
