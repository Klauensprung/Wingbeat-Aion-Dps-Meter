using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AionDpsMeter.Local;

// Keep the collection/view and column objects alive across capture ticks.
// Immutable analysis records are replaced only when their values change.
internal sealed class LiveTable : DataGrid
{
    public LiveTable() => SetResourceReference(StyleProperty, typeof(DataGrid));
    protected override void OnAutoGeneratingColumn(DataGridAutoGeneratingColumnEventArgs e)
    {
        base.OnAutoGeneratingColumn(e);
        if (e.Cancel || e.Column is not DataGridTextColumn column) return;
        var style = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(e.PropertyName)));
        Type type = Nullable.GetUnderlyingType(e.PropertyType) ?? e.PropertyType;
        if (type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        column.ElementStyle = style;
    }
    sealed class Rows(Type type) : ObservableCollection<object>, ITypedList
    {
        public PropertyDescriptorCollection GetItemProperties(PropertyDescriptor[]? accessors) => TypeDescriptor.GetProperties(type);
        public string GetListName(PropertyDescriptor[]? accessors) => type.Name;
    }
    Rows? rows;
    Type? rowType;
    internal void SetRows(IEnumerable values)
    {
        var next = values.Cast<object>().ToArray();
        if (rows != null && rows.SequenceEqual(next)) return;
        var type = next.FirstOrDefault()?.GetType();
        if (rows == null || type != null && type != rowType)
        {
            if (type == null) return;
            rowType = type; rows = new(type);
            foreach (var item in next) rows.Add(item);
            ItemsSource = rows; return;
        }
        var selectedKeys = SelectedItems.Cast<object>().Select(Key).ToHashSet();
        var scroll = FindScroll(this);
        double horizontal = scroll?.HorizontalOffset ?? 0, vertical = scroll?.VerticalOffset ?? 0;
        for (int i = 0; i < next.Length; i++)
        {
            if (i == rows.Count) rows.Add(next[i]);
            else if (!Equals(rows[i], next[i])) rows[i] = next[i];
        }
        while (rows.Count > next.Length) rows.RemoveAt(rows.Count - 1);
        // Single-selection grids reject mutations of SelectedItems. Restore the
        // current row synchronously so rendering never sees the cleared selection.
        if (SelectionMode == DataGridSelectionMode.Single)
            SelectedItem = rows.FirstOrDefault(item => selectedKeys.Contains(Key(item)));
        else if (selectedKeys.Count > 0)
        {
            foreach (var item in rows.Where(item => selectedKeys.Contains(Key(item))))
                if (!SelectedItems.Contains(item)) SelectedItems.Add(item);
        }
        // Replacing a selected row must not scroll it back into view. Clamping
        // when filtering removes rows is handled by the existing scroll viewer.
        scroll?.ScrollToHorizontalOffset(horizontal); scroll?.ScrollToVerticalOffset(vertical);
    }
    static object Key(object row) => row switch {
        TotalRow r => r.Id,
        EncounterParticipant r => r.Id,
        EncounterSkill r => (r.ActorId, r.SkillCode),
        BossAbility r => r.SkillCode,
        BossParticipant r => r.Id,
        BossAbilityTarget r => (r.SkillCode, r.EntityId),
        OpponentRow r => r.Id,
        SkillRow r => (r.Character, r.SkillCode),
        TrackedCooldownRow r => r.SkillCode,
        HealingRow r => (r.Healer, r.SkillCode, r.Target),
        DefenseRow r => r.Character,
        OpenerRow r => (r.ActorId, r.Order),
        RotationRow r => (r.ActorId, r.Hit.Sequence),
        SpecRow r => (r.Character, r.SkillCode),
        EquipmentRow r => (r.Character, r.Slot),
        TrendRow r => (r.FightId, r.Character, r.Job, r.Server),
        EncounterTrendRow r => r.Encounter.ToUpperInvariant(),
        ReportRow r => (r.Character, r.Encounter),
        CohortRow r => (r.Tier, r.Code),
        DifferenceRow r => (r.Tier, r.Metric),
        CohortSpecRow r => (r.Tier, r.Code, r.Level, r.Choices),
        CohortEquipmentRow r => (r.Tier, r.Slot, r.Item),
        CohortOpenerRow r => (r.Tier, r.FirstSkill, r.FirstEvent),
        _ => row.GetType().GetProperty("Id")?.GetValue(row) ?? row
    };
    internal static ScrollViewer? FindScroll(System.Windows.DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer scroll) return scroll;
            if (FindScroll(child) is { } nested) return nested;
        }
        return null;
    }
}
