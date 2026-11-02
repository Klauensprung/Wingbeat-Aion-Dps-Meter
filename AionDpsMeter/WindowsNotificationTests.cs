namespace AionDpsMeter.Local;

internal static class WindowsNotificationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var commands = new List<uint>();
        WindowsNotifications.IconData last = default;
        bool Send(uint command, ref WindowsNotifications.IconData data) { commands.Add(command); last = data; return true; }
        var notice = new WindowsNotifications(1, Send);
        check(notice.Show("Event", "Upcoming") && commands.SequenceEqual(new uint[] { 0, 4, 1 }) && last.Version == 4 &&
            last.Flags == (16 | 64) && last.InfoFlags == (1 | 16 | 128),
            "Windows notices initialize version 4 and request realtime quiet-aware delivery without duplicate system sound");
        notice.Show("Next", "Later"); notice.Dispose(); notice.Dispose();
        check(commands.SequenceEqual(new uint[] { 0, 4, 1, 1, 2 }) && !notice.Show("Closed", "No delivery"),
            "Native notices reuse one icon, remove it once and reject notifications after disposal");
        int attempts = 0;
        bool Fail(uint command, ref WindowsNotifications.IconData data) { attempts++; return false; }
        using var failed = new WindowsNotifications(1, Fail);
        check(!failed.Show("Event", "Upcoming") && attempts == 1, "Native setup failure returns control for the overlay fallback");
        var rejectedCommands = new List<uint>();
        bool RejectVersion(uint command, ref WindowsNotifications.IconData data) { rejectedCommands.Add(command); return command != 4; }
        using var rejected = new WindowsNotifications(1, RejectVersion);
        check(!rejected.Show("Event", "Upcoming") && rejectedCommands.SequenceEqual(new uint[] { 0, 4, 2 }),
            "Failed native version setup removes the partially created notification icon");
        string unicode = new string('x', 254) + char.ConvertFromUtf32(0x1f600);
        check(WindowsNotifications.Truncate(unicode, 255).Length == 254 && WindowsNotifications.Truncate("A\0B", 63) == "A B",
            "Native notification text respects UTF-16 buffer limits without splitting surrogate pairs or embedding terminators");
    }
}
