using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using SharpPcap.LibPcap;

namespace AionDpsMeter.Local;

internal static class GameAdapterTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string first = "{11111111-1111-1111-1111-111111111111}", second = "{22222222-2222-2222-2222-222222222222}";
        string device = @"\Device\NPF_" + first, other = @"\Device\NPF_" + second;
        var v4 = IPAddress.Parse("192.0.2.10"); var v6 = IPAddress.Parse("2001:db8::10");
        var connection = new ProcessConnection(123, v4, IPAddress.Parse("198.51.100.5"), 54321, 21000);
        AdapterAddress[] interfaces = [new(first, [v4, v6]), new(second, [IPAddress.Parse("192.0.2.20")])];
        AdapterDetection Select(params ProcessConnection[] rows) => GameAdapterDetector.Select(rows, interfaces, [other, device]);
        check(Select(connection) == new AdapterDetection(AdapterDetectionStatus.Found, device), "Game connections select the matching capture GUID regardless of enumeration order");
        check(Select(connection, connection with { ProcessId = 124, RemotePort = 443 }) is { Status: AdapterDetectionStatus.Found }, "Several Aion processes and connections on one adapter remain unambiguous");
        check(Select(connection, connection with { Local = v6, Remote = IPAddress.Parse("2001:db8::20") }).Adapter == device, "IPv4 and IPv6 connections on the same adapter agree");
        check(Select(connection with { Local = v4.MapToIPv6() }).Adapter == device, "IPv4-mapped IPv6 addresses match their physical interface");
        check(Select(connection, connection with { Local = IPAddress.Parse("192.0.2.20") }).Status == AdapterDetectionStatus.Ambiguous, "Connections across adapters require manual selection instead of traffic-volume guessing");
        check(Select().Status == AdapterDetectionStatus.NotConnected, "An unconnected game does not select an arbitrary adapter");
        check(Select(connection with { Local = IPAddress.Loopback, Remote = IPAddress.Loopback }).Status == AdapterDetectionStatus.LocalProxy, "Loopback-only connections require explicit proxy adapter selection");
        check(Select(connection with { Local = IPAddress.IPv6Loopback, Remote = IPAddress.IPv6Loopback }).Status == AdapterDetectionStatus.LocalProxy, "IPv6 loopback is not mistaken for a physical game adapter");
        check(Select(connection, connection with { Local = IPAddress.Loopback, Remote = IPAddress.Loopback }).Status == AdapterDetectionStatus.Ambiguous,
            "Mixed remote and loopback connections cannot silently select a proxy's outer adapter");
        var ipc = connection with { Local = IPAddress.Loopback, Remote = IPAddress.Loopback };
        var ipcPeer = ipc with { ProcessId = 124, LocalPort = ipc.RemotePort, RemotePort = ipc.LocalPort };
        check(Select(connection, ipc, ipcPeer).Adapter == device, "Local IPC is ignored only when both ends belong to Aion processes");
        check(Select(ipc, ipcPeer).Status == AdapterDetectionStatus.NotConnected, "Internal Aion IPC alone is not a connected game");
        check(GameAdapterDetector.Select([connection], interfaces, [other]).Status == AdapterDetectionStatus.Unavailable, "A game interface absent from Npcap does not fall back to another adapter");
        check(GameAdapterDetector.Select([connection], [interfaces[0], new(second, [v4])], [device, other]).Status == AdapterDetectionStatus.Ambiguous, "Duplicate local addresses across interfaces do not silently select one");
        check(Select(connection with { Local = IPAddress.Parse("192.0.2.99") }).Status == AdapterDetectionStatus.Unavailable, "A stale address cannot select a replacement interface");
        check(GameAdapterDetector.Select([connection], interfaces, [device.ToLowerInvariant()]).Status == AdapterDetectionStatus.Found, "Npcap GUID matching is case insensitive");

        var settings = JsonSerializer.Deserialize<Preferences>("{}")!;
        check(GameAdapterDetector.Automatic(settings), "Fresh installations default to game connection detection");
        settings.Adapter = device;
        check(!GameAdapterDetector.Automatic(settings), "Existing saved adapter choices migrate to manual selection");
        var found = new AdapterDetection(AdapterDetectionStatus.Found, other);
        check(StartupPlan.Create(settings, [device, other], false, detection: found).CaptureAdapter == device, "Detection never overrides manual startup selection");
        settings.AutoDetectAdapter = true;
        check(StartupPlan.Create(settings, [device, other], false, detection: found) is { StartCapture: true, CaptureAdapter: var chosen } && chosen == other,
            "Automatic startup uses the current game adapter instead of a stale saved adapter");
        check(!StartupPlan.Create(settings, [device], false, detection: found).StartCapture, "A disappeared detected adapter prevents automatic capture");
        foreach (var status in Enum.GetValues<AdapterDetectionStatus>().Where(s => s != AdapterDetectionStatus.Found))
            check(StartupPlan.Create(settings, [device], false, detection: new(status)) is { StartCapture: false, CaptureAdapter: null, SetupMessage: not null }, "Detection failure requests setup: " + status);
        check(!StartupPlan.Create(settings, [other], true, detection: found).StartCapture, "Missing Npcap prevents capture even with a detected adapter");
        settings.AutoStartConfigured = false;
        check(!StartupPlan.Create(settings, [other], false, detection: found).StartCapture, "Detection respects the automatic startup opt-out");
        settings.AutoStartConfigured = true;
        check(!StartupPlan.Create(settings, [other], false, demo: true, detection: found).StartCapture, "Demo startup never opens a detected live connection");
        check(GameAdapterDetector.Automatic(JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(settings))!), "Explicit automatic mode survives saving an adapter and restarting");

        var fixture = Table(false, 123, 5, v4, connection.Remote, 54321, 21000);
        check(WindowsTcpConnections.Parse(fixture, false, new HashSet<uint> { 123 }).Single() == connection, "IPv4 Windows TCP rows decode address bytes, network-order ports and process ownership");
        check(WindowsTcpConnections.Parse(fixture, false, new HashSet<uint> { 456 }).Length == 0, "Other processes' connections are excluded");
        check(WindowsTcpConnections.Parse(Table(false, 123, 11, v4, connection.Remote, 54321, 21000), false, new HashSet<uint> { 123 }).Length == 0,
            "Closed and TIME_WAIT connections do not identify an adapter");
        var ipv6Local = IPAddress.Parse("fe80::1234%7"); var ipv6Remote = IPAddress.Parse("fe80::5678%7");
        var six = WindowsTcpConnections.Parse(Table(true, 123, 5, ipv6Local, ipv6Remote, 54321, 21000), true, new HashSet<uint> { 123 }).Single();
        check(six == new ProcessConnection(123, ipv6Local, ipv6Remote, 54321, 21000), "IPv6 Windows TCP rows retain scope IDs and ports");
        bool truncated = false; try { WindowsTcpConnections.Parse(fixture.AsSpan(0, fixture.Length - 1), false, new HashSet<uint> { 123 }); } catch (InvalidDataException) { truncated = true; }
        check(truncated, "Truncated native TCP tables fail without reading outside the buffer");
        CheckNative(IPAddress.Loopback, check);
        if (Socket.OSSupportsIPv6) CheckNative(IPAddress.IPv6Loopback, check);
    }
    static byte[] Table(bool ipv6, uint pid, uint state, IPAddress local, IPAddress remote, ushort localPort, ushort remotePort)
    {
        var table = new byte[4 + (ipv6 ? 56 : 24)]; BinaryPrimitives.WriteUInt32LittleEndian(table, 1); var row = table.AsSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(row[(ipv6 ? 48 : 0)..], state);
        BinaryPrimitives.WriteUInt32LittleEndian(row[(ipv6 ? 52 : 20)..], pid);
        local.GetAddressBytes().CopyTo(row[(ipv6 ? 0 : 4)..]); remote.GetAddressBytes().CopyTo(row[(ipv6 ? 24 : 12)..]);
        BinaryPrimitives.WriteUInt16BigEndian(row[(ipv6 ? 20 : 8)..], localPort); BinaryPrimitives.WriteUInt16BigEndian(row[(ipv6 ? 44 : 16)..], remotePort);
        if (ipv6) { BinaryPrimitives.WriteUInt32BigEndian(row[16..], (uint)local.ScopeId); BinaryPrimitives.WriteUInt32BigEndian(row[40..], (uint)remote.ScopeId); }
        return table;
    }
    static void CheckNative(IPAddress loopback, Action<bool, string> check)
    {
        using var listener = new TcpListener(loopback, 0); listener.Start();
        using var client = new TcpClient(loopback.AddressFamily);
        client.ConnectAsync((IPEndPoint)listener.LocalEndpoint).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        using var server = listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        var local = (IPEndPoint)client.Client.LocalEndPoint!; var remote = (IPEndPoint)client.Client.RemoteEndPoint!;
        var native = WindowsTcpConnections.Read(new HashSet<uint> { (uint)Environment.ProcessId });
        check(native.Any(c => c.Local.Equals(local.Address) && c.Remote.Equals(remote.Address) && c.LocalPort == local.Port && c.RemotePort == remote.Port),
            $"Native {loopback.AddressFamily} connection lookup finds this process's synthetic local TCP connection");
    }
    internal static int CheckLive()
    {
        var timer = Stopwatch.StartNew(); AdapterDetection result;
        try { result = GameAdapterDetector.Detect(LibPcapLiveDeviceList.Instance.Select(d => d.Name).ToArray()); }
        catch { result = new(AdapterDetectionStatus.Failed); }
        // Deliberately omit adapter identity, IPs, endpoints and process IDs.
        File.WriteAllLines("game-adapter-results.txt", ["Status: " + result.Status, result.Message, $"Lookup time: {timer.ElapsedMilliseconds} ms", "Read-only check; no capture was started and no settings were changed."]);
        return result.Status == AdapterDetectionStatus.Failed ? 1 : 0;
    }
}
