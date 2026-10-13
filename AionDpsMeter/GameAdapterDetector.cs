using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace AionDpsMeter.Local;

internal enum AdapterDetectionStatus { Found, GameNotRunning, NotConnected, LocalProxy, Ambiguous, Unavailable, Failed }
internal sealed record AdapterDetection(AdapterDetectionStatus Status, string? Adapter = null)
{
    internal string Message => Status switch {
        AdapterDetectionStatus.Found => "Adapter detected from Aion's active connections. Capture stays on this adapter until stopped.",
        AdapterDetectionStatus.GameNotRunning => "Open Aion and connect to a server, then use Detect game adapter or Start capture.",
        AdapterDetectionStatus.NotConnected => "Aion has no active remote TCP connections yet. Connect to a server, then retry detection.",
        AdapterDetectionStatus.LocalProxy => "Aion is connected through a local proxy. Turn off automatic detection and choose your capture adapter manually.",
        AdapterDetectionStatus.Ambiguous => "Aion's connections do not identify one adapter. Turn off automatic detection and choose your capture adapter manually.",
        AdapterDetectionStatus.Unavailable => "Aion's connection could not be matched to an available Npcap adapter. Refresh adapters, or choose one manually.",
        _ => "Windows connection information could not be read. Retry detection, or turn it off and choose an adapter manually."
    };
}
internal sealed record AdapterAddress(string InterfaceId, IPAddress[] Addresses);
internal sealed record ProcessConnection(uint ProcessId, IPAddress Local, IPAddress Remote, ushort LocalPort, ushort RemotePort);

internal static class GameAdapterDetector
{
    // A missing setting migrates existing saved selections to manual, while new
    // installations use automatic detection. Never silently replace a saved choice.
    internal static bool Automatic(Preferences settings) => settings.AutoDetectAdapter ?? string.IsNullOrWhiteSpace(settings.Adapter);
    internal static AdapterDetection Detect(string[] captureDevices)
    {
        try {
            var processes = new HashSet<uint>();
            foreach (string name in new[] { "AION2", "AION2-Win64-Shipping" }) {
                foreach (var process in Process.GetProcessesByName(name)) {
                    using (process) { try { processes.Add((uint)process.Id); } catch (InvalidOperationException) { } }
                }
            }
            if (processes.Count == 0) return new(AdapterDetectionStatus.GameNotRunning);
            var connections = WindowsTcpConnections.Read(processes);
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => new AdapterAddress(n.Id, n.GetIPProperties().UnicastAddresses.Select(a => a.Address).ToArray())).ToArray();
            return Select(connections, interfaces, captureDevices);
        }
        catch (Exception ex) when (ex is Win32Exception or NetworkInformationException or InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) {
            // No endpoints, process IDs, adapter IDs or exception details are logged.
            return new(AdapterDetectionStatus.Failed);
        }
    }
    internal static AdapterDetection Select(ProcessConnection[] connections, AdapterAddress[] interfaces, string[] captureDevices)
    {
        static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        // Both ends must be owned by Aion before local IPC can be ignored. A
        // connection to an unrelated local proxy has only one end in this table.
        var endpoints = connections.Select(c => (Normalize(c.Local), c.LocalPort, Normalize(c.Remote), c.RemotePort)).ToHashSet();
        var external = connections.Where(c => !IPAddress.IsLoopback(Normalize(c.Local)) || !IPAddress.IsLoopback(Normalize(c.Remote)) ||
            !endpoints.Contains((Normalize(c.Remote), c.RemotePort, Normalize(c.Local), c.LocalPort))).ToArray();
        var remote = external.Where(c => !IPAddress.IsLoopback(Normalize(c.Local)) && !IPAddress.IsLoopback(Normalize(c.Remote)) &&
            !Normalize(c.Local).Equals(IPAddress.Any) && !c.Local.Equals(IPAddress.IPv6Any)).ToArray();
        if (remote.Length == 0) return new(external.Length == 0 ? AdapterDetectionStatus.NotConnected : AdapterDetectionStatus.LocalProxy);
        // A remote login connection can coexist with a proxied game connection.
        // Without protocol evidence, do not assume the remote one carries combat.
        if (remote.Length != external.Length) return new(AdapterDetectionStatus.Ambiguous);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in remote.Select(c => Normalize(c.Local)).Distinct()) {
            var matching = interfaces.Where(n => n.Addresses.Any(a => Normalize(a).Equals(address))).Select(n => n.InterfaceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (matching.Length > 1) return new(AdapterDetectionStatus.Ambiguous);
            if (matching.Length == 0 || !Guid.TryParse(matching[0], out var id)) return new(AdapterDetectionStatus.Unavailable);
            var devices = captureDevices.Where(d => CaptureGuid(d) == id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (devices.Length != 1) return new(devices.Length == 0 ? AdapterDetectionStatus.Unavailable : AdapterDetectionStatus.Ambiguous);
            selected.Add(devices[0]);
        }
        return selected.Count == 1 ? new(AdapterDetectionStatus.Found, selected.Single()) : new(AdapterDetectionStatus.Ambiguous);
    }
    static Guid? CaptureGuid(string name)
    {
        const string prefix = @"\Device\NPF_";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(name[prefix.Length..], out var value) ? value : null;
    }
}

