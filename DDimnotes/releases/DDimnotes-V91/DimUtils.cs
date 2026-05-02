using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

namespace DDimnotes.Utils
{
    internal static class DimUtils
    {
        private static int GetDimvarInt(string name, int fallback)
        {
            try
            {
                object v = Application.GetSystemVariable(name);
                if (v is short ss) return ss;
                if (v is int ii) return ii;
                if (v is long ll) return (int)ll;
                if (v is double dd) return (int)Math.Round(dd);
                if (v != null && int.TryParse(v.ToString(), out int parsed)) return parsed;
            }
            catch { /* ignore */ }
            return fallback;
        }

        private static string GetDimvarString(string name, string fallback)
        {
            try
            {
                object v = Application.GetSystemVariable(name);
                if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
                if (v != null)
                {
                    string t = v.ToString();
                    if (!string.IsNullOrWhiteSpace(t)) return t;
                }
            }
            catch { /* ignore */ }
            return fallback;
        }

        private static double GetDimvarDouble(string name, double fallback)
        {
            try
            {
                object v = Application.GetSystemVariable(name);
                if (v is double dd) return dd;
                if (v is float ff) return ff;
                if (v is short ss) return ss;
                if (v is int ii) return ii;
                if (v is long ll) return ll;
                if (v != null && double.TryParse(v.ToString(), out double parsed)) return parsed;
            }
            catch
            {
                // ignore
            }
            return fallback;
        }

        /// <summary>
        /// Returns (baseOffset, step) used to place dimension text/leader endpoints.
        /// </summary>
        public static (double baseOffset, double step) GetDimOffsets(double? dimtxtOverride = null, double dimOffsetScale = 3.0)
        {
            // User rule:
            //   DimOffset = dimOffsetScale * DIMTXT.
            // DIMTXT comes from user input or current dimvars.
            double dimtxt = dimtxtOverride.HasValue && dimtxtOverride.Value > 0.0
                ? dimtxtOverride.Value
                : GetDimvarDouble("DIMTXT", 2.5);

            if (double.IsNaN(dimOffsetScale) || double.IsInfinity(dimOffsetScale) || dimOffsetScale <= 0.0)
                dimOffsetScale = 3.0;

            double baseOffset = dimtxt * dimOffsetScale; // DimOffset
            double step = dimtxt * dimOffsetScale; // kept for compatibility (unused stacking)

            // Safety fallback
            if (double.IsNaN(baseOffset) || double.IsInfinity(baseOffset) || baseOffset <= 0.0)
                baseOffset = 10.0;
            if (double.IsNaN(step) || double.IsInfinity(step) || step <= 0.0)
                step = 5.0;

            return (baseOffset, step);
        }

        public static double GetCurrentDimtxt(double fallback = 2.5)
            => GetDimvarDouble("DIMTXT", fallback);

        /// <summary>
        /// Creates an effective DimStyle data record for dimensions created by DDimnotes.
        /// Standard AutoCAD .NET workflow:
        /// Database.GetDimstyleData -> edit DimStyleTableRecord copy -> Dimension.SetDimstyleData.
        /// This does not change the drawing current DIM variables or the real DimStyle record.
        /// Dimtxsty is intentionally not changed, so the dimension text style remains from
        /// the current/effective DimStyle data rather than the current TEXTSTYLE.
        /// </summary>
        public static DimStyleTableRecord CreateDDimnotesDimstyleData(Database db, double dimtxt, short dimLineColorIndex, short dimTextColorIndex)
        {
            DimStyleTableRecord data = null;

            try
            {
                // AutoCAD .NET managed API returns the effective current dimstyle data
                // as a DimStyleTableRecord. This includes the current DimStyle plus
                // drawing DIM overrides, and is the correct source before applying
                // DDimnotes/DTE-specific overrides.
                data = db.GetDimstyleData();
            }
            catch
            {
                data = new DimStyleTableRecord();
            }

            if (data == null)
                data = new DimStyleTableRecord();

            if (dimtxt <= 0.0)
                dimtxt = GetDimvarDouble("DIMTXT", 2.5);
            if (dimtxt <= 0.0 || double.IsNaN(dimtxt) || double.IsInfinity(dimtxt))
                dimtxt = 2.5;

            short lineColor = NormalizeAci(dimLineColorIndex, 3);
            short textColor = NormalizeAci(dimTextColorIndex, 7);

            // DTE-like values. Do NOT touch Dimtxsty.
            data.Dimtxt = dimtxt;
            data.Dimgap = dimtxt * 0.5;
            data.Dimtad = 1;
            data.Dimasz = dimtxt / 3.0;
            data.Dimexe = dimtxt * 0.5;
            data.Dimexo = dimtxt * 0.5;
            data.Dimatfit = 1;
            data.Dimtofl = true;
            data.Dimtmove = 0;
            data.Dimjust = 0;
            data.Dimtih = false;
            data.Dimtoh = false;
            data.Dimtix = false;
            data.Dimupt = false;
            data.Dimdec = 2;
            data.Dimzin = 8;
            data.Dimlwd = (LineWeight)9;
            data.Dimlwe = (LineWeight)9;

            // Requested exceptions from MIS_SET.SET.
            data.Dimclrd = Color.FromColorIndex(ColorMethod.ByAci, lineColor);
            data.Dimclre = Color.FromColorIndex(ColorMethod.ByAci, lineColor);
            data.Dimclrt = Color.FromColorIndex(ColorMethod.ByAci, textColor);

            TryApplyClosedBlankArrow(db, data);
            return data;
        }

