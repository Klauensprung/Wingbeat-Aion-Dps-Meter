using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AionDpsMeter.Local;

internal sealed class PetSpawnPlot : FrameworkElement
{
    readonly PetSpawnViewport viewport = new();
    internal bool OverlayOnly {get;set;}
    internal bool FollowPlayer {get;set;}
    internal bool AlignmentGuide {get;set;}
    internal MapViewportState Viewport => viewport.Snapshot();
    internal void RestoreViewport(MapViewportState state){viewport.Restore(state);InvalidateVisual();}
    readonly TerrainTileCache tiles = new();
    TerrainMap? terrain;
    CapturedPosition? player;
    double uncertainty;
    readonly Pen playerRing = new(UiTheme.Brush("#E7EDF7"), 2);
    readonly Brush playerMarker = UiTheme.Brush("#74AAFF");
    readonly Brush uncertaintyFill = UiTheme.Brush("#2074AAFF");
    readonly Pen uncertaintyRing = new(UiTheme.Brush("#74AAFF"), 1);
    readonly Brush background = UiTheme.Brush("#101823"), marker = UiTheme.Brush("#55D6BE"), highlight = UiTheme.Brush("#E7CF7D");
    readonly Pen grid = new(UiTheme.Brush("#253346"), 1);
    PetSpawnPoint[] points = [];
    PetSpawnPoint[] route = [];
    internal int RouteCount=>route.Length;
    int routeStart = 1;
    FormattedText[] routeLabels=[];
    double labelDpi;
    readonly Pen routePen=new(UiTheme.Brush("#FFC857"),2);
    internal void SetRoute(PetSpawnPoint[] value, int start = 1) {
        if(routeStart==start&&route.AsSpan().SequenceEqual(value))return;
        route = value; routeStart = start;routeLabels=[]; InvalidateVisual();
    }
    HashSet<int> highlighted = new();
    internal void SetHighlights(IEnumerable<int> ids) { highlighted = ids.ToHashSet(); InvalidateVisual(); }
    Point? dragStart;
    Point previous;
    bool moved;
    int selected = -1;
    internal event Action<PetSpawnPoint?>? Selected;
    internal PetSpawnPlot() { ClipToBounds = true; Focusable = true; Cursor = Cursors.Hand; Unloaded += (_, _) => tiles.Clear(); }
    internal void SetSite(PetSpawnSite? site) { terrain = site == null ? null : PetTerrain.Find(site.MapKey); tiles.Clear(); points = site?.Spawns ?? []; selected = -1; Selected?.Invoke(null); Fit(); }
    internal void Fit() { viewport.Fit(points); InvalidateVisual(); }
    internal void FitWithPlayer(double minimumSpan = 100) {
        viewport.Fit(player is { } p ? points.Append(new PetSpawnPoint(0, p.X, p.Y, p.Z)).ToArray() : points, minimumSpan);
        InvalidateVisual();
    }
    internal void SetPlayer(CapturedPosition? value, double accuracy = 0) {
        if (value == player && uncertainty == accuracy && (!FollowPlayer || value==null || viewport.CenteredOn(value.X,value.Y))) return;
        player = value; uncertainty = accuracy;
        if(FollowPlayer && player is {} current) viewport.Center(current.X,current.Y);
        InvalidateVisual();
    }
    internal void CenterPlayer() { if (player != null) { viewport.Center(player.X, player.Y); InvalidateVisual(); } }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.DrawRectangle(OverlayOnly ? (AlignmentGuide ? UiTheme.Brush("#08101823") : Brushes.Transparent) : background, null, new Rect(RenderSize));
        if(!OverlayOnly||AlignmentGuide) {
        dc.PushOpacity(OverlayOnly ? .35 : 1);
        if (terrain is { } map) foreach (var tile in map.Tiles) {
            var first = map.World(tile.Column * map.Tile, tile.Row * map.Tile);
            var last = map.World((tile.Column + 1) * map.Tile, (tile.Row + 1) * map.Tile);
            var rect = new Rect(viewport.Project(first.X, first.Y, RenderSize), viewport.Project(last.X, last.Y, RenderSize));
            if (rect.IntersectsWith(new Rect(RenderSize)) && tiles.Get(map, tile, rect.Width) is { } image) dc.DrawImage(image, rect);
        }
        for (int i = 1; i < 6; i++) {
            dc.DrawLine(grid, new(ActualWidth * i / 6, 0), new(ActualWidth * i / 6, ActualHeight));
            dc.DrawLine(grid, new(0, ActualHeight * i / 6), new(ActualWidth, ActualHeight * i / 6));
        }
        for (int i = 0; i < points.Length; i++) {
            var p = viewport.Project(points[i].X, points[i].Y, RenderSize);
            if (p.X < -8 || p.Y < -8 || p.X > ActualWidth + 8 || p.Y > ActualHeight + 8) continue;
            bool pinned = highlighted.Contains(points[i].NpcId);
            double radius = i == selected ? 7 : pinned ? 4.5 : 3.5;
            dc.DrawEllipse(i == selected || pinned ? highlight : marker, null, p, radius, radius);
        }
        dc.Pop();
        }
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if(routeLabels.Length!=route.Length||labelDpi!=dpi){
            labelDpi=dpi;
            routeLabels=Enumerable.Range(routeStart,route.Length).Select(n=>new FormattedText(n.ToString(),System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,highlight,dpi)).ToArray();
        }
        Point? routeLast = player == null ? null : viewport.Project(player.X, player.Y, RenderSize);
        for (int i = 0; i < route.Length; i++) {
            var next = viewport.Project(route[i].X, route[i].Y, RenderSize);
            if (routeLast is Point from) {
                var direction = next - from;
                if (direction.Length > 20) {
                    direction.Normalize();
                    dc.DrawLine(routePen, from + direction * 10, next - direction * 10);
                }
            }
            dc.DrawEllipse(null, routePen, next, 9, 9);
            var label = routeLabels[i];
            dc.DrawText(label, new(next.X - label.Width / 2, next.Y - 11 - label.Height));
            routeLast = next;
        }
        if (player is { } location) {
            var point = viewport.Project(location.X, location.Y, RenderSize);
            if (uncertainty > 0 && double.IsFinite(uncertainty)) {
                var extent = viewport.Project(location.X + uncertainty, location.Y + uncertainty, RenderSize);
                dc.DrawEllipse(uncertaintyFill, uncertaintyRing, point,
                    Math.Abs(extent.X - point.X), Math.Abs(extent.Y - point.Y));
            }
            dc.DrawEllipse(playerMarker, playerRing, point, 6, 6);
            dc.DrawLine(playerRing, point + new Vector(-11, 0), point + new Vector(11, 0));
            dc.DrawLine(playerRing, point + new Vector(0, -11), point + new Vector(0, 11));
        }
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        viewport.Zoom(FollowPlayer ? new Point(ActualWidth/2,ActualHeight/2) : e.GetPosition(this), e.Delta > 0 ? 1.25 : .8, RenderSize); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if(FollowPlayer) { e.Handled=true;return; }
        Focus(); previous = e.GetPosition(this); dragStart = previous; moved = false; CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragStart is not Point start || !IsMouseCaptured) return;
        var position = e.GetPosition(this);
        if ((position - start).Length > 4) moved = true;
        if (moved) { viewport.Pan(position - previous, RenderSize); InvalidateVisual(); }
        previous = position;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragStart == null) return;
        if (!moved) { selected = viewport.Hit(points, e.GetPosition(this), RenderSize); Selected?.Invoke(selected >= 0 ? points[selected] : null); InvalidateVisual(); }
        dragStart = null; ReleaseMouseCapture(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { dragStart = null; base.OnLostMouseCapture(e); }
}
