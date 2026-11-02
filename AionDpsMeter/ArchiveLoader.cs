using System.Diagnostics;
namespace AionDpsMeter.Local;

// One file at a time, with backpressure from the UI. Full fights never enter a batch.
internal sealed class ArchiveLoader
{
    readonly SemaphoreSlim gate=new(1,1);
    internal Task LoadAsync(Func<IEnumerable<string>> files,Func<string,Fight> load,
        Func<Fight[],int,Task> publish,CancellationToken cancellation)=>Task.Run(async ()=>{
        await gate.WaitAsync(cancellation);
        try {
            var batch=new List<Fight>(16);int skipped=0;var elapsed=Stopwatch.StartNew();
            foreach(var path in files()) {
                cancellation.ThrowIfCancellationRequested();
                try {batch.Add(FightArchiveSummary.Create(load(path)));}
                catch(Exception ex)when(ex is not OperationCanceledException){skipped++;}
                cancellation.ThrowIfCancellationRequested();
                if(batch.Count>=16||elapsed.ElapsedMilliseconds>=200){
                    await publish(batch.ToArray(),skipped);batch.Clear();elapsed.Restart();
                    await Task.Delay(10,cancellation);
                }
            }
            cancellation.ThrowIfCancellationRequested();
            await publish(batch.ToArray(),skipped);
        }finally{gate.Release();}
    },cancellation);
}
