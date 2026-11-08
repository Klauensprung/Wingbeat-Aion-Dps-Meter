using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AionDpsMeter.Models;
namespace AionDpsMeter.Local;

internal sealed record ShowcasePlayer(string Name,string Job,int Dps,int Hp,int[] Skills);
internal sealed record ShowcaseData(string Description,string Boss,int DurationSeconds,ShowcasePlayer[] Players)
{
    internal static ShowcaseData Load(){using var stream=typeof(ShowcaseData).Assembly.GetManifestResourceStream("Wingbeat.Showcase.json")!;return JsonSerializer.Deserialize<ShowcaseData>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;}
    internal Fight Fight(int variation=0){
        var time=new DateTimeOffset(2026,11,5,19,0,0,TimeSpan.Zero).AddDays(variation/3).AddMinutes(variation%3*12);
        long start=time.ToUnixTimeMilliseconds();
        var fight=new Fight{Demo=variation==0,LocalPlayer=1,PartyKnown=true,Label=Boss,Created=time.UtcDateTime};
        fight.Actors[100]=new(100,Boss,false,false,0,"Unknown",0){NpcId=990001,CapturedBoss=true};
        for(int i=0;i<Players.Length;i++){
            var player=Players[i];fight.Actors[i+1]=new(i+1,player.Name,true,true,18500+i*1200,player.Job,1);
            for(int second=0;second<=DurationSeconds;second++){
                int skill=player.Skills[(second/3)%player.Skills.Length];
                fight.Damage.Add(new(){ActorId=i+1,TargetId=100,Timestamp=start+second*1000,SkillCode=skill,Damage=(long)(player.Dps*(.45+.8*Math.Pow(Math.Sin(second*.075+1),2)+(second<12?.35:0))*(second is >37 and <44 or >71 and <76?.18:1)*(1+variation*.025)),IsCritical=second%4==0,IsBackAttack=second%3==0});
                if(i==0&&second%3==0)fight.Healing.Add(new(){ActorId=1,TargetId=2,Timestamp=start+second*1000,SkillCode=18010340,Damage=2400,IsHeal=true});
            }
            fight.Network.Add(new("hp",start+DurationSeconds*1000,"synthetic",JsonSerializer.SerializeToElement(new{EntityId=i+1,CurrentHp=player.Hp*100,MaximumHp=10000})));
        }
        fight.Deaths.Add(new(100,start+DurationSeconds*1000));return fight;
    }
}
internal static class ReadmeShowcase
{
    internal static int Run(Application app){
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");MainWindow? main=null;
        try{Directory.CreateDirectory("readme-showcase");main=new MainWindow(true){Left=-32000,Top=-32000,ShowActivated=false};main.ExportReadmeShowcase();return 0;}
        catch(Exception ex){File.WriteAllText("readme-showcase-error.txt",ex.ToString());return 1;}
        finally{main?.Close();}
    }
    internal static void Save(FrameworkElement element,string name,Brush? background=null){
        element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);if(background==null)image.Render(element);
        else{var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){var bounds=new Rect(0,0,element.ActualWidth,element.ActualHeight);drawing.DrawRectangle(background,null,bounds);drawing.DrawRectangle(new VisualBrush(element){Stretch=Stretch.Fill},null,bounds);}image.Render(visual);}
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine("readme-showcase",name));png.Save(file);
    }
    internal static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    internal static void Hero(Dictionary<string,Window> windows){
        var canvas=new Canvas{Width=1600,Height=1120,Background=new LinearGradientBrush(Color.FromRgb(12,20,31),Color.FromRgb(21,39,47),35)};
        void Text(string text,double x,double y,double size,string color,bool bold=false){var label=new TextBlock{Text=text,FontSize=size,Foreground=UiTheme.Brush(color),FontFamily=new("Segoe UI"),FontWeight=bold?FontWeights.Bold:FontWeights.Normal};Canvas.SetLeft(label,x);Canvas.SetTop(label,y);canvas.Children.Add(label);}
        Text("WINGBEAT  /  AION 2",56,35,18,"#63DDC1",true);
        Text("Know your damage.",52,75,58,"#F3F6FF",true);Text("Keep your party alive.",52,140,58,"#F3F6FF",true);
        Text("DPS + party health  /  cooldowns + voice alerts  /  boss warnings",58,220,21,"#ADBFD0");
        void Card(string key,string title,double x,double y,double width,double height){
            var card=new Border{Width=width,Height=height,CornerRadius=new(14),Background=UiTheme.Brush("#C0111B29"),BorderBrush=UiTheme.Brush("#34465A"),BorderThickness=new(1)};
            Canvas.SetLeft(card,x);Canvas.SetTop(card,y);canvas.Children.Add(card);
            Text(title,x+18,y+15,14,"#63DDC1",true);
            var visual=windows[key];visual.UpdateLayout();
            var view=new Border{Width=width-28,Height=height-58,Background=new VisualBrush(visual){Stretch=Stretch.Uniform,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Top}};
            Canvas.SetLeft(view,x+14);Canvas.SetTop(view,y+48);canvas.Children.Add(view);
        }
        Card("damage","LIVE DAMAGE",56,285,475,480);Card("health","PARTY HEALTH",551,285,475,480);
        Card("cooldowns","COOLDOWNS",1046,285,498,170);Card("boss","BOSS WARNINGS",1046,475,498,290);
        Card("farming","MATERIALS",56,785,475,230);Card("pets","PET SOULS",551,785,475,230);Card("enemies","NEARBY PLAYERS",1046,785,498,230);
        Text("Free. Local. No account. Your layout.",58,1050,23,"#D8E6EF",true);
        Text("Actual Wingbeat UI / fictional showcase data",1110,1064,14,"#94A9BC");
        canvas.Measure(new Size(1600,1120));canvas.Arrange(new Rect(0,0,1600,1120));Save(canvas,"wingbeat-hero.png");
    }
}
public sealed partial class MainWindow
{
    internal void ExportReadmeShowcase(){
        Show();timer.Stop();var data=ShowcaseData.Load();var fight=data.Fight();viewed=fight;
        archive.Clear();archive.AddRange(Enumerable.Range(0,8).Select(i=>FightArchiveSummary.Create(data.Fight(i+1))));archiveChanged=true;
        Width=1400;Height=940;NavigateTo("Encounter");Refresh();ReadmeShowcase.Pump();UpdateLayout();ReadmeShowcase.Save((FrameworkElement)Content,"encounter.png",Background);
        NavigateTo("Encounter log");Refresh();UpdateLayout();ReadmeShowcase.Save((FrameworkElement)Content,"encounter-log.png",Background);
        var opened=new Dictionary<string,Window>();
        void Add(string key,Window window){window.Left=-32000;window.Top=-32000;window.ShowActivated=false;OverlayHideControls.SetupPreview(window);window.Show();window.UpdateLayout();opened.Add(key,window);}
        try{
            var damage=new OverlayWindow(skillName:id=>skills.GetSkillName(id),skillIcons:skillIcons);damage.Update(fight,new(){OverlayScope="Party",OverlayWidth=447,OverlayHeight=400,OverlayCompact=true});Add("damage",damage);
            var health=new OverlayWindow(){MeterLabel="DPS2"};health.PartyRecovery=()=>new(){[3]=1300,[4]=1700};health.PartyDebuffs=()=>[new(4,12000101,"Poison",12)];health.PartyDistances=_=>new(){[2]="850 units away",[3]="1,200 units away"};health.Update(fight,new(){OverlayPartyHealth=true,OverlayWidth=447,OverlayHeight=400,OverlayCompact=true});Add("health",health);
            var cooldowns=new CooldownTracker();var p=new Preferences{CooldownSkills=[18040000,18020000,18050000,18010000],CooldownStyle="Square",CooldownWidth=450,CooldownHeight=95};long tick=Environment.TickCount64;
            for(int i=0;i<p.CooldownSkills.Length;i++)cooldowns.Capture(new("skill-cooldown",tick,"synthetic",JsonSerializer.SerializeToElement(new{SkillCode=p.CooldownSkills[i],RemainingMs=6000+i*7000})),1);
            Add("cooldowns",new CooldownOverlayWindow(p,cooldowns,skills,skillIcons,()=>true,()=>{},()=>{}));
            var boss=new BossAlertOverlayWindow(new(),()=>{});long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();boss.Update([new(100,data.Boss,18040120,"Casting",now,now+8000,10000,true)],now,_=>"Heavy strike");Add("boss",boss);
            Add("farming",CreateOverlaySample("Farming overlay"));Add("pets",CreateOverlaySample("Pet farming"));Add("enemies",CreateOverlaySample("Nearby enemies"));
            opened["enemies"].Height=180;opened["enemies"].Width=450;
            ReadmeShowcase.Pump();
            foreach(var (key,window) in opened){window.UpdateLayout();ReadmeShowcase.Save(window,key+"-overlay.png");}
            ReadmeShowcase.Hero(opened);
            ShowOverlayPlacementSetup();placementSetup!.Left=-32000;placementSetup.Top=-32000;placementSetup.UpdateLayout();ReadmeShowcase.Save(placementSetup,"overlay-setup.png");placementSetup.Close();
        }finally{foreach(var window in opened.Values)window.Close();}
    }
}
