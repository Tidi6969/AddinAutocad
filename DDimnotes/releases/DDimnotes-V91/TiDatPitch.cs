using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Autodesk.AutoCAD.DatabaseServices;

namespace DDimnotes
{
    internal sealed class TiDatPitchTables
    {
        public static readonly TiDatPitchTables Empty = new TiDatPitchTables(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        private readonly Dictionary<string, string> _mScrewPitch; // M_SCREW: key Mx -> pitch
        private readonly Dictionary<string, string> _mswPitch;    // MSW: key Mx -> pitch

        public TiDatPitchTables(Dictionary<string, string> mScrewPitch, Dictionary<string, string> mswPitch)
        {
            _mScrewPitch = mScrewPitch ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _mswPitch = mswPitch ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get pitch string from ti.dat according to process key.
        /// - set_screw -> MSW (pitch at nth2)
        /// - m_screw / fm_screw -> M_SCREW (pitch at nth1)
        /// Returns null if not found.
        /// </summary>
        public string? GetPitch(string procKey, double dia)
        {
            if (string.IsNullOrWhiteSpace(procKey)) return null;

            string mk = "M" + NtConfig.FmtNumClean(dia);

            if (procKey.Equals("set_screw", StringComparison.OrdinalIgnoreCase))
            {
                if (_mswPitch.TryGetValue(mk, out var p) && !string.IsNullOrWhiteSpace(p))
                    return p.Trim();
                return null;
            }

            if (procKey.Equals("m_screw", StringComparison.OrdinalIgnoreCase) ||
                procKey.Equals("fm_screw", StringComparison.OrdinalIgnoreCase))
            {
                if (_mScrewPitch.TryGetValue(mk, out var p) && !string.IsNullOrWhiteSpace(p))
                    return p.Trim();
                return null;
            }

            return null;
        }
    }

    internal static class TiDatPitchCache
    {
        private static string? _cachedPath;
        private static DateTime _cachedWriteUtc;
        private static TiDatPitchTables _cachedTables = TiDatPitchTables.Empty;

        public static TiDatPitchTables LoadOrEmpty(Database db)
        {
            try
            {
                string? path = ResolveDataPath(db, "ti.dat");
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    path = ResolveDataPath(db, "Ti.dat");
                }

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return TiDatPitchTables.Empty;

                DateTime w = File.GetLastWriteTimeUtc(path);

                if (!string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) || w != _cachedWriteUtc)
                {
                    _cachedTables = ReadPitchTables(path);
                    _cachedPath = path;
                    _cachedWriteUtc = w;
                }

                return _cachedTables ?? TiDatPitchTables.Empty;
            }
            catch
            {
                return TiDatPitchTables.Empty;
            }
        }

        private static string? ResolveDataPath(Database db, string fileName)
        {
            try
            {
                return HostApplicationServices.Current.FindFile(fileName, db, FindFileHint.Default);
            }
            catch
            {
                return null;
            }
        }

        private static TiDatPitchTables ReadPitchTables(string path)
        {
            var mScrew = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var msw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            bool inMScrew = false;
            bool inMSW = false;

            foreach (var raw in File.ReadAllLines(path))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0) continue;

                if (IsSeparator(line))
                {
                    // end current section(s)
                    inMScrew = false;
                    inMSW = false;
                    continue;
                }

                if (!inMScrew && IsHeader(line, "M_SCREW"))
                {
                    inMScrew = true;
                    inMSW = false;
                    continue;
                }

                if (!inMSW && IsHeader(line, "MSW"))
                {
                    inMSW = true;
                    inMScrew = false;
                    continue;
                }

                if (inMScrew)
                {
                    // data rows start with M*
                    if (!line.StartsWith("M", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var toks = SplitRowTokens(line);
                    if (toks.Count >= 2)
                    {
                        string key = toks[0];
                        string pitch = toks[1]; // nth1
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(pitch))
                            mScrew[key] = pitch;
                    }
                    continue;
                }

                if (inMSW)
                {
                    if (!line.StartsWith("M", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var toks = SplitRowTokens(line);
                    if (toks.Count >= 3)
                    {
                        string key = toks[0];
                        string pitch = toks[2]; // nth2
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(pitch))
                            msw[key] = pitch;
                    }
                    continue;
                }
            }

            return new TiDatPitchTables(mScrew, msw);
        }

        private static bool IsHeader(string line, string key)
        {
            if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(key)) return false;
            line = line.Trim();
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return false;

            string rest = line.Substring(key.Length).TrimStart();
            if (rest.Length == 0) return false;

            char c0 = rest[0];
            if (c0 == '(' || c0 == '（' || c0 == '/' || c0 == '【' || c0 == '[') return true;
            if (c0 > 127) return true;

            return false;
        }

        private static bool IsSeparator(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            line = line.Trim();
            if (line.Length < 3) return false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '-' || c == '—' || c == '–' || c == '─' || c == '－') continue;
                return false;
            }
            return true;
        }

        private static List<string> SplitRowTokens(string line)
        {
            var list = new List<string>();
            foreach (var t0 in (line ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = (t0 ?? "").Trim();
                if (t.Length == 0) continue;

                int p = t.IndexOf('(');
                if (p > 0) t = t.Substring(0, p);

                list.Add(t);
            }
            return list;
        }
    }
}