        public static void ApplyDDimnotesDimstyleData(Database db, Dimension dim, DimStyleTableRecord data)
        {
            if (dim == null || data == null) return;

            try
            {
                if (db != null && !db.Dimstyle.IsNull)
                    dim.DimensionStyle = db.Dimstyle;
            }
            catch { /* ignore */ }

            try
            {
                dim.SetDimstyleData(data);
            }
            catch
            {
                // Last-resort fallback only. The normal path above is the documented API route.
                ApplyCurrentDimEnvironment(dim);
            }
        }

        private static short NormalizeAci(short value, short fallback)
        {
            if (value >= 1 && value <= 255) return value;
            if (value == 256) return value;
            return fallback;
        }

        private static void TryApplyClosedBlankArrow(Database db, DimStyleTableRecord data)
        {
            if (db == null || data == null) return;

            try
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    if (bt == null)
                    {
                        tr.Commit();
                        return;
                    }

                    ObjectId arrowId = ObjectId.Null;
                    string[] names = { "CLOSEDBLANK", "_CLOSEDBLANK", "CLOSED_BLANK", "_CLOSED_BLANK" };
                    foreach (string name in names)
                    {
                        if (bt.Has(name))
                        {
                            arrowId = bt[name];
                            break;
                        }
                    }

                    if (!arrowId.IsNull)
                    {
                        data.Dimblk = arrowId;
                        data.Dimblk1 = arrowId;
                        data.Dimblk2 = arrowId;
                    }

                    tr.Commit();
                }
            }
            catch
            {
                // Built-in arrowheads are not always normal BlockTable records.
                // Keep whatever arrow data came from GetDimstyleData in that case.
            }
        }

        /// <summary>
        /// Apply current-dimension "style overrides" similarly to the provided AutoLISP:
        /// 2.0 _(DTE)_Dimensions_Text_Size(Styles_Overrides).lsp
        ///
        /// IMPORTANT: This intentionally changes the *current* dimension variables (system variables).
        /// It is meant to affect subsequent manual DIMLINEAR operations as well.
        /// No restore is performed.
        /// </summary>
        public static void ApplyDteStyleOverrides(double dimtxt)
        {
            if (dimtxt <= 0.0) return;

            static void TrySet(string name, object value)
            {
                try { Application.SetSystemVariable(name, value); }
                catch { /* ignore */ }
            }

            // Core text size
            TrySet("DIMTXT", dimtxt);

            // Keep current DIMTXSTY (font/text style) unless user explicitly wants otherwise.
            // The reference LISP sets DIMTXSTY to "iso".
            // If you want that behavior, uncomment the next line.
            // TrySet("DIMTXSTY", "iso");

            // Gap from dimension line to text
            TrySet("DIMGAP", dimtxt * 0.5);

            // Place text above the dimension line
            TrySet("DIMTAD", 1);

            // Fixed dim and object
            TrySet("DIMASSOC", 1);

            // Arrow size
            TrySet("DIMASZ", dimtxt * 0.3);

            // Arrow block name (LISP: _CLOSEDBLANK). System variable expects just the block name.
            TrySet("DIMBLK", "CLOSEDBLANK");

            // Colors
            TrySet("DIMCLRD", 3); // Dimension line & leader color
            TrySet("DIMCLRE", 3); // Extension line color
            TrySet("DIMCLRT", 7); // Text color

            // Extension parameters
            TrySet("DIMEXE", dimtxt * 0.5); // Extension above dimension line
            TrySet("DIMEXO", dimtxt * 0.5); // Extension line origin offset

            // Fit, forcing, movement
            TrySet("DIMATFIT", 1);
            TrySet("DIMTOFL", 1);   // on
            TrySet("DIMTMOVE", 0);
            TrySet("DIMJUST", 0);
            TrySet("DIMTIH", 0);
            TrySet("DIMTOH", 0);
            TrySet("DIMTIX", 0);

            // User positioned text (final in LISP turns DIMUPT off)
            TrySet("DIMUPT", 0);

            // Numeric format
            TrySet("DIMDEC", 2);
            TrySet("DIMZIN", 8);

            // Lineweights (0.09)
            TrySet("DIMLWD", 9);
            TrySet("DIMLWE", 9);
        }

        /// <summary>
        /// Apply the DTE-style DIM system-variable setup used by the original Lisp,
        /// with only these requested differences:
        /// - DIMTXSTY/text style is NOT changed, so the active DimStyle keeps its text style.
        /// - DIMCLRD/DIMCLRE are read from MIS_SET.SET line 27.
        /// - DIMCLRT is read from MIS_SET.SET line 26.
        ///
        /// The .NET-created Dimension objects later copy these active DIM variables
        /// via ApplyCurrentDimEnvironment().
        /// </summary>
        public static void ApplyDDimnotesDimOverrides(double dimtxt, short dimLineColorIndex, short dimTextColorIndex)
        {
            if (dimtxt <= 0.0) return;

            short lineColor = (dimLineColorIndex >= 1 && dimLineColorIndex <= 255) ? dimLineColorIndex : (short)3;
            short textColor = (dimTextColorIndex >= 1 && dimTextColorIndex <= 255) ? dimTextColorIndex : (short)7;

            static void TrySet(string name, object value)
            {
                try { Application.SetSystemVariable(name, value); }
                catch { /* ignore */ }
            }

            // Match DTE Lisp values. Do NOT set DIMTXSTY here.
            TrySet("DIMTXT", dimtxt);
            TrySet("DIMGAP", dimtxt * 0.5);
            TrySet("DIMTAD", 1);
            TrySet("DIMASSOC", 1);
            TrySet("DIMASZ", dimtxt * 0.3);
            TrySet("DIMBLK", "CLOSEDBLANK");

            // Requested exceptions: colors come from MIS_SET.SET.
            TrySet("DIMCLRD", lineColor);
            TrySet("DIMCLRE", lineColor);
            TrySet("DIMCLRT", textColor);

            TrySet("DIMEXE", dimtxt * 0.5);
            TrySet("DIMEXO", dimtxt * 0.5);
            TrySet("DIMATFIT", 1);
            TrySet("DIMTOFL", 1);
            TrySet("DIMTMOVE", 0);
            TrySet("DIMJUST", 0);
            TrySet("DIMTIH", 0);
            TrySet("DIMTOH", 0);
            TrySet("DIMTIX", 0);
            TrySet("DIMUPT", 0);
            TrySet("DIMDEC", 2);
            TrySet("DIMZIN", 8);
            TrySet("DIMLWD", 9);
            TrySet("DIMLWE", 9);
        }

        public static void ApplyDimtxtOverride(Dimension dim, double dimtxt)
        {
            if (dimtxt <= 0.0) return;
            try
            {
                dim.Dimtxt = dimtxt;
            }
            catch
            {
                // Some AutoCAD versions may not expose Dimtxt; ignore.
            }
        }

        /// <summary>
        /// Apply the CURRENT dimension environment (DIM* system variables) onto a Dimension object.
        ///
        /// Creating dimensions through the .NET API does not always automatically pick up
        /// the currently-active DIM* variables ("style overrides") the same way command-based
        /// dimensions do. This copies the active environment onto the object using safe reflection.
        ///
        /// This intentionally does NOT reset anything back to a "base" dimstyle.
        /// </summary>
        public static void ApplyCurrentDimEnvironment(Dimension dim)
        {
            if (dim == null) return;

            // Pull current dimvars
            double dimtxt = GetDimvarDouble("DIMTXT", 2.5);
            double dimgap = GetDimvarDouble("DIMGAP", 0.625);
            double dimasz = GetDimvarDouble("DIMASZ", dimtxt * 0.3);
            double dimexe = GetDimvarDouble("DIMEXE", dimtxt * 0.5);
            double dimexo = GetDimvarDouble("DIMEXO", dimtxt * 0.5);

            int dimtad = GetDimvarInt("DIMTAD", 1);
            int dimjust = GetDimvarInt("DIMJUST", 0);
            int dimdec = GetDimvarInt("DIMDEC", 2);
            int dimzin = GetDimvarInt("DIMZIN", 8);
            int dimtmove = GetDimvarInt("DIMTMOVE", 0);
            int dimtofl = GetDimvarInt("DIMTOFL", 1);
            int dimtih = GetDimvarInt("DIMTIH", 0);
            int dimtoh = GetDimvarInt("DIMTOH", 0);
            int dimtix = GetDimvarInt("DIMTIX", 0);
            int dimupt = GetDimvarInt("DIMUPT", 0);
            int dimatfit = GetDimvarInt("DIMATFIT", 1);
            int dimclrd = GetDimvarInt("DIMCLRD", 3);
            int dimclre = GetDimvarInt("DIMCLRE", 3);
            int dimclrt = GetDimvarInt("DIMCLRT", 7);
            int dimlwd = GetDimvarInt("DIMLWD", -2);
            int dimlwe = GetDimvarInt("DIMLWE", -2);
            string dimblk = GetDimvarString("DIMBLK", string.Empty);

            // Safe set helper using reflection (avoids compile/runtime differences between AutoCAD versions)
            static void TrySetProp(object obj, string propName, object value)
            {
                try
                {
                    var p = obj.GetType().GetProperty(
                        propName,
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.IgnoreCase);
                    if (p == null || !p.CanWrite) return;

                    Type t = p.PropertyType;
                    object v = value;
                    if (v != null && !t.IsInstanceOfType(v))
                    {
                        // AutoCAD dimension color properties (Dimclrd/Dimclre/Dimclrt)
                        // are Autodesk.AutoCAD.Colors.Color, not raw integer ACI values.
                        // If we pass a short/int here, reflection throws and V16 silently
                        // leaves the dimension color as ByLayer.
                        if (t == typeof(Color))
                        {
                            short aci = Convert.ToInt16(v);
                            if (aci < 0) aci = 256;
                            if (aci > 256) aci = 256;
                            v = Color.FromColorIndex(ColorMethod.ByAci, aci);
                        }
                        else if (t == typeof(short)) v = Convert.ToInt16(v);
                        else if (t == typeof(int)) v = Convert.ToInt32(v);
                        else if (t == typeof(double)) v = Convert.ToDouble(v);
                        else if (t == typeof(string)) v = Convert.ToString(v);
                        else if (t.IsEnum) v = Enum.ToObject(t, Convert.ToInt32(v));
                        else return;
                    }
                    p.SetValue(obj, v, null);
                }
                catch
                {
                    // ignore
                }
            }

            // Apply the most important environment variables.
            TrySetProp(dim, "Dimtxt", dimtxt);
            TrySetProp(dim, "Dimgap", dimgap);
            TrySetProp(dim, "Dimasz", dimasz);
            TrySetProp(dim, "Dimexe", dimexe);
            TrySetProp(dim, "Dimexo", dimexo);
            TrySetProp(dim, "Dimtad", dimtad);
            TrySetProp(dim, "Dimjust", dimjust);
            TrySetProp(dim, "Dimdec", dimdec);
            TrySetProp(dim, "Dimzin", dimzin);
            TrySetProp(dim, "Dimtmove", dimtmove);
            TrySetProp(dim, "Dimtofl", dimtofl);
            TrySetProp(dim, "Dimtih", dimtih);
            TrySetProp(dim, "Dimtoh", dimtoh);
            TrySetProp(dim, "Dimtix", dimtix);
            TrySetProp(dim, "Dimupt", dimupt);
            TrySetProp(dim, "Dimatfit", dimatfit);
            TrySetProp(dim, "Dimclrd", dimclrd);
            TrySetProp(dim, "Dimclre", dimclre);
            TrySetProp(dim, "Dimclrt", dimclrt);
            TrySetProp(dim, "Dimlwd", dimlwd);
            TrySetProp(dim, "Dimlwe", dimlwe);

            // Arrow block name: different versions expose different property names.
            if (!string.IsNullOrWhiteSpace(dimblk))
            {
                TrySetProp(dim, "Dimblk", dimblk);
                TrySetProp(dim, "Dimblk1", dimblk);
                TrySetProp(dim, "Dimblk2", dimblk);
            }

            // Do not override Dimtxsty/DIMTXSTY. Text style remains from the active DimStyle.
        }

        /// <summary>
        /// Force a newly-created Dimension to use the text style stored inside the current DimStyle
        /// (db.Dimstyle), not AutoCAD's current TEXTSTYLE / DIMTXSTY environment.
        /// This matches manual dimensions created from the active DimStyle while still allowing the
        /// DTE-style overrides for size, gaps, colors, arrows, etc.
        /// </summary>
        public static void ApplyCurrentDimStyleTextStyle(Database db, Dimension dim)
        {
            if (db == null || dim == null || db.Dimstyle.IsNull) return;

            ObjectId textStyleId = ObjectId.Null;

            try
            {
                using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    DimStyleTableRecord dimStyle = tr.GetObject(db.Dimstyle, OpenMode.ForRead) as DimStyleTableRecord;
                    if (dimStyle != null)
                    {
                        textStyleId = GetObjectIdProperty(dimStyle, "Dimtxsty");
                        if (textStyleId.IsNull)
                            textStyleId = GetObjectIdProperty(dimStyle, "TextStyleId");
                    }
                    tr.Commit();
                }
            }
            catch
            {
                textStyleId = ObjectId.Null;
            }

            if (textStyleId.IsNull) return;

            TrySetObjectIdProperty(dim, "Dimtxsty", textStyleId);
            TrySetObjectIdProperty(dim, "TextStyleId", textStyleId);
        }

        private static ObjectId GetObjectIdProperty(object obj, string propName)
        {
            try
            {
                var p = obj.GetType().GetProperty(
                    propName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.IgnoreCase);
                if (p == null || !p.CanRead) return ObjectId.Null;
                object v = p.GetValue(obj, null);
                if (v is ObjectId id) return id;
            }
            catch
            {
                // ignore
            }
            return ObjectId.Null;
        }

        private static void TrySetObjectIdProperty(object obj, string propName, ObjectId value)
        {
            if (obj == null || value.IsNull) return;
            try
            {
                var p = obj.GetType().GetProperty(
                    propName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.IgnoreCase);
                if (p == null || !p.CanWrite) return;
                if (p.PropertyType == typeof(ObjectId))
                    p.SetValue(obj, value, null);
            }
            catch
            {
                // ignore
            }
        }

        public static OrdinateDimension MakeOrdinate(Database db, Point3d origin, Point3d definingPoint, Point3d leaderEndPoint, bool usingXAxis)
        {
            var od = new OrdinateDimension();
            od.SetDatabaseDefaults(db);

            od.UsingXAxis = usingXAxis;
            od.Origin = origin;
            od.DefiningPoint = definingPoint;
            od.LeaderEndPoint = leaderEndPoint;
            od.DimensionStyle = db.Dimstyle;

            return od;
        }

        public static RotatedDimension MakeRotatedDim(Database db, double rotation, Point3d xline1, Point3d xline2, Point3d dimLinePoint)
        {
            var rd = new RotatedDimension();
            rd.SetDatabaseDefaults(db);

            rd.Rotation = rotation;
            rd.XLine1Point = xline1;
            rd.XLine2Point = xline2;
            rd.DimLinePoint = dimLinePoint;
            rd.DimensionStyle = db.Dimstyle;

            return rd;
        }
    }
}
