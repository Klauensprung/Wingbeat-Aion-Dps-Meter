namespace AionDpsMeter.Local;

public sealed partial class MainWindow
{
    void ShowDutyMissions()
    {
        WeeklyPurchaseRow[] ReadRows() {
            var servers = accountCharacters.Snapshot().Select(r => r.Server).ToList();
            var live = LiveSnapshot();
            if (live.Actors.GetValueOrDefault(live.LocalPlayer) is { Player: true, Server: > 0 } local) servers.Add(local.Server);
            return ChecklistTasks.DailyRows(dutyMissions.Snapshot(), servers, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        ShowWeeklyPurchases(ReadRows(), ReadRows, daily: true);
    }
    void ShowDutyMissions(DutyMissionProgress[] readings, long now) => ShowWeeklyPurchases(ChecklistTasks.DailyRows(readings, [], now), daily: true);
}
