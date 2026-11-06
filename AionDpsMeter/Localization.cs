using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AionDpsMeter.Local;

internal static class Localization
{
    static Dictionary<string,string> german=new(StringComparer.Ordinal);
    internal static string GameLanguage { get; private set; }="en";
    internal static string Preference { get; private set; }="auto";
    internal static string Language { get; private set; }="en";
    internal static string Resolve(string? preference,CultureInfo windows)=>preference is "de" or "en"?preference:windows.TwoLetterISOLanguageName=="de"?"de":"en";
    internal static void Initialize(bool isolated,string? checkLanguage=null)
    {
        string? choice=null;
        if(!isolated)try{
            using var json=JsonDocument.Parse(File.ReadAllText(AppDataPaths.FilePath("preferences.json")));
            if(json.RootElement.TryGetProperty("Language",out var value))choice=value.GetString();
            if(json.RootElement.TryGetProperty("GameLanguage",out var game))GameLanguage=game.GetString()=="de"?"de":"en";
        }catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){ }
        Preference=choice is "de" or "en"?choice:"auto";
        Language=isolated?Resolve(checkLanguage??"en",CultureInfo.CurrentUICulture):Resolve(Preference,CultureInfo.CurrentUICulture);
        using var stream=typeof(Localization).Assembly.GetManifestResourceStream("Wingbeat.Language.de.json");
        if(stream!=null)german=JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??new();
    }
    internal static string T(string text)=>Language=="de"&&german.TryGetValue(text,out var translated)?translated:text;
    internal static string F(FormattableString text)=>string.Format(Language=="de"?CultureInfo.GetCultureInfo("de-DE"):CultureInfo.CurrentCulture,T(text.Format),text.GetArguments());

    // Translate headers at presentation time: tabs and menu modes retain their
    // original keys for selection, persistence and comparisons.
    internal static void InstallPresentation()
    {
        EventManager.RegisterClassHandler(typeof(HeaderedContentControl),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            if(sender is HeaderedContentControl c&&c.Header is string&&c.HeaderTemplate==null)c.HeaderTemplate=HeaderTemplate();
        }));
        EventManager.RegisterClassHandler(typeof(HeaderedItemsControl),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            if(sender is HeaderedItemsControl c&&c.Header is string&&c.HeaderTemplate==null)c.HeaderTemplate=HeaderTemplate();
        }));
        EventManager.RegisterClassHandler(typeof(ComboBox),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            // DisplayMemberPath and an explicit item template are mutually exclusive.
            // Localize string choices without replacing the selected/persisted value.
            if(sender is ComboBox c&&string.IsNullOrEmpty(c.DisplayMemberPath)&&c.ItemTemplate==null&&c.ItemTemplateSelector==null)
                c.ItemTemplateSelector=TextTemplates;
        }));
        EventManager.RegisterClassHandler(typeof(TextBox),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            if(sender is TextBox c&&c.Template?.FindName("Hint",c) is TextBlock hint)
                hint.SetBinding(TextBlock.TextProperty,new Binding("Tag"){Source=c,Converter=Converter});
        }));
        EventManager.RegisterClassHandler(typeof(TabItem),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{
            if(sender is TabItem c&&c.Template?.FindName("Group",c) is TextBlock group)
                group.SetBinding(TextBlock.TextProperty,new Binding("Tag"){Source=c,Converter=Converter});
        }));
    }
    internal static readonly IValueConverter Converter=new TranslationConverter();
    internal static readonly DataTemplateSelector TextTemplates=new StringTemplateSelector();
    sealed class StringTemplateSelector:DataTemplateSelector
    {
        readonly DataTemplate template=HeaderTemplate();
        public override DataTemplate? SelectTemplate(object item,DependencyObject container)=>item is string?template:null;
    }
    internal static DataTemplate HeaderTemplate()
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        text.SetBinding(TextBlock.TextProperty,new Binding{Converter=Converter});
        return new DataTemplate{VisualTree=text};
    }
    sealed class TranslationConverter:IValueConverter
    {
        public object Convert(object value,Type type,object parameter,CultureInfo culture)=>value is string text?T(text):value;
        public object ConvertBack(object value,Type type,object parameter,CultureInfo culture)=>Binding.DoNothing;
    }
}
