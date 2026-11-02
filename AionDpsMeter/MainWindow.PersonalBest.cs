namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    readonly Queue<string> bossNotifications = new();
    string bestNotice = "";
    long bestNoticeUntil;
    void NotifyPersonalBest(Fight saved)
    {
        foreach (var improvement in BossPersonalBest.Improvements(saved, archive)) {
            var record = improvement.Current;
            if (archive.Where(f => f.Id == saved.Id).SelectMany(BossPersonalBest.Records)
                .Any(r => r.EntityId == record.EntityId && r.DeathTimestamp == record.DeathTimestamp)) continue;
            string key = $"{saved.Id}:{record.EntityId}:{record.DeathTimestamp}";
            if (bossNotifications.Contains(key)) continue;
            bossNotifications.Enqueue(key);
            while (bossNotifications.Count > 512) bossNotifications.Dequeue();
            bestNotice = $"Personal best · {record.Boss}: {record.Dps:N1} DPS (+{record.Dps - improvement.PreviousDps:N1})";
            notice = bestNotice; bestNoticeUntil = Environment.TickCount64 + 15000;
        }
    }
}
