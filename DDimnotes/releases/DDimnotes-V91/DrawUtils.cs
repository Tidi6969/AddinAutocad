using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Geometry;

namespace DDimnotes.Utils
{
    internal static class DrawUtils
    {
        public static void EnsureLinetype(Database db, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ltTable = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                bool has = ltTable.Has(name);
                tr.Commit();

                if (has) return;
            }

            // LoadLineTypeFile must be done outside that read-only transaction in many patterns.
            try
            {
                db.LoadLineTypeFile(name, "acad.lin");
            }
            catch
            {
                // If loading fails (different .lin file, localization, etc.), we silently continue.
                // The entity may render as Continuous if linetype missing.
            }
        }

        public static ObjectId CreateRectBlock(Database db, List<RectSpec> rectsWorld, Point2d anchor, out string blockName)
        {
            blockName = "DDimnotes_TMP_" + Guid.NewGuid().ToString("N");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

                var btr = new BlockTableRecord();
                btr.Name = blockName;

                ObjectId btrId = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);

                foreach (var r in rectsWorld)
                {
                    Entity ent = MakeEntityLocal(r, anchor);
                    btr.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);
                }

                tr.Commit();
                return btrId;
            }
        }

        private static Entity MakeEntityLocal(RectSpec r, Point2d anchor)
        {
            Entity ent;
            switch (r.Kind)
            {
                case RectSpecKind.Polyline:
                    ent = MakePolylineLocal(r, anchor);
                    break;
                case RectSpecKind.Circle:
                    ent = MakeCircleLocal(r, anchor);
                    break;
                case RectSpecKind.Arc:
                    ent = MakeArcLocal(r, anchor);
                    break;
                case RectSpecKind.Line:
                    ent = MakeLineLocal(r, anchor);
                    break;
                case RectSpecKind.Rectangle:
                default:
                    ent = MakeRectPolylineLocal(r, anchor);
                    break;
            }

            ApplyEntityStyle(ent, r);
            return ent;
        }

        private static Polyline MakeRectPolylineLocal(RectSpec r, Point2d anchor)
        {
            double x1 = r.X1 - anchor.X;
            double x2 = r.X2 - anchor.X;
            double y1 = r.Y1 - anchor.Y;
            double y2 = r.Y2 - anchor.Y;

            double xmin = Math.Min(x1, x2), xmax = Math.Max(x1, x2);
            double ymin = Math.Min(y1, y2), ymax = Math.Max(y1, y2);

            var pl = new Polyline(4);
            pl.SetDatabaseDefaults();

            pl.AddVertexAt(0, new Point2d(xmin, ymin), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(xmax, ymin), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(xmax, ymax), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(xmin, ymax), 0, 0, 0);
            pl.Closed = true;
            return pl;
        }

        private static Polyline MakePolylineLocal(RectSpec r, Point2d anchor)
        {
            var pl = new Polyline(r.Points.Count);
            pl.SetDatabaseDefaults();

            for (int i = 0; i < r.Points.Count; i++)
            {
                Point2d p = r.Points[i];
                pl.AddVertexAt(i, new Point2d(p.X - anchor.X, p.Y - anchor.Y), 0, 0, 0);
            }
            pl.Closed = r.Closed;
            return pl;
        }

        private static Circle MakeCircleLocal(RectSpec r, Point2d anchor)
        {
            var c = new Circle(new Point3d(r.Center.X - anchor.X, r.Center.Y - anchor.Y, 0.0), Vector3d.ZAxis, r.Radius);
            c.SetDatabaseDefaults();
            return c;
        }

        private static Arc MakeArcLocal(RectSpec r, Point2d anchor)
        {
            var a = new Arc(new Point3d(r.Center.X - anchor.X, r.Center.Y - anchor.Y, 0.0), r.Radius, r.StartAngleRad, r.EndAngleRad);
            a.SetDatabaseDefaults();
            return a;
        }

        private static Line MakeLineLocal(RectSpec r, Point2d anchor)
        {
            Point2d p1 = r.Points[0];
            Point2d p2 = r.Points[1];
            var ln = new Line(new Point3d(p1.X - anchor.X, p1.Y - anchor.Y, 0.0), new Point3d(p2.X - anchor.X, p2.Y - anchor.Y, 0.0));
            ln.SetDatabaseDefaults();
            return ln;
        }

        private static void ApplyEntityStyle(Entity ent, RectSpec r)
        {
            ent.Layer = r.Layer;
            try { ent.Linetype = r.Linetype; }
            catch { ent.Linetype = "Continuous"; }

            if (r.ColorIndex.HasValue)
                ent.Color = Color.FromColorIndex(ColorMethod.ByAci, r.ColorIndex.Value);
            else
                ent.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
        }

        public static List<ObjectId> ExplodeBlockRefToCurrentSpace(Database db, BlockReference br)
        {
            // IMPORTANT:
            // Some AutoCAD versions do not fully honor the transform/position of a non-database-resident
            // BlockReference during Explode(). To ensure the preview-picked position is respected,
            // we append the block reference to current space, explode there, then erase it.

            var created = new List<ObjectId>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Append BR to the drawing so that its placement transform is definitive.
                ObjectId brId = space.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);

                // Explode to a collection and append explicitly.
                var exploded = new DBObjectCollection();
                br.Explode(exploded);

                foreach (DBObject obj in exploded)
                {
                    var ent = obj as Entity;
                    if (ent == null)
                    {
                        obj.Dispose();
                        continue;
                    }

                    ObjectId id = space.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);
                    created.Add(id);
                }

                // Erase the temporary block reference itself.
                if (!br.IsErased)
                    br.Erase(true);

                tr.Commit();
            }

            return created;
        }

        public static void TryEraseBlockDef(Database db, ObjectId blockDefId)
        {
            if (blockDefId == ObjectId.Null) return;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var btr = (BlockTableRecord)tr.GetObject(blockDefId, OpenMode.ForWrite, false);
                    if (!btr.IsErased)
                    {
                        btr.Erase(true);
                    }
                    tr.Commit();
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }
}
