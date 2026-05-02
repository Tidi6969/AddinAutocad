using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace DDimnotes
{
    internal sealed class ThreadInfo
    {
        public string Size { get; }
        public double Pitch { get; }
        public double NominalDia { get; }
        public double CoreDia { get; }

        public ThreadInfo(string size, double pitch, double nominalDia)
            : this(size, pitch, nominalDia, nominalDia - pitch)
        {
        }

        public ThreadInfo(string size, double pitch, double nominalDia, double coreDia)
        {
            Size = size;
            Pitch = pitch;
            NominalDia = nominalDia;
            CoreDia = coreDia;
        }
    }

    internal static class ThreadTable
    {
        private static readonly object Sync = new object();
        private static readonly Regex SectionKeyRegex = new Regex(@"^\s*([A-Za-z0-9_]+)", RegexOptions.Compiled);
        private static Dictionary<string, ThreadInfo> _bySize;
        private static Dictionary<int, ThreadInfo> _byDiaKey;
        private static Dictionary<string, ThreadInfo> _mswBySize;
        private static string _cachePath;
        private static DateTime _cacheWriteTimeUtc;

        public static bool TryGetMetricThread(HoleRec h, out ThreadInfo info)
        {
            EnsureLoaded();

            info = null;
            if (h == null) return false;

            string code = (h.Code ?? string.Empty).Trim();
            if (IsSetScrewCode(code)) return false;

            bool isMetric = IsMetricThreadCode(code) || !string.IsNullOrWhiteSpace(h.MetricThreadSizeKey);
            if (!isMetric) return false;

            // First use the key prepared while reading NAME XData. This makes these two records
            // follow the same recognition path:
            //   1 m_screw0CSR 10.000  25.000 ... => M10, depth +25
            //   1 M_SCREWHO  6.000 -15.000 ...  => M6,  depth -15
            string size = NormalizeSize(h.MetricThreadSizeKey);
            if (string.IsNullOrEmpty(size))
                size = ExtractSizeKey(code, h.Dia);

            if (!string.IsNullOrEmpty(size) && _bySize.TryGetValue(size, out info))
                return true;

            // Backward-compatible fallback by nominal diameter.
            int diaKey = MakeDiaKey(h.Dia);
            if (_byDiaKey.TryGetValue(diaKey, out info))
                return true;

            return false;
        }

        public static bool TryGetSetScrewThread(HoleRec h, out ThreadInfo info)
        {
            EnsureLoaded();

            info = null;
            if (h == null || !IsSetScrewCode(h.Code)) return false;

            string size = NormalizeSize(h.MetricThreadSizeKey);
            if (string.IsNullOrEmpty(size))
                size = ExtractSizeKey(h.Code, h.Dia);

            if (!string.IsNullOrEmpty(size) && _mswBySize.TryGetValue(size, out info))
                return true;

            return false;
        }

        public static string BuildMetricThreadSizeKey(string code, double dia)
        {
            if (!IsMetricThreadCode(code) && !IsSetScrewCode(code)) return string.Empty;
            return ExtractSizeKey(code, dia);
        }

        public static bool IsSetScrewCode(string code)
        {
            return !string.IsNullOrWhiteSpace(code)
                && code.Trim().StartsWith("set_screw", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMetricThreadCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            string c = code.Trim().ToUpperInvariant();

            // DScrew/DDimnotes metric screw records normally start with M:
            // m_screw0CSR, M_SCREWHO, M_SCREW..., or direct M6/M8...
            return c.StartsWith("M", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureLoaded()
        {
            string path = FindTiDatPath();
            DateTime wt = (!string.IsNullOrEmpty(path) && File.Exists(path)) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

            if (_bySize != null && _byDiaKey != null && _mswBySize != null && string.Equals(_cachePath, path, StringComparison.OrdinalIgnoreCase) && _cacheWriteTimeUtc == wt)
                return;

            lock (Sync)
            {
                if (_bySize != null && _byDiaKey != null && _mswBySize != null && string.Equals(_cachePath, path, StringComparison.OrdinalIgnoreCase) && _cacheWriteTimeUtc == wt)
                    return;

                var bySize = new Dictionary<string, ThreadInfo>(StringComparer.OrdinalIgnoreCase);
                var byDia = new Dictionary<int, ThreadInfo>();
                var mswBySize = new Dictionary<string, ThreadInfo>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try
                    {
                        foreach (var row in ReadMScrewRows(path, "M_SCREW"))
                        {
                            string[] tok = row.Tokens;
                            string size = NormalizeSize(tok[0]);

                            double pitch;
                            double nominal;
                            if (!double.TryParse(tok[1], NumberStyles.Float, CultureInfo.InvariantCulture, out pitch))
                                continue;
                            if (!double.TryParse(tok[3], NumberStyles.Float, CultureInfo.InvariantCulture, out nominal))
                                continue;
                            if (pitch <= 0.0 || nominal <= 0.0) continue;

                            var ti = new ThreadInfo(size, pitch, nominal);
                            bySize[size] = ti;

                            int dk = MakeDiaKey(nominal);
                            if (!byDia.ContainsKey(dk)) byDia.Add(dk, ti);

                            // Do not map tap-drill diameter token[8] as a thread-size lookup key.
                            // For NAME: "1 M_SCREWHO 6.000 -15.000 ...", token[2] means M6.
                            // HOLE2MARK is not used to override the M size.
                        }

                        foreach (var row in ReadMSWRows(path))
                        {
                            string[] tok = row.Tokens;
                            string size = NormalizeSize(tok[0]);

                            double nominal;
                            double pitch;
                            double core;
                            if (!TryParseNominalFromSize(size, out nominal))
                                continue;
                            if (!double.TryParse(tok[2], NumberStyles.Float, CultureInfo.InvariantCulture, out pitch))
                                continue;
                            if (!double.TryParse(tok[4], NumberStyles.Float, CultureInfo.InvariantCulture, out core))
                                continue;
                            if (nominal <= 0.0 || pitch <= 0.0 || core <= 0.0) continue;

                            mswBySize[size] = new ThreadInfo(size, pitch, nominal, core);
                        }
                    }
                    catch
                    {
                        // If the file is invalid, metric thread drawing falls back to normal hole drawing.
                    }
                }

                _cachePath = path;
                _cacheWriteTimeUtc = wt;
                _bySize = bySize;
                _byDiaKey = byDia;
                _mswBySize = mswBySize;
            }
        }

        private sealed class ScrewRow
        {
            public string Size;
            public string[] Tokens;
        }

        private static List<ScrewRow> ReadMScrewRows(string tiDatPath, string key)
        {
            var rows = new List<ScrewRow>();
            bool inSection = false;

            foreach (string rawLine in File.ReadLines(tiDatPath))
            {
                string line = (rawLine ?? string.Empty).Trim();
                if (line.Length == 0) continue;

                if (inSection && line.StartsWith("-"))
                    break;

                if (!inSection)
                {
                    Match m = SectionKeyRegex.Match(line);
                    if (m.Success && string.Equals(m.Groups[1].Value, key, StringComparison.OrdinalIgnoreCase))
                        inSection = true;
                    continue;
                }

                string[] toks = SplitTokens(line);
                if (toks.Length < 10) continue;
                if (!Row1To8AreNumbers(toks)) continue;

                rows.Add(new ScrewRow { Size = toks[0], Tokens = toks });
            }

            return rows;
        }

        private static List<ScrewRow> ReadMSWRows(string tiDatPath)
        {
            var rows = new List<ScrewRow>();
            bool inSection = false;

            foreach (string rawLine in File.ReadLines(tiDatPath))
            {
                string line = (rawLine ?? string.Empty).Trim();
                if (line.Length == 0) continue;

                if (inSection && line.StartsWith("-"))
                    break;

                if (!inSection)
                {
                    Match m = SectionKeyRegex.Match(line);
                    if (m.Success && string.Equals(m.Groups[1].Value, "MSW", StringComparison.OrdinalIgnoreCase))
                        inSection = true;
                    continue;
                }

                string[] toks = SplitTokens(line);
                if (toks.Length < 5) continue;
                if (!toks[0].StartsWith("M", StringComparison.OrdinalIgnoreCase)) continue;
                if (!TokenIsNumber(toks[1]) || !TokenIsNumber(toks[2]) || !TokenIsNumber(toks[4])) continue;

                rows.Add(new ScrewRow { Size = toks[0], Tokens = toks });
            }

            return rows;
        }

        private static string[] SplitTokens(string line)
        {
            return line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool Row1To8AreNumbers(string[] toks)
        {
            if (toks == null || toks.Length < 9) return false;
            for (int i = 1; i <= 8; i++)
            {
                if (!TokenIsNumber(toks[i]))
                    return false;
            }
            return true;
        }

        private static bool TokenIsNumber(string token)
        {
            double tmp;
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out tmp);
        }

        private static string FindTiDatPath()
        {
            // ti.dat is a runtime data file from AutoCAD Support File Search Path.
            // UserData\ti.dat in this source ZIP is only a reference/maintenance sample.
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Database db = doc != null ? doc.Database : null;

                // Keep runtime lookup in AutoCAD Support File Search Path, but try common casing.
                string[] names = { "ti.dat", "Ti.dat", "TI.DAT" };
                foreach (string name in names)
                {
                    string p = HostApplicationServices.Current.FindFile(name, db, FindFileHint.Default);
                    if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
                }
            }
            catch { }

            return null;
        }

        private static string ExtractSizeKey(string code, double dia)
        {
            // If the code itself is like M8/M10, use that number.
            if (!string.IsNullOrEmpty(code) && code.StartsWith("M", StringComparison.OrdinalIgnoreCase) && code.Length > 1)
            {
                Match m = Regex.Match(code.ToUpperInvariant(), @"M\s*([0-9]+(?:\.[0-9]+)?)");
                if (m.Success) return "M" + m.Groups[1].Value;
            }

            // For M_SCREWHO / m_screw... / set_screw... data, NAME token[2] is the nominal M size.
            // Examples:
            //   "1 M_SCREWHO 6.000 -15.000 ..." => M6
            //   "1 set_screwBSPR 26.000 -25.000 ..." => M26
            if (dia > 0.0) return "M" + FormatDia(dia);
            return string.Empty;
        }

        private static string NormalizeSize(string size)
        {
            if (string.IsNullOrWhiteSpace(size)) return string.Empty;
            size = size.Trim().ToUpperInvariant();
            Match m = Regex.Match(size, @"M\s*([0-9]+(?:\.[0-9]+)?)");
            return m.Success ? "M" + m.Groups[1].Value : size;
        }

        private static bool TryParseNominalFromSize(string size, out double nominal)
        {
            nominal = 0.0;
            if (string.IsNullOrWhiteSpace(size)) return false;
            Match m = Regex.Match(size.Trim().ToUpperInvariant(), @"M\s*([0-9]+(?:\.[0-9]+)?)");
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out nominal);
        }

        private static int MakeDiaKey(double dia)
        {
            return (int)Math.Round(dia * 1000.0);
        }

        private static string FormatDia(double dia)
        {
            return dia.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
