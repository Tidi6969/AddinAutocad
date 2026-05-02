using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DDimnotes
{
    internal sealed class HoleDataBlock
    {
        public readonly Dictionary<string, string> Base = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<(string Key, string Name, string Sym)> HoleProcessMap = new List<(string Key, string Name, string Sym)>();
        public readonly List<(string Tag, string Sym, string Desc)> HoleTagMap = new List<(string Tag, string Sym, string Desc)>();
        public readonly Dictionary<string, (string Sym, string Func)> CurverMap = new Dictionary<string, (string Sym, string Func)>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, (string FullName, string Extra)> PlateMap = new Dictionary<string, (string FullName, string Extra)>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> HeaderLayerMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class HoleData
    {
        private const int FallbackLangId = 0;
        private static readonly Regex HeaderRegex = new Regex(@"^HoleData_(\d+)\((.+)\)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ValueRegex = new Regex(@"<([^>]*)>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Dictionary<int, HoleDataBlock> _blocks = new Dictionary<int, HoleDataBlock>();
        private static bool _loaded;

        public static void Reload()
        {
            _loaded = false;
            _blocks.Clear();

            string text = ReadEmbeddedHoleDataDat();
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                LoadFromText(text);
                _loaded = true;
                ApplyToNtConfig(Lang.CurrentLangId);
            }
            catch
            {
                // Keep NtConfig hard-coded defaults if holedata.dat is invalid.
            }
        }

        public static void ApplyToNtConfig(int langId)
        {
            if (!_loaded || _blocks.Count == 0)
                return;

            HoleDataBlock fallback;
            if (!_blocks.TryGetValue(FallbackLangId, out fallback))
                return;

            HoleDataBlock selected;
            if (!_blocks.TryGetValue(langId, out selected))
                selected = fallback;

            HoleDataBlock merged = Merge(fallback, selected);
            NtConfig.ApplyHoleData(merged);
        }

        private static HoleDataBlock Merge(HoleDataBlock fallback, HoleDataBlock selected)
        {
            var merged = new HoleDataBlock();

            foreach (var kv in fallback.Base)
                merged.Base[kv.Key] = kv.Value;
            foreach (var kv in selected.Base)
                merged.Base[kv.Key] = kv.Value;

            var selectedProc = new Dictionary<string, (string Key, string Name, string Sym)>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in selected.HoleProcessMap)
                selectedProc[x.Key] = x;
            var seenProc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in fallback.HoleProcessMap)
            {
                (string Key, string Name, string Sym) y;
                merged.HoleProcessMap.Add(selectedProc.TryGetValue(x.Key, out y) ? y : x);
                seenProc.Add(x.Key);
            }
            foreach (var x in selected.HoleProcessMap)
            {
                if (!seenProc.Contains(x.Key))
                    merged.HoleProcessMap.Add(x);
            }

            var selectedTags = new Dictionary<string, (string Tag, string Sym, string Desc)>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in selected.HoleTagMap)
                selectedTags[x.Tag] = x;
            var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in fallback.HoleTagMap)
            {
                (string Tag, string Sym, string Desc) y;
                merged.HoleTagMap.Add(selectedTags.TryGetValue(x.Tag, out y) ? y : x);
                seenTags.Add(x.Tag);
            }
            foreach (var x in selected.HoleTagMap)
            {
                if (!seenTags.Contains(x.Tag))
                    merged.HoleTagMap.Add(x);
            }

            foreach (var kv in fallback.CurverMap)
                merged.CurverMap[kv.Key] = kv.Value;
            foreach (var kv in selected.CurverMap)
                merged.CurverMap[kv.Key] = kv.Value;

            foreach (var kv in fallback.PlateMap)
                merged.PlateMap[kv.Key] = kv.Value;
            foreach (var kv in selected.PlateMap)
                merged.PlateMap[kv.Key] = kv.Value;

            foreach (var kv in fallback.HeaderLayerMap)
                merged.HeaderLayerMap[kv.Key] = kv.Value;
            foreach (var kv in selected.HeaderLayerMap)
                merged.HeaderLayerMap[kv.Key] = kv.Value;

            return merged;
        }

        private static void LoadFromText(string text)
        {
            HoleDataBlock current = null;
            string section = null;
            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');

            foreach (string rawLine in lines)
            {
                string line = (rawLine ?? string.Empty).Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                    continue;

                if (IsSeparator(line))
                {
                    current = null;
                    section = null;
                    continue;
                }

                Match header = HeaderRegex.Match(line);
                if (header.Success)
                {
                    int id;
                    if (int.TryParse(header.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id >= 0)
                    {
                        current = new HoleDataBlock();
                        _blocks[id] = current;
                        section = null;
                    }
                    continue;
                }

                if (line.StartsWith("HoleData_", StringComparison.OrdinalIgnoreCase))
                {
                    section = line.Substring("HoleData_".Length).Trim();
                    continue;
                }

                if (current == null || string.IsNullOrWhiteSpace(section))
                    continue;

                AddLine(current, section, line);
            }
        }

        private static bool IsSeparator(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] != '-') return false;
            }
            return line.Length >= 3;
        }

        private static void AddLine(HoleDataBlock block, string section, string line)
        {
            int p = line.IndexOf('<');
            if (p <= 0)
                return;

            string key = line.Substring(0, p).Trim();
            if (key.Length == 0)
                return;

            List<string> values = ReadValues(line);
            if (values.Count == 0)
                return;

            if (section.Equals("Base", StringComparison.OrdinalIgnoreCase))
            {
                block.Base[key] = values[0] ?? string.Empty;
            }
            else if (section.Equals("HoleProcessMap", StringComparison.OrdinalIgnoreCase))
            {
                string name = values.Count > 0 ? values[0] : string.Empty;
                string sym = values.Count > 1 ? values[1] : string.Empty;
                block.HoleProcessMap.Add((key, name, sym));
            }
            else if (section.Equals("HoleTagMap", StringComparison.OrdinalIgnoreCase))
            {
                string sym = values.Count > 0 ? values[0] : string.Empty;
                string desc = values.Count > 1 ? values[1] : string.Empty;
                block.HoleTagMap.Add((key, sym, desc));
            }
            else if (section.Equals("CurverMap", StringComparison.OrdinalIgnoreCase))
            {
                string sym = values.Count > 0 ? values[0] : string.Empty;
                string func = values.Count > 1 ? values[1] : string.Empty;
                block.CurverMap[key] = (sym, func);
            }
            else if (section.Equals("PlateMap", StringComparison.OrdinalIgnoreCase))
            {
                string fullName = values.Count > 0 ? values[0] : string.Empty;
                string extra = values.Count > 1 ? values[1] : string.Empty;
                block.PlateMap[key] = (fullName, extra);
            }
            else if (section.Equals("HeaderLayerMap", StringComparison.OrdinalIgnoreCase))
            {
                block.HeaderLayerMap[key] = values[0] ?? string.Empty;
            }
        }

        private static List<string> ReadValues(string line)
        {
            var vals = new List<string>();
            MatchCollection matches = ValueRegex.Matches(line ?? string.Empty);
            foreach (Match m in matches)
                vals.Add(m.Groups[1].Value);
            return vals;
        }

        private static string ReadEmbeddedHoleDataDat()
        {
            try
            {
                Assembly asm = typeof(HoleData).Assembly;
                string[] resourceNames = asm.GetManifestResourceNames();
                string resourceName = resourceNames
                    .FirstOrDefault(n => string.Equals(n, "holedata.dat", StringComparison.OrdinalIgnoreCase)) ??
                    resourceNames.FirstOrDefault(n => n.EndsWith(".holedata.dat", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrEmpty(resourceName))
                    return null;

                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
