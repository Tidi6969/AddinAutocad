using System;
using Autodesk.AutoCAD.Geometry;

namespace DDimnotes
{
    public enum ViewDir
    {
        Deg0 = 0,
        Deg90 = 90,
        Deg180 = 180,
        Deg270 = 270
    }

    public sealed class BBox2d
    {
        public double Xmin { get; }
        public double Ymin { get; }
        public double Xmax { get; }
        public double Ymax { get; }

        public BBox2d(double xmin, double ymin, double xmax, double ymax)
        {
            Xmin = Math.Min(xmin, xmax);
            Xmax = Math.Max(xmin, xmax);
            Ymin = Math.Min(ymin, ymax);
            Ymax = Math.Max(ymin, ymax);
        }

        public double Width => Math.Abs(Xmax - Xmin);
        public double Height => Math.Abs(Ymax - Ymin);
        public double Area => Width * Height;

        public Point2d Center => new Point2d((Xmin + Xmax) / 2.0, (Ymin + Ymax) / 2.0);
    }

    public sealed class ThicknessRec
    {
        public Autodesk.AutoCAD.DatabaseServices.ObjectId Id { get; }
        public BBox2d Box { get; }
        public double Thickness { get; }
        public string Kind { get; }
        public string Layer { get; }
        public short? ColorIndex { get; }

        public ThicknessRec(
            Autodesk.AutoCAD.DatabaseServices.ObjectId id,
            BBox2d box,
            double thickness,
            string kind,
            string layer,
            short? colorIndex)
        {
            Id = id;
            Box = box;
            Thickness = thickness;
            Kind = kind ?? "UNK";
            Layer = layer ?? "0";
            ColorIndex = colorIndex;
        }
    }

    public sealed class HoleRec
    {
        public Autodesk.AutoCAD.DatabaseServices.ObjectId Id { get; }
        public Point2d Center { get; }
        public string Code { get; }
        public double Dia { get; }
        public double Depth { get; }
        public string TolStr { get; }
        public string MetricThreadSizeKey { get; }
        public string Layer { get; }
        public short? ColorIndex { get; }

        public HoleRec(
            Autodesk.AutoCAD.DatabaseServices.ObjectId id,
            Point2d center,
            string code,
            double dia,
            double depth,
            string tolStr,
            string layer,
            short? colorIndex,
            string metricThreadSizeKey = null)
        {
            Id = id;
            Center = center;
            Code = code ?? "";
            Dia = dia;
            Depth = depth;
            TolStr = tolStr ?? "0";
            MetricThreadSizeKey = metricThreadSizeKey ?? string.Empty;
            Layer = layer ?? "0";
            ColorIndex = colorIndex;
        }
    }

    public sealed class SideThreadRec
    {
        public Autodesk.AutoCAD.DatabaseServices.ObjectId Id { get; }
        public Point2d Start { get; }
        public Point2d End { get; }
        public Point2d Center { get; }
        public string Code { get; }
        public double Dia { get; }
        public string TolStr { get; }
        public string MetricThreadSizeKey { get; }
        public string Layer { get; }
        public short? ColorIndex { get; }

