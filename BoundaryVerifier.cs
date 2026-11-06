using System;
using System.Collections.Generic;
using System.Linq;

namespace LinkArea2024
{
    public struct BoundaryPiece
    {
        public OutlinePoint Start, End;
        public BoundaryPiece(OutlinePoint start, OutlinePoint end) { Start = start; End = end; }
    }

    public static class BoundaryVerifier
    {
        private sealed class MappedPiece
        {
            public BoundaryPiece Piece;
            public int Edge;
            public double From, To;
        }

        public static List<OutlinePoint> Verify(IList<OutlinePoint> expected, IList<BoundaryPiece> actual, double tolerance)
        {
            if (expected.Count < 3 || actual.Count < 3) throw new InvalidOperationException("Пустой фактический контур.");
            var mapped = new List<MappedPiece>();
            foreach (var piece in actual)
            {
                if (ContourCleaner.Distance(piece.Start, piece.End) <= tolerance)
                    throw new InvalidOperationException("Нулевой или вырожденный участок фактической границы.");
                MappedPiece match = null;
                for (int i = 0; i < expected.Count; i++)
                {
                    var a = expected[i];
                    var b = expected[(i + 1) % expected.Count];
                    double length = ContourCleaner.Distance(a, b);
                    double dx = (b.X - a.X) / length, dy = (b.Y - a.Y) / length;
                    double p = (piece.Start.X - a.X) * dx + (piece.Start.Y - a.Y) * dy;
                    double q = (piece.End.X - a.X) * dx + (piece.End.Y - a.Y) * dy;
                    double offP = Math.Abs(dx * (piece.Start.Y - a.Y) - dy * (piece.Start.X - a.X));
                    double offQ = Math.Abs(dx * (piece.End.Y - a.Y) - dy * (piece.End.X - a.X));
                    if (offP <= tolerance && offQ <= tolerance && Math.Min(p, q) >= -tolerance && Math.Max(p, q) <= length + tolerance)
                    {
                        match = new MappedPiece { Piece = piece, Edge = i, From = Math.Min(p, q), To = Math.Max(p, q) };
                        break;
                    }
                }
                if (match == null) throw new InvalidOperationException("Фактическая граница отклоняется от построенного контура.");
                mapped.Add(match);
            }
            // Match intervals geometrically: one source line may appear in several BoundarySegments.
            for (int i = 0; i < expected.Count; i++)
            {
                double length = ContourCleaner.Distance(expected[i], expected[(i + 1) % expected.Count]);
                var spans = mapped.Where(p => p.Edge == i).OrderBy(p => p.From).ToList();
                double covered = 0;
                if (spans.Count == 0) throw new InvalidOperationException("Ребро построенного контура не вошло в границу зоны.");
                foreach (var span in spans)
                {
                    if (span.From > covered + tolerance) throw new InvalidOperationException("Разрыв покрытия ребра фактической границы.");
                    if (span.From < covered - tolerance) throw new InvalidOperationException("Дублирующийся или перекрывающийся участок фактической границы.");
                    covered = span.To;
                }
                if (Math.Abs(covered - length) > tolerance) throw new InvalidOperationException("Неполное покрытие ребра фактической границы.");
            }
            // Revit can reverse or subdivide curves. Reconstruct one cycle by endpoints.
            var remaining = new List<MappedPiece>(mapped);
            var ordered = new List<MappedPiece> { remaining[0] };
            remaining.RemoveAt(0);
            while (remaining.Count > 0)
            {
                var end = ordered[ordered.Count - 1].Piece.End;
                var candidates = remaining.Where(p => ContourCleaner.Distance(end, p.Piece.Start) <= tolerance ||
                    ContourCleaner.Distance(end, p.Piece.End) <= tolerance).ToList();
                if (candidates.Count != 1) throw new InvalidOperationException("Разрыв или разветвление фактической границы.");
                var next = candidates[0];
                if (ContourCleaner.Distance(end, next.Piece.Start) > tolerance)
                    next.Piece = new BoundaryPiece(next.Piece.End, next.Piece.Start);
                ordered.Add(next);
                remaining.Remove(next);
            }
            if (ContourCleaner.Distance(ordered[ordered.Count - 1].Piece.End, ordered[0].Piece.Start) > tolerance)
                throw new InvalidOperationException("Фактическая граница не замкнута.");
            // Collapse topology splits ONLY for verification. Do not modify model geometry.
            var corners = new List<OutlinePoint>();
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[(i + ordered.Count - 1) % ordered.Count].Edge != ordered[i].Edge)
                    corners.Add(ordered[i].Piece.Start);
            if (corners.Count != expected.Count) throw new InvalidOperationException("Нарушен порядок рёбер фактической границы.");
            return corners;
        }
    }
}
