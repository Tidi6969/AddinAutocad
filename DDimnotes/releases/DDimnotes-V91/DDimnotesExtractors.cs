using System;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using DDimnotes.Utils;

namespace DDimnotes
{
    internal static class DDimnotesExtractors
    {
        public static ThicknessRec? TryExtractThickness(Entity ent)
        {
            var g1000 = XDataUtils.GetMyTag1000Strings(ent);
            if (g1000 == null) return null;

            string? nameData = XDataUtils.GetAfterLabel(g1000, "NAME");
            if (string.IsNullOrWhiteSpace(nameData)) return null;

            double? thk = null;
            string kind = "UNK";

            // HDR_A: thickness = token[3]
            if (XDataUtils.HasLabelSeq_NameMaterLength(g1000))
            {
                var tok = StringUtils.SplitWs(nameData);
                if (tok.Length >= 4)
                {
                    double t;
                    if (double.TryParse(tok[3], NumberStyles.Float, CultureInfo.InvariantCulture, out t))
                    {
                        thk = t;
                        kind = "HDR_A";
                    }
                }
            }

            // grp routing
            if (!thk.HasValue)
            {
                var tok = StringUtils.SplitWs(nameData);
                if (tok.Length >= 3)
                {
                    int grp;
                    if (int.TryParse(tok[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out grp))
                    {
                        if (grp == 7)
                        {
                            // grp7: thickness only if NAME_BEFORE_MATER
                            if (XDataUtils.GetNameMaterMode(g1000) == NameMaterMode.NAME_BEFORE_MATER && tok.Length >= 4)
                            {
                                double t7;
                                if (double.TryParse(tok[3], NumberStyles.Float, CultureInfo.InvariantCulture, out t7))
                                {
                                    thk = t7;
                                    kind = "G7";
                                }
                            }
                        }
                        else if (grp == 8)
                        {
                            // grp8: thickness = token[2]
                            double t8;
                            if (double.TryParse(tok[2], NumberStyles.Float, CultureInfo.InvariantCulture, out t8))
                            {
                                thk = t8;
                                kind = "G8";
                            }
                        }
                    }
                }
            }

            if (!thk.HasValue) return null;

            var box = GeomUtils.GetBBox2d(ent);
            if (box == null) return null;

            return new ThicknessRec(
                ent.ObjectId,
                box,
                thk.Value,
                kind,
                ent.Layer,
                GeomUtils.GetColorIndex(ent)
            );
        }

        public static HoleRec? TryExtractHole(Entity ent)
        {
            var g1000 = XDataUtils.GetMyTag1000Strings(ent);
            if (g1000 == null) return null;

            string? nameData = XDataUtils.GetAfterLabel(g1000, "NAME");
            if (string.IsNullOrWhiteSpace(nameData)) return null;

            var tok = StringUtils.SplitWs(nameData);
            if (tok.Length < 5) return null;

            int grp;
            if (!int.TryParse(tok[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out grp)) return null;
            if (grp != 1) return null;

            string code = tok[1];

            double dia, depth;
            if (!double.TryParse(tok[2], NumberStyles.Float, CultureInfo.InvariantCulture, out dia)) return null;
            if (!double.TryParse(tok[3], NumberStyles.Float, CultureInfo.InvariantCulture, out depth)) return null;

            string tolstr = tok[4];

            // Metric thread family: M_SCREWHO, m_screw... and set_screw... use NAME token[2] as M size.
            // Examples:
            //   1 m_screw0CSR 10.000 25.000 ...  => M10 depth +25
            //   1 M_SCREWHO  6.000 -15.000 ...  => M6  depth -15
            // HOLE2MARK is not used to override the M size.
            string metricThreadSizeKey = ThreadTable.BuildMetricThreadSizeKey(code, dia);

            var box = GeomUtils.GetBBox2d(ent);
            if (box == null) return null;

            return new HoleRec(
                ent.ObjectId,
                box.Center,
                code,
                dia,
                depth,
                tolstr,
                ent.Layer,
                GeomUtils.GetColorIndex(ent),
                metricThreadSizeKey
            );
        }

        public static SideThreadRec? TryExtractSideThread(Entity ent)
        {
            var line = ent as Line;
            if (line == null) return null;

            var g1000 = XDataUtils.GetMyTag1000Strings(ent);
            if (g1000 == null) return null;

            string? nameData = XDataUtils.GetAfterLabel(g1000, "NAME");
            if (string.IsNullOrWhiteSpace(nameData)) return null;

            var tok = StringUtils.SplitWs(nameData);
            if (tok.Length < 5) return null;

            int grp;
            if (!int.TryParse(tok[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out grp)) return null;
            if (grp != 1) return null;

            string code = tok[1];
            if (ThreadTable.IsSetScrewCode(code)) return null;

            double dia;
            if (!double.TryParse(tok[2], NumberStyles.Float, CultureInfo.InvariantCulture, out dia)) return null;

            string metricThreadSizeKey = ThreadTable.BuildMetricThreadSizeKey(code, dia);
            if (string.IsNullOrWhiteSpace(metricThreadSizeKey)) return null;

            string tolstr = tok[4];

            return new SideThreadRec(
                ent.ObjectId,
                new Autodesk.AutoCAD.Geometry.Point2d(line.StartPoint.X, line.StartPoint.Y),
                new Autodesk.AutoCAD.Geometry.Point2d(line.EndPoint.X, line.EndPoint.Y),
                code,
                dia,
                tolstr,
                ent.Layer,
                GeomUtils.GetColorIndex(ent),
                metricThreadSizeKey
            );
        }

        public static CurverRec? TryExtractCurver(Entity ent)
        {
            var g1000 = XDataUtils.GetMyTag1000Strings(ent);
            if (g1000 == null) return null;

            // exclude HDR_A plate
            if (XDataUtils.IsHdrAPlate(g1000))
                return null;

            string? nameData = XDataUtils.GetAfterLabel(g1000, "NAME");
            if (string.IsNullOrWhiteSpace(nameData)) return null;

            var tok = StringUtils.SplitWs(nameData);
            if (tok.Length < 4) return null;

            int grp;
            if (!int.TryParse(tok[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out grp)) return null;

            double? depth = null;
            string tolstr = "0";

            if (grp == 5)
            {
                double d;
                if (!double.TryParse(tok[3], NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return null;
                depth = d;
                tolstr = (tok.Length > 4) ? tok[4] : "0";
            }
            else if (grp == 7)
            {
                var mode = XDataUtils.GetNameMaterMode(g1000);
                if (mode == NameMaterMode.NAME_ONLY || mode == NameMaterMode.MATER_BEFORE_NAME)
                {
                    double d;
                    if (!double.TryParse(tok[3], NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return null;
                    depth = d;

                    if (tok.Length > 7) tolstr = tok[7];
                    else if (tok.Length > 4) tolstr = tok[4];
                    else tolstr = "0";
                }
            }

            if (!depth.HasValue) return null;

            var box = GeomUtils.GetBBox2d(ent);
            if (box == null) return null;

            return new CurverRec(
                ent.ObjectId,
                box,
                depth.Value,
                tolstr,
                ent.Layer,
                GeomUtils.GetColorIndex(ent)
            );
        }
    }
}
