using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DDimnotes
{
    internal static class StrUtil
    {
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

            // collapse multiple spaces
            while (t.IndexOf("  ", StringComparison.Ordinal) >= 0)
                t = ReplaceOrdinal(t, "  ", " ");

            return t.Trim();
        }

        // .NET Framework (VS2019): no Replace(string,string,StringComparison)
        private static string ReplaceOrdinal(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue)) return input;

            int prev = 0;
            int idx;
            var sb = new StringBuilder();

            while ((idx = input.IndexOf(oldValue, prev, StringComparison.Ordinal)) >= 0)
            {
                sb.Append(input, prev, idx - prev);
                sb.Append(newValue);
                prev = idx + oldValue.Length;
            }

            sb.Append(input, prev, input.Length - prev);
            return sb.ToString();
        }

        public static List<string> SplitWs(string s)
        {
            s = Norm(s);
            if (s.Length == 0) return new List<string>();
            return s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public static bool EndsWithIgnoreCase(string s, string suffix)
        {
            if (s == null) return false;
            return s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool StartsWithIgnoreCase(string s, string prefix)
        {
            if (s == null) return false;
            return s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class NumUtil
    {
        public static double Atof(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0.0;
            double v;
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        public static int Atoi(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            int v;
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            return v;
        }

        public static string Fmt2(double x)
        {
            return x.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    internal static class GeoUtil
    {
        public static Point3d Center(Entity ent)
        {
            if (ent == null) return Point3d.Origin;

            // VS2019: avoid pattern matching switch
            Line ln = ent as Line;
            if (ln != null)
            {
                return new Point3d(
                    (ln.StartPoint.X + ln.EndPoint.X) * 0.5,
                    (ln.StartPoint.Y + ln.EndPoint.Y) * 0.5,
                    (ln.StartPoint.Z + ln.EndPoint.Z) * 0.5);
            }

            Circle c = ent as Circle;
            if (c != null) return c.Center;

            Arc a = ent as Arc;
            if (a != null) return a.Center;

            Ellipse e = ent as Ellipse;
            if (e != null) return e.Center;

            try
            {
                Extents3d ex = ent.GeometricExtents;
                return new Point3d(
                    (ex.MinPoint.X + ex.MaxPoint.X) * 0.5,
                    (ex.MinPoint.Y + ex.MaxPoint.Y) * 0.5,
                    (ex.MinPoint.Z + ex.MaxPoint.Z) * 0.5);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        public static Tuple<double, double> BboxDxDy(Entity ent)
        {
            try
            {
                Extents3d ex = ent.GeometricExtents;
                double dx = Math.Abs(ex.MaxPoint.X - ex.MinPoint.X);
                double dy = Math.Abs(ex.MaxPoint.Y - ex.MinPoint.Y);
                return Tuple.Create(dx, dy);
            }
            catch
            {
                return Tuple.Create(0.0, 0.0);
            }
        }

        private static double Quant(double v, double tol)
        {
            if (tol <= 0) tol = 1.0;
            double q = v / tol;
            double r = q + (v < 0 ? -0.5 : 0.5);
            return tol * Math.Truncate(r);
        }

        public static string CenterKey(Point3d pt, double tol)
        {
            double x = Quant(pt.X, tol);
            double y = Quant(pt.Y, tol);
            double z = Quant(pt.Z, tol);

            return x.ToString("0.###", CultureInfo.InvariantCulture) + "|" +
                   y.ToString("0.###", CultureInfo.InvariantCulture) + "|" +
                   z.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static double Dist(Point3d a, Point3d b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static Point3d Polar(Point3d basePt, double angRad, double dist)
        {
            return new Point3d(
                basePt.X + dist * Math.Cos(angRad),
                basePt.Y + dist * Math.Sin(angRad),
                basePt.Z);
        }
    }

    internal static class XDataUtil
    {
        public static string GetNameDataFromMyTag(ResultBuffer xdata)
        {
            if (xdata == null) return null;

            TypedValue[] tvs = xdata.AsArray();

            // Find block after 1001 "my_tag"
            int start = -1;
            for (int i = 0; i < tvs.Length; i++)
            {
                if (tvs[i].TypeCode == 1001)
                {
                    string s = tvs[i].Value as string;
                    if (s != null && s.Equals("my_tag", StringComparison.OrdinalIgnoreCase))
                    {
                        start = i + 1;
                        break;
                    }
                }
            }
            if (start < 0) return null;

            // End at next 1001 or end
            int end = tvs.Length;
            for (int i = start; i < tvs.Length; i++)
            {
                if (tvs[i].TypeCode == 1001)
                {
                    end = i;
                    break;
                }
            }

            // Count NAME occurrences (1000 "NAME")
            int nameCount = 0;
            for (int i = start; i < end; i++)
            {
                if (tvs[i].TypeCode == 1000)
                {
                    string s = tvs[i].Value as string;
                    if (s != null && StrUtil.Norm(s).Equals("NAME", StringComparison.OrdinalIgnoreCase))
                        nameCount++;
                }
            }
            if (nameCount != 1) return null;

            // Find first 1000 after "NAME"
            for (int i = start; i < end; i++)
            {
                if (tvs[i].TypeCode == 1000)
                {
                    string s = tvs[i].Value as string;
                    if (s != null && StrUtil.Norm(s).Equals("NAME", StringComparison.OrdinalIgnoreCase))
                    {
                        for (int j = i + 1; j < end; j++)
                        {
                            if (tvs[j].TypeCode == 1000)
                            {
                                string nxt = tvs[j].Value as string;
                                if (nxt != null) return nxt;
                            }
                        }
                    }
                }
            }

            return null;
        }


        /// <summary>
        /// Return ordered list of 1000-string values inside the first my_tag XData block.
        /// This is used by DNOTES1 header parsing (NAME/MATER order-sensitive).
        /// </summary>
        public static List<string> GetMyTag1000Strings(ResultBuffer xdata)
        {
            if (xdata == null) return null;

            TypedValue[] tvs = xdata.AsArray();

            // Find block after 1001 "my_tag"
            int start = -1;
            for (int i = 0; i < tvs.Length; i++)
            {
                if (tvs[i].TypeCode == 1001)
                {
                    string s = tvs[i].Value as string;
                    if (s != null && s.Equals("my_tag", StringComparison.OrdinalIgnoreCase))
                    {
                        start = i + 1;
                        break;
                    }
                }
            }
            if (start < 0) return null;

            int end = tvs.Length;
            for (int i = start; i < tvs.Length; i++)
            {
                if (tvs[i].TypeCode == 1001)
                {
                    end = i;
                    break;
                }
            }

            var res = new List<string>();
            for (int i = start; i < end; i++)
            {
                if (tvs[i].TypeCode == 1000)
                {
                    string s = tvs[i].Value as string;
                    if (s != null) res.Add(s);
                }
            }

            return res;
        }
    }
}
