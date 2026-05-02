using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

// Alias
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DDimnotes
{
    // ============================================================
    // HOLE LOGIC
    // ============================================================
    internal static class HoleLogic
    {
        // Returns (sym, B, processName, desc) or null if invalid process
        public static (string Sym, string B, string Process, string Desc, string ProcKey)? Lookup4(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            // pick process by longest prefix
            var procKeys = NtConfig.KeysByLenDesc(NtConfig.HoleProcessMap.Select(x => x.Key));
            (string Key, string Name, string Sym)? proc = null;

            foreach (var k in procKeys)
            {
                if (code.StartsWith(k, StringComparison.OrdinalIgnoreCase))
                {
                    var rec = NtConfig.HoleProcessMap.First(x => x.Key.Equals(k, StringComparison.OrdinalIgnoreCase));
                    proc = rec;
                    break;
                }
            }
            if (proc == null) return null;

            string rest = code.Substring(proc.Value.Key.Length);

            // pick tag by contains, longest tag first
            var tagKeys = NtConfig.KeysByLenDesc(NtConfig.HoleTagMap.Select(x => x.Tag));
            (string Tag, string Sym, string Desc)? tag = null;

            foreach (var k in tagKeys)
            {
                if (rest.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tag = NtConfig.HoleTagMap.First(x => x.Tag.Equals(k, StringComparison.OrdinalIgnoreCase));
                    break;
                }
            }

            string sym = tag?.Sym ?? "?";
            string desc = tag?.Desc ?? "UNKNOWN";
            return (sym, proc.Value.Sym, proc.Value.Name, desc, proc.Value.Key);
        }

        public static bool IsWireHoleCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;

            // Wire hole = after process prefix startswith "WI"
            var procKeys = NtConfig.KeysByLenDesc(NtConfig.HoleProcessMap.Select(x => x.Key));
            foreach (var k in procKeys)
            {
                if (code.StartsWith(k, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = code.Substring(k.Length);
                    return rest.StartsWith("WI", StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }
    }

    // ============================================================
    // CURVER LOGIC
    // ============================================================
    internal static class CurverLogic
    {
        public static (string Key, string Sym, string Func) Lookup(string key)
        {
            if (!string.IsNullOrEmpty(key) && NtConfig.CurverMap.TryGetValue(key, out var v))
                return (key, v.Sym, v.Func);

            return (key ?? "", "?", "UNKNOWN");
        }

        public static string TailKey(string tail1, double depth, string tolStr, bool isW)
        {
            tail1 ??= "";
            string tolTxt = (!string.IsNullOrEmpty(tolStr) ? tolStr : depth.ToString("0.####", CultureInfo.InvariantCulture));
            string dSuf = NtConfig.DepthSuffix(depth); // "" or Dp..mmFront/Back

            if (isW && Math.Abs(depth) <= 1e-9)
                return tail1 + "-WC+" + tolTxt;

            return tail1 + (dSuf.Length == 0 ? "" : "-" + dSuf) + "-Mill";
        }
    }

    // ============================================================
    // GROUP LOGIC (Curver groups -> lines)
    // ============================================================
    internal static class GroupLogic
    {
        public static string CompressRanges(IEnumerable<int> nums)
        {
            var list = nums.Distinct().OrderBy(x => x).ToList();
            if (list.Count == 0) return "";

            var parts = new List<string>();
            int a = list[0], b = list[0];
            int prev = list[0];

            for (int i = 1; i < list.Count; i++)
            {
                int x = list[i];
                if (x == prev + 1)
                {
                    b = x; prev = x;
                }
                else
                {
                    parts.Add(a == b ? a.ToString() : a + "~" + b);
                    a = b = prev = x;
                }
            }

            parts.Add(a == b ? a.ToString() : a + "~" + b);
            return string.Join(",", parts);
        }

        public static List<string> CurverGroupsToLines(List<CurverGroup> groups)
        {
            var lines = new List<string>();

            foreach (var g in groups.OrderBy(x => x.TailKey, StringComparer.OrdinalIgnoreCase))
            {
                var parts = new List<string>();
                foreach (var kv in g.SymToIdxs.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string rng = CompressRanges(kv.Value);
                    parts.Add(kv.Key + rng);
                }

                string head = string.Join(";", parts);
                lines.Add(head + ":" + g.Total + "-" + g.TailKey);
            }

            return lines;
        }
    }

        // ============================================================
    // MIS_SET.SET IO
    //
    // User rules:
    //  - Line 27: Underline LINE color
    //  - Line 26: Note text color (note table lines)
    //  - Line 72: Mark color (symbols at center)
    //  - Line 59: Note text width factor
    //  - Line 66: Use DESC in hole note lines (0/1)
    //
    // Notes:
    //  - Line numbers are 1-based (as user specifies).
    //  - We store ONLY the value on each line (no "26:" prefix), but we parse leading numbers robustly anyway.
    //  - We do NOT require a fixed "DData" folder. We resolve the file similar to Ti.dat using FindFile.
    // ============================================================
    internal static class MisSetIO
    {
        // 1-based line numbers
        public const int NoteTextColorLineNo = 26;
        public const int LineColorLineNo = 27;
        public const int FactorLineNo = 59;
        public const int MarkColorLineNo = 72;
        public const int PitchToggleLineNo = 7; // 0/1: add pitch in thread notes
        public const int DescToggleLineNo = 66; // 0/1: include "-<Desc>" at end of hole note lines
        // Defaults (preserve prior behavior where underline line color came from NtConfig.LineColorIndex)
        public const short DefaultLineColor = 3;        // fallback if line 27 missing/blank
        public const short DefaultNoteTextColor = 123;   // fallback if line 26 missing/blank
        public const short DefaultMarkColor = 123;       // fallback if line 72 missing/blank
        public const double DefaultFactor = 0.88;
        public const int DefaultPitchToggle = 0;
        public const int DefaultDescToggle = 1; // preserve current behavior (use desc)

        private const string FileName = "MIS_SET.SET";

        /// <summary>
        /// Resolve MIS_SET.SET from AutoCAD Support File Search Path only.
        /// UserData\MIS_SET.SET in this source ZIP is reference-only and must not be used as runtime fallback.
        /// Missing data is handled by the command before LoadConfig is called.
        /// </summary>
        public static bool TryGetMisSetPath(Editor ed, out string misPath)
        {
            misPath = null;

            var doc = AcAp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return false;

            string[] names = { FileName, "mis_set.set", "Mis_Set.Set" };
            foreach (string name in names)
            {
                string found = TryFindFile(doc.Database, name);
                if (!string.IsNullOrEmpty(found) && File.Exists(found))
                {
                    misPath = found;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ensure MIS_SET.SET exists and has at least required line count.
        /// Only fills blank lines for required settings (does not overwrite non-empty user values).
        /// </summary>
        public static void EnsureFileExists(string misPath)
        {
            int need = Math.Max(80, Math.Max(MarkColorLineNo, Math.Max(DescToggleLineNo, Math.Max(PitchToggleLineNo, Math.Max(NoteTextColorLineNo, Math.Max(FactorLineNo, LineColorLineNo))))));

            if (File.Exists(misPath))
            {
                try
                {
                    var lines0 = File.ReadAllLines(misPath).ToList();
                    bool changed0 = false;

                    while (lines0.Count < need)
                    {
                        lines0.Add("");
                        changed0 = true;
                    }

                    if (lines0.Count >= LineColorLineNo && string.IsNullOrWhiteSpace(lines0[LineColorLineNo - 1]))
                    {
                        lines0[LineColorLineNo - 1] = DefaultLineColor.ToString(CultureInfo.InvariantCulture);
                        changed0 = true;
                    }

                    if (lines0.Count >= PitchToggleLineNo && string.IsNullOrWhiteSpace(lines0[PitchToggleLineNo - 1]))
                    {
                        lines0[PitchToggleLineNo - 1] = DefaultPitchToggle.ToString(CultureInfo.InvariantCulture);
                        changed0 = true;
                    }

                    if (lines0.Count >= DescToggleLineNo && string.IsNullOrWhiteSpace(lines0[DescToggleLineNo - 1]))
                    {
                        lines0[DescToggleLineNo - 1] = DefaultDescToggle.ToString(CultureInfo.InvariantCulture);
                        changed0 = true;
                    }
                    if (lines0.Count >= FactorLineNo && string.IsNullOrWhiteSpace(lines0[FactorLineNo - 1]))
                    {
                        lines0[FactorLineNo - 1] = DefaultFactor.ToString("0.##", CultureInfo.InvariantCulture);
                        changed0 = true;
                    }

                    if (lines0.Count >= NoteTextColorLineNo && string.IsNullOrWhiteSpace(lines0[NoteTextColorLineNo - 1]))
                    {
                        lines0[NoteTextColorLineNo - 1] = DefaultNoteTextColor.ToString(CultureInfo.InvariantCulture);
                        changed0 = true;
                    }

                    if (lines0.Count >= MarkColorLineNo && string.IsNullOrWhiteSpace(lines0[MarkColorLineNo - 1]))
                    {
                        lines0[MarkColorLineNo - 1] = DefaultMarkColor.ToString(CultureInfo.InvariantCulture);
                        changed0 = true;
                    }

                    if (changed0)
                        File.WriteAllLines(misPath, lines0);
                }
                catch
                {
                    // ignore; caller will fall back to defaults
                }
                return;
            }

            var lines = new List<string>();
            for (int i = 1; i <= need; i++)
            {
                if (i == LineColorLineNo)
                    lines.Add(DefaultLineColor.ToString(CultureInfo.InvariantCulture));
                else if (i == PitchToggleLineNo)
                    lines.Add(DefaultPitchToggle.ToString(CultureInfo.InvariantCulture));
                else if (i == DescToggleLineNo)
                    lines.Add(DefaultDescToggle.ToString(CultureInfo.InvariantCulture));
                else if (i == FactorLineNo)
                    lines.Add(DefaultFactor.ToString("0.##", CultureInfo.InvariantCulture));
                else if (i == NoteTextColorLineNo)
                    lines.Add(DefaultNoteTextColor.ToString(CultureInfo.InvariantCulture));
                else if (i == MarkColorLineNo)
                    lines.Add(DefaultMarkColor.ToString(CultureInfo.InvariantCulture));
                else
                    lines.Add("");
            }

            var dir = Path.GetDirectoryName(misPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllLines(misPath, lines);
        }

        /// <summary>
        /// Read:
        ///  - LineColor (line 27)
        ///  - Note text color (line 26)
        ///  - Mark color (line 72)
        ///  - WidthFactor (line 59)
        /// Returns false if file missing or too short.
        /// </summary>
        public static bool TryReadColorsFactor(string misPath, out short lineColorIndex, out short noteTextColorIndex, out short markColorIndex, out double factor)
        {
            lineColorIndex = DefaultLineColor;
            noteTextColorIndex = DefaultNoteTextColor;
            markColorIndex = DefaultMarkColor;
            factor = DefaultFactor;

            if (!File.Exists(misPath))
                return false;

            var lines = File.ReadAllLines(misPath).ToList();
            if (lines.Count < Math.Max(MarkColorLineNo, Math.Max(NoteTextColorLineNo, FactorLineNo)))
                return false;

            // line 27: underline line color
            var lcLine = lines[LineColorLineNo - 1] ?? "";
            if (TryParseLeadingNumber(lcLine, out var lcStr))
            {
                if (int.TryParse(lcStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ci) ||
                    int.TryParse(lcStr, out ci))
                {
                    if (ci >= 1 && ci <= 255) lineColorIndex = (short)ci;
                }
            }
            // line 26: note text color
            var ntLine = lines[NoteTextColorLineNo - 1] ?? "";
            if (TryParseLeadingNumber(ntLine, out var ntStr))
            {
                if (int.TryParse(ntStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ci) ||
                    int.TryParse(ntStr, out ci))
                {
                    if (ci >= 1 && ci <= 255) noteTextColorIndex = (short)ci;
                }
            }

            // line 72: mark color
            var mkLine = lines[MarkColorLineNo - 1] ?? "";
            if (TryParseLeadingNumber(mkLine, out var mkStr))
            {
                if (int.TryParse(mkStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ci) ||
                    int.TryParse(mkStr, out ci))
                {
                    if (ci >= 1 && ci <= 255) markColorIndex = (short)ci;
                }
            }
            // line 59: width factor
            var factorLine = lines[FactorLineNo - 1] ?? "";
            if (TryParseLeadingNumber(factorLine, out var factorStr))
            {
                if (double.TryParse(factorStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var wf) ||
                    double.TryParse(factorStr, out wf))
                {
                    if (wf > 0.0 && wf <= 5.0) factor = wf;
                }
            }

            return true;
        }
        /// <summary>
        /// Read pitch toggle from line 7.
        /// Rule: only accept 0 or 1.
        /// </summary>
        public static bool TryReadPitchToggle(string misPath, out int toggle)
        {
            toggle = DefaultPitchToggle;

            if (!File.Exists(misPath))
                return false;

            var lines = File.ReadAllLines(misPath).ToList();
            if (lines.Count < PitchToggleLineNo)
                return false;

            var tLine = lines[PitchToggleLineNo - 1] ?? "";
            if (TryParseLeadingNumber(tLine, out var s))
            {
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ||
                    int.TryParse(s, out v))
                {
                    if (v == 0 || v == 1)
                    {
                        toggle = v;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Read desc toggle from line 66.
        /// Rule: only accept 0 or 1.
        /// </summary>
        public static bool TryReadDescToggle(string misPath, out int toggle)
        {
            toggle = DefaultDescToggle;

            if (!File.Exists(misPath))
                return false;

            var lines = File.ReadAllLines(misPath).ToList();
            if (lines.Count < DescToggleLineNo)
                return false;

            var tLine = lines[DescToggleLineNo - 1] ?? "";
            if (TryParseLeadingNumber(tLine, out var s))
            {
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ||
                    int.TryParse(s, out v))
                {
                    if (v == 0 || v == 1)
                    {
                        toggle = v;
                        return true;
                    }
                }
            }
            return false;
        }


        /// <summary>
        /// Save:
        ///  - Note text color (line 26)
        ///  - WidthFactor (line 59)
        ///
        /// (Underline line color line 27 and mark color line 72 are NOT touched here.)
        /// </summary>
        public static void WriteNoteTextColorFactor(string misPath, short noteTextColorIndex, double factor)
        {
            EnsureFileExists(misPath);

            var lines = File.ReadAllLines(misPath).ToList();
            int need = Math.Max(80, Math.Max(MarkColorLineNo, Math.Max(DescToggleLineNo, Math.Max(PitchToggleLineNo, Math.Max(NoteTextColorLineNo, FactorLineNo)))));
            while (lines.Count < need) lines.Add("");

            lines[NoteTextColorLineNo - 1] = noteTextColorIndex.ToString(CultureInfo.InvariantCulture);
            lines[FactorLineNo - 1] = factor.ToString("0.##", CultureInfo.InvariantCulture);

            File.WriteAllLines(misPath, lines);
        }

        // ---------------- helpers ----------------

        private static string TryFindFile(Autodesk.AutoCAD.DatabaseServices.Database db, string fileName)
        {
            try
            {
                return Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.Current.FindFile(
                    fileName,
                    db,
                    Autodesk.AutoCAD.DatabaseServices.FindFileHint.Default
                );
            }
            catch
            {
                return null;
            }
        }

        private static string GetAppDataMisPath()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Path.GetTempPath();

            string dir = Path.Combine(baseDir, "DNOTES");
            return Path.Combine(dir, FileName);
        }

        private static string TryGetFirstWritableSupportFolder()
        {
            string acad = "";
            try
            {
                var v = AcAp.GetSystemVariable("ACAD");
                acad = v != null ? (v.ToString() ?? "") : "";
            }
            catch { }

            var paths = (acad ?? "")
                .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => (p ?? "").Trim().Trim('"'))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            foreach (var p in paths)
            {
                string dir = (p ?? "").Replace('/', '\\').Trim().TrimEnd('\\');
                if (dir.Length == 0) continue;
                if (!Directory.Exists(dir)) continue;
                if (IsFolderWritable(dir)) return dir;
            }

            return null;
        }

        private static bool IsFolderWritable(string folder)
        {
            try
            {
                string test = Path.Combine(folder, ".__nt_write_test__");
                File.WriteAllText(test, "x");
                File.Delete(test);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseLeadingNumber(string line, out string number)
        {
            number = "";
            if (string.IsNullOrWhiteSpace(line)) return false;

            string s = line.TrimStart();
            if (string.IsNullOrEmpty(s)) return false;

            int i = 0;
            if (s[i] == '+' || s[i] == '-') i++;

            bool hasDigit = false;
            bool hasDot = false;

            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsDigit(c))
                {
                    hasDigit = true;
                    continue;
                }
                if (c == '.' && !hasDot)
                {
                    hasDot = true;
                    continue;
                }
                break;
            }

            if (!hasDigit) return false;

            number = s.Substring(0, i);
            return true;
        }
    }
}
