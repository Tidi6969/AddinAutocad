using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DDimnotes.Utils
{
    internal static class GeomUtils
    {
        public static BBox2d? GetBBox2d(Entity ent)
        {
            try
            {
                // Some entities might not have bounds in certain states.
                if (ent.Bounds == null) return null;
                Extents3d ext = ent.GeometricExtents;
                return new BBox2d(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y);
            }
            catch
            {
                return null;
            }
        }

        public static short? GetColorIndex(Entity ent)
        {
            try
            {
                if (ent.Color == null) return null;
                short idx = ent.Color.ColorIndex;
                // 256 == ByLayer
                return idx == 256 ? (short?)null : idx;
            }
            catch
            {
                return null;
            }
        }

        public static double ThkCoord(double baseCoord, double t, ViewDir dir)
        {
            // 0/90: base + t ; 180/270: base - t
            if (dir == ViewDir.Deg0 || dir == ViewDir.Deg90)
                return baseCoord + t;
            return baseCoord - t;
        }

        public static double RoundStep(double x, double step)
        {
            if (step <= 0) return x;
            return step * Math.Floor(0.5 + (x / step));
        }

        public static string CenterKey(Point2d pt, double step)
        {
            double rx = RoundStep(pt.X, step);
            double ry = RoundStep(pt.Y, step);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},{1:F3}", rx, ry);
        }

        public static BBox2d? UnionBBox(Database db, System.Collections.Generic.IEnumerable<ObjectId> ids)
        {
            // Union geometric extents of entities.
            double? xmin = null, ymin = null, xmax = null, ymax = null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    var b = GetBBox2d(ent);
                    if (b == null) continue;

                    xmin = xmin.HasValue ? Math.Min(xmin.Value, b.Xmin) : b.Xmin;
                    ymin = ymin.HasValue ? Math.Min(ymin.Value, b.Ymin) : b.Ymin;
                    xmax = xmax.HasValue ? Math.Max(xmax.Value, b.Xmax) : b.Xmax;
                    ymax = ymax.HasValue ? Math.Max(ymax.Value, b.Ymax) : b.Ymax;
                }
                tr.Commit();
            }

            if (!xmin.HasValue) return null;
            return new BBox2d(xmin.Value, ymin.Value, xmax.Value, ymax.Value);
        }
    }
}
