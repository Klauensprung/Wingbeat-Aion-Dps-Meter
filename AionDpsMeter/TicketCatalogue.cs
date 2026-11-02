using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace AionDpsMeter.Local;

internal sealed record TicketRefill(bool Weekly, int EverySeconds, int Amount, int MemberAmount);
internal sealed record TicketItem(int Id, int Amount);
internal sealed record TicketDefinition(int Id, string Name, int Max, int? MemberMax, int ItemMax, bool Shared, TicketRefill? Refill, TicketItem[] Items);
internal sealed record TicketCatalogueData(string Source, string SourceSha256, TicketDefinition[] Rows);
internal sealed class TicketCatalogue
{
    static readonly Lazy<TicketCatalogue> instance = new(() => new());
    internal static TicketCatalogue Default => instance.Value;
    readonly FrozenDictionary<int, TicketDefinition> rows;
    internal TicketDefinition? Find(int id) => rows.GetValueOrDefault(id);
    internal int Count => rows.Count;
    internal IEnumerable<TicketDefinition> Definitions => rows.Values.OrderBy(t => t.Id);
    internal string? LoadError { get; }
    TicketCatalogue()
    {
        try {
            using var stream = typeof(TicketCatalogue).Assembly.GetManifestResourceStream("Wingbeat.Tickets.json") ?? throw new InvalidDataException("Missing ticket catalogue.");
            var data = JsonSerializer.Deserialize<TicketCatalogueData>(stream) ?? throw new InvalidDataException("Empty ticket catalogue.");
            Validate(data); rows = data.Rows.ToFrozenDictionary(t => t.Id);
        } catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) {
            rows = FrozenDictionary<int, TicketDefinition>.Empty; LoadError = "Ticket names unavailable; showing IDs.";
        }
    }
    internal static void Validate(TicketCatalogueData data)
    {
        if (data.SourceSha256?.Length != 64 || !data.SourceSha256.All(Uri.IsHexDigit) || data.Rows is not { Length: > 0 and <= 4096 } ||
            data.Rows.Any(t => t == null || t.Id <= 0 || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 300 || t.Max < 0 || t.MemberMax < 0 || t.ItemMax < 0 ||
                t.Items is not { Length: <= 256 } || t.Items.Any(i => i == null || i.Id <= 0 || i.Amount <= 0) ||
                t.Refill is { } r && (r.EverySeconds <= 0 || (r.Weekly ? 604800 : 86400) % r.EverySeconds != 0 || r.Amount <= 0 || r.MemberAmount <= 0)) ||
            data.Rows.Select(t => t.Id).Distinct().Count() != data.Rows.Length) throw new InvalidDataException("Invalid ticket catalogue.");
    }
}
