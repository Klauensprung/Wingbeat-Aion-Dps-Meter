using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    readonly ArchiveLoader archiveLoader=new();
    readonly TextBlock archiveLoadingStatus=new(){Margin=new(8),TextWrapping=TextWrapping.Wrap};
    CancellationTokenSource? archiveLoading;
    readonly HashSet<string> archiveUpdatedDuringLoad=new();
    async void LoadArchiveInBackground() {
        archiveLoading?.Cancel();
        var request=new CancellationTokenSource();archiveLoading=request;
        archiveUpdatedDuringLoad.Clear();
        archiveLoadingStatus.Text="Loading saved fights in the background. Live capture is independent.";
        int loaded=0,skipped=0;
        try {
            await archiveLoader.LoadAsync(store.Files,store.Load,(batch,invalid)=>Dispatcher.InvokeAsync(()=>{
                if(shuttingDown||request.IsCancellationRequested)return;
                foreach(var fight in batch){
                    if(archiveUpdatedDuringLoad.Contains(fight.Id))continue;
                    archive.RemoveAll(f=>f.Id==fight.Id);archive.Add(fight);
                }
                loaded+=batch.Length;skipped=invalid;archiveChanged=true;
                archiveLoadingStatus.Text=$"Loading saved fights: {loaded:N0} loaded"+(skipped>0?$", {skipped:N0} unreadable files skipped.":".");
            },DispatcherPriority.Background).Task,request.Token);
            if(!shuttingDown&&!request.IsCancellationRequested){
                archiveLoadingStatus.Text=$"{loaded:N0} saved fights loaded."+(skipped>0?$" Skipped {skipped:N0} unreadable files; originals retained.":"");
                LocalLog.Write("Background archive loaded: "+loaded+" fights; skipped "+skipped);
            }
        }catch(OperationCanceledException) { }
        catch(Exception ex){if(!shuttingDown&&!request.IsCancellationRequested){archiveLoadingStatus.Text="Saved fights could not finish loading: "+ex.Message;LocalLog.Write(archiveLoadingStatus.Text);}}
        finally{if(ReferenceEquals(archiveLoading,request))archiveLoading=null;request.Dispose();}
    }
}
