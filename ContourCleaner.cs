using System;
using System.Collections.Generic;
using System.Linq;

namespace LinkArea2024
{
    public sealed class CleanContour
    {
        public List<OutlinePoint> Points;
        public int OriginalVertices;
        public double ExtraOutward;
    }

    public static class ContourCleaner
    {
        public static CleanContour Rectangle(IEnumerable<OutlinePoint> source, double margin, double angle, double grid)
        {
            var points = source.ToList();
            if (points.Count < 3 || margin < 0 || grid <= 0) throw new ArgumentException("Invalid rectangle input.");
            double ox = points[0].X, oy = points[0].Y, c = Math.Cos(angle), s = Math.Sin(angle);
            var local = points.Select(p => new OutlinePoint((p.X - ox) * c + (p.Y - oy) * s,
                -(p.X - ox) * s + (p.Y - oy) * c)).ToList();
            double left = Math.Floor((local.Min(p => p.X) - margin) / grid) * grid;
            double right = Math.Ceiling((local.Max(p => p.X) + margin) / grid) * grid;
            double bottom = Math.Floor((local.Min(p => p.Y) - margin) / grid) * grid;
            double top = Math.Ceiling((local.Max(p => p.Y) + margin) / grid) * grid;
            var corners = new[] { new OutlinePoint(left, bottom), new OutlinePoint(right, bottom),
                new OutlinePoint(right, top), new OutlinePoint(left, top) };
            return new CleanContour
            {
                Points = corners.Select(p => new OutlinePoint(ox + p.X * c - p.Y * s, oy + p.X * s + p.Y * c)).ToList(),
                OriginalVertices = 4, ExtraOutward = 0
            };
        }

        public static CleanContour Build(IEnumerable<OutlinePoint> source, double margin, double minimumEdge, double minimumTurnDegrees)
        {
            if (minimumEdge <= 0 || minimumTurnDegrees <= 0 || minimumTurnDegrees >= 90)
                throw new ArgumentException("Invalid contour tolerances.");
            var original = EnclosingOutline.Build(source, 0);
            var polygon = new List<OutlinePoint>(original);
            while (polygon.Count > 3)
            {
                int remove = -1;
                double smallestChange = double.PositiveInfinity;
                for (int i = 0; i < polygon.Count; i++)
                {
                    var a = polygon[(i + polygon.Count - 1) % polygon.Count];
                    var b = polygon[i];
                    var c = polygon[(i + 1) % polygon.Count];
                    double previous = Distance(a, b), next = Distance(b, c);
                    double turn = Turn(a, b, c);
                    if (previous >= minimumEdge && next >= minimumEdge && turn >= minimumTurnDegrees) continue;
                    double chord = Distance(a, c);
                    if (chord <= 1e-12) continue;
                    double change = Math.Abs(Cross(a, c, b)) / chord;
                    if (change < smallestChange) { smallestChange = change; remove = i; }
                }
                if (remove < 0) break;
                polygon.RemoveAt(remove);
            }
            // Removing a hull vertex cuts inward. Compensate by shifting every support
            // line outward enough to contain the ORIGINAL hull with the requested margin.
            double extra = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                double length = Distance(a, b);
                foreach (var p in original) extra = Math.Max(extra, -Cross(a, b, p) / length);
            }
            var result = EnclosingOutline.Build(polygon, margin + extra);
            Validate(result, minimumEdge, minimumTurnDegrees);
            if (!EnclosingOutline.ContainsAll(result, original, 1e-7))
                throw new InvalidOperationException("Clean outline does not enclose source geometry.");
            return new CleanContour { Points = result, OriginalVertices = original.Count, ExtraOutward = extra };
        }

        // Accept a closed convex polygon in either orientation; do not silently repair it here.
        public static void Validate(IList<OutlinePoint> points, double minimumEdge, double minimumTurnDegrees)
        {
            if (points == null || points.Count < 3) throw new InvalidOperationException("Контур содержит менее трёх вершин.");
            int direction = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var c = points[(i + 2) % points.Count];
                if (Distance(a, b) < minimumEdge - 1e-7)
                    throw new InvalidOperationException("В контуре есть слишком короткий отрезок: " + i);
                if (Turn(a, b, c) < minimumTurnDegrees - 1e-7)
                    throw new InvalidOperationException("В контуре есть почти коллинеарные отрезки: " + i);
                int sign = Math.Sign(Cross(a, b, c));
                if (sign == 0 || (direction != 0 && direction != sign))
                    throw new InvalidOperationException("Нарушена выпуклость контура.");
                direction = sign;
                // A star polygon can have equal turn signs, so also test all nonadjacent edges.
                for (int j = i + 2; j < points.Count; j++)
                {
                    if (i == 0 && j == points.Count - 1) continue;
                    var d = points[j];
                    var e = points[(j + 1) % points.Count];
                    if (Cross(a, b, d) * Cross(a, b, e) <= 0 && Cross(d, e, a) * Cross(d, e, b) <= 0 &&
                        Math.Max(Math.Min(a.X, b.X), Math.Min(d.X, e.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(d.X, e.X)) &&
                        Math.Max(Math.Min(a.Y, b.Y), Math.Min(d.Y, e.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(d.Y, e.Y)))
                        throw new InvalidOperationException("Самопересечение или повторяющееся ребро контура.");
                }
            }
        }

        public static double Distance(OutlinePoint a, OutlinePoint b)
        {
            double x = b.X - a.X, y = b.Y - a.Y;
            return Math.Sqrt(x * x + y * y);
        }

        private static double Cross(OutlinePoint a, OutlinePoint b, OutlinePoint c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static double Turn(OutlinePoint a, OutlinePoint b, OutlinePoint c)
        {
            double ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - b.X, vy = c.Y - b.Y;
            return Math.Atan2(Math.Abs(ux * vy - uy * vx), ux * vx + uy * vy) * 180 / Math.PI;
        }
    }
}
