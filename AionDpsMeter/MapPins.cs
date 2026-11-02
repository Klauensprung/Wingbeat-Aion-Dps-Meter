using System.IO;
using System.Text.Json;
namespace AionDpsMeter.Local;

internal sealed class MapPins
{
    readonly string? path;
    Dictionary<int, string[]> worlds = new();
    internal string? LoadError { get; }
    internal MapPins(string? path = null) {
        this.path=path;
        if (path==null || !File.Exists(path)) return;
        try {
            if (new FileInfo(path).Length>1024*1024) throw new InvalidDataException();
            var data=JsonSerializer.Deserialize<Dictionary<int,string[]>>(File.ReadAllText(path)) ?? throw new InvalidDataException();
            if (data.Count>1024 || data.Sum(p=>(long)(p.Value?.Length??0))>8192 || data.Any(p=>p.Key<=0 || p.Value==null || p.Value.Length>512 ||
                p.Value.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>300) || p.Value.Distinct(StringComparer.Ordinal).Count()!=p.Value.Length)) throw new InvalidDataException();
            worlds=data;
        } catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException) {LoadError="Saved map pins could not be loaded. The original file was retained.";}
    }
    internal HashSet<string> ForWorld(int world) => new(worlds.GetValueOrDefault(world)??[],StringComparer.Ordinal);
    internal void Toggle(int world,string name) {
        if(world<=0 || string.IsNullOrWhiteSpace(name) || name.Length>300) throw new ArgumentException("Invalid map pin.");
        var names=ForWorld(world);
        if(!names.Remove(name)) {
            if(names.Count>=512 || worlds.Sum(p=>p.Value.Length)>=8192 || !worlds.ContainsKey(world)&&worlds.Count>=1024) throw new InvalidOperationException("Map pin limit reached.");
            names.Add(name);
        }
        Commit(world,names.ToArray());
    }
    internal void Clear(int world) { if(worlds.ContainsKey(world)) Commit(world,[]); }
    void Commit(int world,string[] names) {
        var next=new Dictionary<int,string[]>(worlds);
        if(names.Length==0)next.Remove(world);else next[world]=names;
        if(path!=null) {
            byte[] data=JsonSerializer.SerializeToUtf8Bytes(next);
            if(data.Length>1024*1024)throw new InvalidOperationException("Map pin storage limit reached.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary=path+".tmp";
            try {File.WriteAllBytes(temporary,data);File.Move(temporary,path,true);}
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        worlds=next;
    }
}
