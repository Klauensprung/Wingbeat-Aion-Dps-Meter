using System.Windows;

namespace AionDpsMeter.Local;

internal sealed record MapViewportState(double X,double Y,double SpanX,double SpanY,double Zoom);

internal sealed class PetSpawnViewport
{
    double centerX, centerY, spanX = 100, spanY = 100, zoom = 1;
    internal bool CenteredOn(double x,double y)=>centerX==x&&centerY==y;
    internal MapViewportState Snapshot()=>new(centerX,centerY,spanX,spanY,zoom);
    internal void Restore(MapViewportState s) {
        if(!new[]{s.X,s.Y,s.SpanX,s.SpanY,s.Zoom}.All(double.IsFinite)||Math.Abs(s.X)>10000000||Math.Abs(s.Y)>10000000||s.SpanX<100||s.SpanY<100||s.Zoom<.01||s.Zoom>64)throw new ArgumentException("Invalid map alignment");
        centerX=s.X;centerY=s.Y;spanX=s.SpanX;spanY=s.SpanY;zoom=s.Zoom;
    }
    internal void Fit(IReadOnlyList<PetSpawnPoint> points, double minimumSpan = 100)
    {
        if (!double.IsFinite(minimumSpan) || minimumSpan < 100) throw new ArgumentOutOfRangeException(nameof(minimumSpan));
        double minX = points.Count == 0 ? 0 : points.Min(p => p.X), maxX = points.Count == 0 ? 0 : points.Max(p => p.X);
        double minY = points.Count == 0 ? 0 : points.Min(p => p.Y), maxY = points.Count == 0 ? 0 : points.Max(p => p.Y);
        centerX = (minX + maxX) / 2; centerY = (minY + maxY) / 2;
        spanX = Math.Max(minimumSpan, maxX - minX); spanY = Math.Max(minimumSpan, maxY - minY); zoom = 1;
    }
    double Scale(Size size) => Math.Max(.000001, Math.Min(Math.Max(1, size.Width - 48) / spanX, Math.Max(1, size.Height - 48) / spanY) * zoom);
    internal void Center(double x, double y) { centerX = x; centerY = y; }
    internal Point Project(double x, double y, Size size) => new(size.Width / 2 + (x - centerX) * Scale(size), size.Height / 2 + (y - centerY) * Scale(size));
    internal Point Unproject(Point point, Size size) => new(centerX + (point.X - size.Width / 2) / Scale(size), centerY + (point.Y - size.Height / 2) / Scale(size));
    internal void Pan(Vector delta, Size size) { centerX -= delta.X / Scale(size); centerY -= delta.Y / Scale(size); }
    internal void Zoom(Point anchor, double factor, Size size)
    {
        var before = Unproject(anchor, size); zoom = Math.Clamp(zoom * factor, .01, 64);
        var after = Unproject(anchor, size); centerX += before.X - after.X; centerY += before.Y - after.Y;
    }
    internal int Hit(IReadOnlyList<PetSpawnPoint> points, Point position, Size size)
    {
        double nearest = 100; int result = -1;
        for (int i = 0; i < points.Count; i++) {
            var p = Project(points[i].X, points[i].Y, size); double distance = (p - position).LengthSquared;
            if (distance <= nearest) { nearest = distance; result = i; }
        }
        return result;
    }
}
