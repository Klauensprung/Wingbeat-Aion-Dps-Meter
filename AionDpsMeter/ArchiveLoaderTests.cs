using System.IO;
namespace AionDpsMeter.Local;
internal static class ArchiveLoaderTests
{
    internal static void Run(Action<bool,string> check)=>RunAsync(check).GetAwaiter().GetResult();
    static async Task RunAsync(Action<bool,string> check) {
        var loader=new ArchiveLoader();
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        int caller=Environment.CurrentManagedThreadId,worker=caller;
        var raw=SelfTest.Demo();int loaded=0,skipped=0,maxBatch=0;bool compact=true;
        var task=loader.LoadAsync(()=>Enumerable.Range(0,34).Select(i=>i.ToString()),path=>{
            worker=Environment.CurrentManagedThreadId;
            if(path=="0"){entered.Set();if(!release.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException();}
            if(path=="17")throw new InvalidDataException();return raw;
        },(batch,invalid)=>{loaded+=batch.Length;skipped=invalid;maxBatch=Math.Max(maxBatch,batch.Length);compact&=batch.All(f=>f.ArchiveSummary!=null&&f.Damage.Count==0&&f.Network.Count==0);return Task.CompletedTask;},CancellationToken.None);
        try {check(entered.Wait(TimeSpan.FromSeconds(5))&&!task.IsCompleted&&worker!=caller,"Archive reads run off the caller thread without blocking startup");}
        finally{release.Set();}
        await task.WaitAsync(TimeSpan.FromSeconds(15));
        check(loaded==33&&skipped==1,"Background archive retains valid fights and skips a damaged file");
        check(compact&&maxBatch<=16,"Archive publication is bounded and contains summaries rather than raw events");
        using var cancel=new CancellationTokenSource();int publications=0;
        var blocked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=loader.LoadAsync(()=>Enumerable.Range(0,48).Select(i=>i.ToString()),_=>raw,async (_,_)=>{publications++;blocked.TrySetResult();await Task.Delay(Timeout.Infinite,cancel.Token);},cancel.Token);
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));cancel.Cancel();
        bool canceled=false;try{await pending;}catch(OperationCanceledException){canceled=true;}
        check(canceled&&publications==1,"Canceled archive load stops before publishing more batches");
        int restarted=0;
        await loader.LoadAsync(()=>new[]{"one"},_=>raw,(batch,_)=>{restarted+=batch.Length;return Task.CompletedTask;},CancellationToken.None);
        check(restarted==1,"Archive loader can restart after cancellation without a stuck reader");
    }
}
