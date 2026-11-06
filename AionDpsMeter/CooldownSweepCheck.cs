using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AionDpsMeter.Local;

internal static class CooldownSweepCheck
{
    internal static int Run(Application app){
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try{
            var names=new SkillNames();var icons=new SkillIcons();
            foreach(string style in new[]{"Bars","Round","Square"}){
                var tracker=new CooldownTracker();long now=Environment.TickCount64;
                var settings=new Preferences{CooldownSkills=[17100000],CooldownStyle=style,CooldownOneSecondEarly=false,CooldownShowReady=true};
                var window=new CooldownOverlayWindow(settings,tracker,names,icons,()=>true,()=>{},()=>{}){
                    Left=-32000,Top=-32000,ShowActivated=false};
                try{
                    if(window.VisibleSkills!=0)throw new Exception("Unused skill is visible before its first cooldown.");
                    tracker.Capture(new("skill-cooldown",now,"test",JsonSerializer.SerializeToElement(new{SkillCode=17100000,RemainingMs=30000})),1);
                    window.Show();window.UpdateLayout();window.RefreshAt(now);window.CheckSweep(17100000,false,1);
                    if(window.VisibleSkills!=1)throw new Exception("Observed cooldown remained hidden.");
                    window.RefreshAt(now+15000);window.CheckSweep(17100000,false,.5);
                    var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using(var file=File.Create("cooldown-sweep-"+style+".png"))encoder.Save(file);
                    window.RefreshAt(now+29900);window.CheckSweep(17100000,false,1d/300);
                    window.RefreshAt(now+30000);window.CheckSweep(17100000,true,1);
                    window.RefreshAt(now+31000);window.CheckSweep(17100000,true,1);
                }finally{window.Close();}
            }
            File.WriteAllText("cooldown-sweep-results.txt","PASS cooldown bars drain; icon sweeps charge empty -> half -> full and remain full when ready.");return 0;
        }catch(Exception ex){File.WriteAllText("cooldown-sweep-error.txt",ex.ToString());return 1;}
    }
}
