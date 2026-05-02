using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DDimnotes
{

    internal static class DrawUtil
    {
        public static ObjectId AddText(BlockTableRecord btr, Transaction tr, string text, Point3d pos,
         double height, double widthFactor, short colorIndex, string textStyleName, string layerName = null)
        {
            var dbt = new DBText
            {
                Position = pos,
                Height = height,
                WidthFactor = widthFactor,
                TextString = text,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
            };

            if (!string.IsNullOrWhiteSpace(layerName))
            {
                try { dbt.Layer = layerName; } catch { }
            }

            ObjectId textStyleId = EnsureTextStyle(btr.Database, tr, textStyleName);
            if (!textStyleId.IsNull)
            {
                try { dbt.TextStyleId = textStyleId; } catch { }
            }

            btr.AppendEntity(dbt);
            tr.AddNewlyCreatedDBObject(dbt, true);
            return dbt.ObjectId;
        }

        private static ObjectId EnsureTextStyle(Database db, Transaction tr, string textStyleName)
        {
            if (db == null || tr == null || string.IsNullOrWhiteSpace(textStyleName))
                return ObjectId.Null;

            string name = NormalizeTextStyleName(textStyleName);
            if (string.IsNullOrWhiteSpace(name))
                return ObjectId.Null;

            try
            {
                var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                if (tst.Has(name))
                    return tst[name];

                tst.UpgradeOpen();
                var rec = new TextStyleTableRecord { Name = name };
                string fileName = FontFileForStyle(name);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    try { rec.FileName = fileName; } catch { }
                }

                ObjectId id = tst.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
                return id;
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static string NormalizeTextStyleName(string textStyleName)
        {
            string s = (textStyleName ?? string.Empty).Trim();
            if (s.Length == 0 || string.Equals(s, "Current", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (string.Equals(s, "Isocp", StringComparison.OrdinalIgnoreCase)) return "Isocp";
            if (string.Equals(s, "txt", StringComparison.OrdinalIgnoreCase)) return "txt";
            if (string.Equals(s, "Arial", StringComparison.OrdinalIgnoreCase)) return "Arial";
            return s;
        }

        private static string FontFileForStyle(string styleName)
        {
            if (string.Equals(styleName, "Isocp", StringComparison.OrdinalIgnoreCase)) return "isocp.shx";
            if (string.Equals(styleName, "txt", StringComparison.OrdinalIgnoreCase)) return "txt.shx";
            if (string.Equals(styleName, "Arial", StringComparison.OrdinalIgnoreCase)) return "arial.ttf";
            return string.Empty;
        }


        public static ObjectId AddLine(BlockTableRecord btr, Transaction tr, Point3d p1, Point3d p2, short colorIndex, bool addNoteLineXData = false, string layerName = null)
        {
            var ln = new Line(p1, p2)
            {
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };

            if (!string.IsNullOrWhiteSpace(layerName))
            {
                try { ln.Layer = layerName; } catch { }
            }

            if (addNoteLineXData)
            {
                EnsureRegApp(btr.Database, tr, "my_tag");
                ln.XData = new ResultBuffer(
                    new TypedValue(1001, "my_tag"),
                    new TypedValue(1000, "NOTE_LINE"),
                    new TypedValue(1000, " ")
                );
            }

            btr.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
            return ln.ObjectId;
        }

        private static void EnsureRegApp(Database db, Transaction tr, string appName)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(appName)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = appName };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        public static double ApproxTextWidth(string s, double h, double widthFactor)
        {
            // LISP fallback: strlen * h * 0.6
            return (s?.Length ?? 0) * h * 0.6 * widthFactor;
        }

        public static double MaxWidth(IEnumerable<string> lines, double h, double widthFactor)
        {
            double mx = 0.0;
            foreach (var ln in lines)
            {
                double w = ApproxTextWidth(ln, h, widthFactor);
                if (w > mx) mx = w;
            }
            return mx;
        }
    }
}