using System;
using System.Collections.Generic;
using System.Linq;

namespace LinkArea2024
{
    // Pure 2D geometry, independent of Revit, so the enclosing guarantee can be tested locally.
    public struct OutlinePoint
    {
        public double X, Y;
        public OutlinePoint(double x, double y) { X = x; Y = y; }
    }

    public static class EnclosingOutline
    {
        public static bool SegmentTouches(IList<OutlinePoint> outline, OutlinePoint start, OutlinePoint end, double tolerance)
        {
            if (outline.Count < 3 || tolerance < 0) return false;
            double enter = 0, exit = 1;
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 0) return false;
                double first = (dx * (start.Y - a.Y) - dy * (start.X - a.X)) / length;
                double last = (dx * (end.Y - a.Y) - dy * (end.X - a.X)) / length;
                if (first < -tolerance && last < -tolerance) return false;
                if (first < -tolerance) enter = Math.Max(enter, (-tolerance - first) / (last - first));
                else if (last < -tolerance) exit = Math.Min(exit, (-tolerance - first) / (last - first));
                if (enter > exit) return false;
            }
            return true;
        }

        public static bool ContainsAll(IList<OutlinePoint> outline, IEnumerable<OutlinePoint> points, double tolerance)
        {
            if (outline.Count < 3 || tolerance < 0) return false;
            foreach (var point in points)
                for (int i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];
                    double dx = b.X - a.X, dy = b.Y - a.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length <= 0 || (dx * (point.Y - a.Y) - dy * (point.X - a.X)) / length < -tolerance)
                        return false;
                }
            return true;
        }

        public static List<OutlinePoint> Build(IEnumerable<OutlinePoint> source, double margin)
        {
            if (margin < 0 || double.IsNaN(margin) || double.IsInfinity(margin))
                throw new ArgumentException("Отступ должен быть неотрицательным конечным числом.");
            var points = source.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            var unique = new List<OutlinePoint>();
            foreach (var p in points)
            {
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y))
                    throw new ArgumentException("Недопустимые координаты контура.");
                if (unique.Count == 0 || Math.Abs(p.X - unique[unique.Count - 1].X) > 1e-8 ||
                    Math.Abs(p.Y - unique[unique.Count - 1].Y) > 1e-8) unique.Add(p);
            }
            if (unique.Count < 3) throw new InvalidOperationException("Недостаточно точек для внешнего контура.");
            // Translate to a local origin to preserve precision on shared-coordinate projects.
            double ox = unique[0].X, oy = unique[0].Y;
            points = unique.Select(p => new OutlinePoint(p.X - ox, p.Y - oy)).ToList();
            var lower = new List<OutlinePoint>();
            foreach (var p in points) Push(lower, p);
            var upper = new List<OutlinePoint>();
            for (int i = points.Count - 1; i >= 0; i--) Push(upper, points[i]);
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            if (lower.Count < 3) throw new InvalidOperationException("Точки контура лежат на одной прямой.");
            var result = new List<OutlinePoint>();
            for (int i = 0; i < lower.Count; i++)
            {
                OutlinePoint a = lower[(i + lower.Count - 1) % lower.Count], b = lower[i], c = lower[(i + 1) % lower.Count];
                double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y;
                double ul = Math.Sqrt(ux * ux + uy * uy), vl = Math.Sqrt(vx * vx + vy * vy);
                // Right-hand normals point outside the CCW convex hull.
                double nx = uy / ul, ny = -ux / ul, mx = vy / vl, my = -vx / vl;
                double denominator = 1 + nx * mx + ny * my;
                if (denominator < 1e-12) throw new InvalidOperationException("Вырожденный угол внешнего контура.");
                result.Add(new OutlinePoint(b.X + margin * (nx + mx) / denominator + ox,
                    b.Y + margin * (ny + my) / denominator + oy));
            }
            return result;
        }

        private static void Push(List<OutlinePoint> hull, OutlinePoint point)
        {
            while (hull.Count >= 2)
            {
                var a = hull[hull.Count - 2];
                var b = hull[hull.Count - 1];
                if ((b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X) > 1e-10) break;
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(point);
        }
    }
}