        public SideThreadRec(
            Autodesk.AutoCAD.DatabaseServices.ObjectId id,
            Point2d start,
            Point2d end,
            string code,
            double dia,
            string tolStr,
            string layer,
            short? colorIndex,
            string metricThreadSizeKey = null)
        {
            Id = id;
            Start = start;
            End = end;
            Center = new Point2d((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
            Code = code ?? "";
            Dia = dia;
            TolStr = tolStr ?? "0";
            MetricThreadSizeKey = metricThreadSizeKey ?? string.Empty;
            Layer = layer ?? "0";
            ColorIndex = colorIndex;
        }
    }

    public sealed class CurverRec
    {
        public Autodesk.AutoCAD.DatabaseServices.ObjectId Id { get; }
        public BBox2d Box { get; }
        public double Depth { get; }
        public string TolStr { get; }
        public string Layer { get; }
        public short? ColorIndex { get; }

        public CurverRec(
            Autodesk.AutoCAD.DatabaseServices.ObjectId id,
            BBox2d box,
            double depth,
            string tolStr,
            string layer,
            short? colorIndex)
        {
            Id = id;
            Box = box;
            Depth = depth;
            TolStr = tolStr ?? "0";
            Layer = layer ?? "0";
            ColorIndex = colorIndex;
        }
    }

    public enum RectSpecKind
    {
        Rectangle,
        Polyline,
        Circle,
        Arc,
        Line
    }

    public sealed class RectSpec
    {
        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public string Linetype { get; }
        public string Layer { get; }
        public short? ColorIndex { get; }
        public System.Collections.Generic.IReadOnlyList<Point2d> Points { get; }
        public bool Closed { get; }
        public RectSpecKind Kind { get; }
        public Point2d Center { get; }
        public double Radius { get; }
        public double StartAngleRad { get; }
        public double EndAngleRad { get; }

        public bool IsPolyline => Kind == RectSpecKind.Polyline;

        public RectSpec(double x1, double y1, double x2, double y2, string linetype, string layer, short? colorIndex)
        {
            Kind = RectSpecKind.Rectangle;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Linetype = linetype ?? "Continuous";
            Layer = layer ?? "0";
            ColorIndex = colorIndex;
            Points = null;
            Closed = true;
            Center = new Point2d((x1 + x2) / 2.0, (y1 + y2) / 2.0);
            Radius = 0.0;
            StartAngleRad = 0.0;
            EndAngleRad = 0.0;
        }

        private RectSpec(System.Collections.Generic.IReadOnlyList<Point2d> points, bool closed, string linetype, string layer, short? colorIndex, RectSpecKind kind)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (points.Count < 2) throw new ArgumentException("Line/polyline needs at least two points.", nameof(points));

            Kind = kind;
            Points = points;
            Closed = closed;
            Linetype = linetype ?? "Continuous";
            Layer = layer ?? "0";
            ColorIndex = colorIndex;
            Center = new Point2d((points[0].X + points[points.Count - 1].X) / 2.0, (points[0].Y + points[points.Count - 1].Y) / 2.0);
            Radius = 0.0;
            StartAngleRad = 0.0;
            EndAngleRad = 0.0;

            double xmin = points[0].X, xmax = points[0].X;
            double ymin = points[0].Y, ymax = points[0].Y;
            foreach (var p in points)
            {
                xmin = Math.Min(xmin, p.X);
                xmax = Math.Max(xmax, p.X);
                ymin = Math.Min(ymin, p.Y);
                ymax = Math.Max(ymax, p.Y);
            }
            X1 = xmin;
            Y1 = ymin;
            X2 = xmax;
            Y2 = ymax;
        }

        private RectSpec(Point2d center, double radius, double startAngleRad, double endAngleRad, string linetype, string layer, short? colorIndex, RectSpecKind kind)
        {
            if (radius <= 0.0) throw new ArgumentOutOfRangeException(nameof(radius));
            Kind = kind;
            Points = null;
            Closed = false;
            Linetype = linetype ?? "Continuous";
            Layer = layer ?? "0";
            ColorIndex = colorIndex;
            Center = center;
            Radius = radius;
            StartAngleRad = startAngleRad;
            EndAngleRad = endAngleRad;
            X1 = center.X - radius;
            Y1 = center.Y - radius;
            X2 = center.X + radius;
            Y2 = center.Y + radius;
        }

        public static RectSpec Polyline(System.Collections.Generic.IEnumerable<Point2d> points, bool closed, string linetype, string layer, short? colorIndex)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            var list = new System.Collections.Generic.List<Point2d>(points);
            if (list.Count < 2) throw new ArgumentException("Polyline needs at least two points.", nameof(points));
            return new RectSpec(list, closed, linetype, layer, colorIndex, RectSpecKind.Polyline);
        }

        public static RectSpec Line(Point2d start, Point2d end, string linetype, string layer, short? colorIndex)
        {
            return new RectSpec(new[] { start, end }, false, linetype, layer, colorIndex, RectSpecKind.Line);
        }

        public static RectSpec Circle(Point2d center, double radius, string linetype, string layer, short? colorIndex)
        {
            return new RectSpec(center, radius, 0.0, Math.PI * 2.0, linetype, layer, colorIndex, RectSpecKind.Circle);
        }

        public static RectSpec Arc(Point2d center, double radius, double startAngleDeg, double endAngleDeg, string linetype, string layer, short? colorIndex)
        {
            double start = startAngleDeg * Math.PI / 180.0;
            double end = endAngleDeg * Math.PI / 180.0;
            return new RectSpec(center, radius, start, end, linetype, layer, colorIndex, RectSpecKind.Arc);
        }
    }

    internal sealed class SideDepthDimSegment
    {
        public HoleRec Hole { get; }
        public Point2d InnerPoint { get; }
        public Point2d EdgePoint { get; }

        public SideDepthDimSegment(HoleRec hole, Point2d innerPoint, Point2d edgePoint)
        {
            Hole = hole;
            InnerPoint = innerPoint;
            EdgePoint = edgePoint;
        }
    }

    internal sealed class DDimnotesPlan
    {
        public ViewDir Dir { get; }
        public double MainThk { get; }
        public BBox2d MainBox { get; }
        public System.Collections.Generic.List<RectSpec> Rects { get; }
        public System.Collections.Generic.List<SideDepthDimSegment> SideDepthDimSegments { get; }
        public Point2d Anchor { get; }
        public bool MoveAlongX { get; } // true: move insertion X; false: move insertion Y
        public System.Collections.Generic.List<string> Warnings { get; }

        public DDimnotesPlan(
            ViewDir dir,
            double mainThk,
            BBox2d mainBox,
            System.Collections.Generic.List<RectSpec> rects,
            System.Collections.Generic.List<SideDepthDimSegment> sideDepthDimSegments,
            Point2d anchor,
            bool moveAlongX,
            System.Collections.Generic.List<string> warnings = null)
        {
            Dir = dir;
            MainThk = mainThk;
            MainBox = mainBox;
            Rects = rects;
            SideDepthDimSegments = sideDepthDimSegments ?? new System.Collections.Generic.List<SideDepthDimSegment>();
            Anchor = anchor;
            MoveAlongX = moveAlongX;
            Warnings = warnings ?? new System.Collections.Generic.List<string>();
        }
    }
}
