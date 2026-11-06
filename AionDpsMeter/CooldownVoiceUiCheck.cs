using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AionDpsMeter.Local;

internal static class CooldownVoiceUiCheck
{
    internal static int Run(Application app)
    {
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try{
            var settings=new Preferences{CooldownSkills=[11010000,17010000],CooldownVoiceSkills=[11010000]};
            var names=new SkillNames();bool saved=false;
            var window=new CooldownSettingsWindow(settings,names,settings.CooldownSkills,()=>saved=true,()=>{}){
                Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,ShowActivated=false};
            try{
                window.Show();window.UpdateLayout();window.Dispatcher.Invoke(()=>{},DispatcherPriority.Loaded);window.UpdateLayout();
                var voice=Descendants(window).OfType<CheckBox>().Single(c=>c.Content?.ToString()==Localization.T("Say this skill when ready"));
                if(!voice.IsEnabled||voice.IsChecked!=true)throw new InvalidOperationException("Selected skill's saved voice choice was not displayed.");
                var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                using(var file=File.Create("cooldown-voice-settings.png"))encoder.Save(file);
                voice.IsChecked=false;voice.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Descendants(window).OfType<Button>().Single(b=>b.Content?.ToString()==Localization.T("Save")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                if(!saved||settings.CooldownVoiceSkills.Length!=0||settings.CooldownSkills.Length!=2)throw new InvalidOperationException("Voice toggle did not save independently of tracked skills.");
            }finally{window.Close();}
            var clip=CooldownVoicePack.Clip(CooldownSkillChoice.Resolve(11010000,names),"en")!;
            string path=CooldownVoicePack.Extract(clip)??throw new InvalidOperationException("Voice clip could not be extracted.");
            var player=new MediaPlayer{Volume=0};var frame=new DispatcherFrame();Exception? failure=null;bool opened=false,ended=false;
            var timeout=new DispatcherTimer{Interval=TimeSpan.FromSeconds(12)};
            timeout.Tick+=(_,_)=>{failure=new TimeoutException("Voice playback check timed out.");frame.Continue=false;};
            player.MediaOpened+=(_,_)=>opened=true;
            player.MediaEnded+=(_,_)=>{ended=true;frame.Continue=false;};
            player.MediaFailed+=(_,e)=>{failure=e.ErrorException;frame.Continue=false;};
            try{timeout.Start();player.Open(new Uri(path));player.Play();Dispatcher.PushFrame(frame);}
            finally{timeout.Stop();player.Close();}
            if(failure!=null)throw failure;
            if(!opened||!ended)throw new InvalidOperationException("Voice playback did not complete.");
            File.WriteAllText("cooldown-voice-ui-results.txt","PASS German voice controls, saved per-skill opt-out, unchanged tracked skills, lazy clip extraction and muted MP3 playback through Windows MediaPlayer.");return 0;
        }catch(Exception ex){File.WriteAllText("cooldown-voice-ui-error.txt",ex.ToString());return 1;}
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent){
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){
            var child=VisualTreeHelper.GetChild(parent,i);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
}
