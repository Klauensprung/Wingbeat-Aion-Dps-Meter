using System.Globalization;
using System.IO;
using System.Text;

namespace AionDpsMeter.Local;

internal sealed record BossAnnouncement(double Npc, string? Race, string Name, double? Server);
internal static class BossAnnouncementWire
{
    static string Text(ref WireReader r) { int n=r.Var(); if(n>4096)throw new InvalidDataException("Server message string exceeds bounds.");return new UTF8Encoding(false,true).GetString(r.Take(n)).TrimEnd('\0'); }
    static double Number(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return 0;
        string value=text.Trim();
        if(value.Length>2 && value[0]=='0' && "xXbBoO".Contains(value[1])) {
            try { return Convert.ToUInt64(value[2..], char.ToLowerInvariant(value[1]) switch {'x'=>16,'b'=>2,_=>8}); }
            catch(Exception ex) when(ex is FormatException or OverflowException or ArgumentException){return 0;}
        }
        return double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double number) && double.IsFinite(number) ? number : 0;
    }
    internal static BossAnnouncement? Read(ref WireReader r)
    {
        int message=unchecked((int)r.U32()),count=r.Var();if(count>4096)throw new InvalidDataException("Server message parameter list exceeds bounds.");
        string? race=null,npc=null;string name="";
        for(int i=0;i<count;i++){string value=Text(ref r);if(i==0)race=value;else if(i==1)name=value;else if(i==2)npc=value;}
        if(r.Remaining!=0)throw new InvalidDataException("Unexpected server message trailer.");
        if(message!=139)return null;
        int colon=name.LastIndexOf(':');double server=colon<0?0:Number(name[(colon+1)..]);
        return new(Number(npc),race switch{"Light"=>"light","Dark"=>"dark",_=>null},colon<0?name:name[..colon],server==0?null:server);
    }
}
internal sealed record ArtifactCaptorship(int Artifact, string Owner, long Timestamp, string? Name=null, double? Server=null);