internal static class WindowsTcpConnections
{
    // Windows IP Helper, TCP_TABLE_OWNER_PID_CONNECTIONS. Address bytes and port
    // fields are network order; table counts, states and PIDs are native DWORDs.
    // https://learn.microsoft.com/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint GetExtendedTcpTable([Out] byte[]? table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, uint family, int tableClass, uint reserved);

    internal static ProcessConnection[] Read(IReadOnlySet<uint> processIds) => [.. ReadFamily(false, processIds), .. ReadFamily(true, processIds)];
    static ProcessConnection[] ReadFamily(bool ipv6, IReadOnlySet<uint> processIds)
    {
        uint size = 0;
        uint result = GetExtendedTcpTable(null, ref size, false, ipv6 ? 23u : 2u, 4, 0);
        for (int attempt = 0; attempt < 4 && result == 122; attempt++) {
            if (size < 4 || size > 16 * 1024 * 1024) throw new InvalidDataException("Invalid TCP table size.");
            var buffer = new byte[size];
            result = GetExtendedTcpTable(buffer, ref size, false, ipv6 ? 23u : 2u, 4, 0);
            if (result == 0) return Parse(buffer, ipv6, processIds);
        }
        if (result == 0 || (ipv6 && result == 50)) return [];
        throw new Win32Exception((int)result);
    }
    internal static ProcessConnection[] Parse(ReadOnlySpan<byte> table, bool ipv6, IReadOnlySet<uint> processIds)
    {
        int stride = ipv6 ? 56 : 24;
        if (table.Length < 4) throw new InvalidDataException("Truncated TCP table.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        if (count > (table.Length - 4) / stride) throw new InvalidDataException("Truncated TCP rows.");
        var rows = new List<ProcessConnection>();
        for (int i = 0; i < count; i++) {
            var row = table.Slice(4 + i * stride, stride);
            uint state = BinaryPrimitives.ReadUInt32LittleEndian(row[(ipv6 ? 48 : 0)..]);
            uint pid = BinaryPrimitives.ReadUInt32LittleEndian(row[(ipv6 ? 52 : 20)..]);
            if (state != 5 || !processIds.Contains(pid)) continue;
            IPAddress local = ipv6 ? new(row[..16], BinaryPrimitives.ReadUInt32BigEndian(row[16..])) : new(row.Slice(4, 4));
            IPAddress remote = ipv6 ? new(row.Slice(24, 16), BinaryPrimitives.ReadUInt32BigEndian(row[40..])) : new(row.Slice(12, 4));
            rows.Add(new(pid, local, remote, BinaryPrimitives.ReadUInt16BigEndian(row[(ipv6 ? 20 : 8)..]), BinaryPrimitives.ReadUInt16BigEndian(row[(ipv6 ? 44 : 16)..])));
        }
        return rows.ToArray();
    }
}
