using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace DDimnotes
{
    internal static class Lang
    {
        private const int MisSetLanguageLineNumber = 28;
        private const int FallbackLangId = 0; // Lang_0(English_En)

        private static int _currentLangId = FallbackLangId;
        private static bool _fallbackWarnedThisCommand = false;

        private static readonly Dictionary<int, Dictionary<string, string>> _textsByLang =
            new Dictionary<int, Dictionary<string, string>>();

        private static readonly Dictionary<int, string> _langFullNames =
            new Dictionary<int, string>();

        private static readonly Dictionary<int, string> _langShortNames =
            new Dictionary<int, string>();

        private static readonly Regex LangHeaderRegex = new Regex(
            @"^Lang_(\d+)\((.+)_([^_()]+)\)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static int CurrentLangId { get { return _currentLangId; } }

        public static string CurrentLanguageName
        {
            get
            {
                string name;
                return _langFullNames.TryGetValue(_currentLangId, out name) ? name : string.Empty;
            }
        }

        public static void BeginCommand()
        {
            _fallbackWarnedThisCommand = false;
        }

        public static void Reload()
        {
            _textsByLang.Clear();
            _langFullNames.Clear();
            _langShortNames.Clear();
            _currentLangId = FallbackLangId;

            int requestedLangId = ReadLanguageIdFromMisSet();

            string embeddedText = ReadEmbeddedLangDat();
            if (string.IsNullOrEmpty(embeddedText))
            {
                UseFallback("Không đọc được embedded lang.dat trong DLL.");
                return;
            }

            try
            {
                LoadLangDatFromText(embeddedText);
            }
            catch (System.Exception ex)
            {
                UseFallback("Lỗi đọc embedded lang.dat: " + ex.Message);
                return;
            }

            if (!HasRequiredFallbackBlock())
            {
                UseFallback("Embedded lang.dat không có Lang_0(English_En).");
                return;
            }

            if (_textsByLang.ContainsKey(requestedLangId))
            {
                _currentLangId = requestedLangId;
                return;
            }

            UseFallback("MIS_SET.SET line 28 = " + requestedLangId.ToString(CultureInfo.InvariantCulture) +
                        " nhưng không có block Lang_" + requestedLangId.ToString(CultureInfo.InvariantCulture) +
                        " trong embedded lang.dat.");
        }

        // Compatibility overload for existing project code. Lang.cs still owns lookup and fallback.
        public static void Reload(Database db, Editor ed)
        {
            Reload();
        }

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            Dictionary<string, string> current;
            if (_textsByLang.TryGetValue(_currentLangId, out current))
            {
                string value;
                if (current.TryGetValue(key, out value))
                    return DecodeValue(value);
            }

            Dictionary<string, string> fallback;
            if (_textsByLang.TryGetValue(FallbackLangId, out fallback))
            {
                string fallbackValue;
                if (fallback.TryGetValue(key, out fallbackValue))
                    return DecodeValue(fallbackValue);
            }

            return key;
        }

        public static string F(string key, params object[] args)
        {
            string fmt = T(key);
            try { return string.Format(CultureInfo.CurrentCulture, fmt, args); }
            catch { return fmt; }
        }

        private static int ReadLanguageIdFromMisSet()
        {
            string path = FindDataFile("MIS_SET.SET");

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                UseFallback("Không tìm thấy MIS_SET.SET.");
                return FallbackLangId;
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);

                // line 28 outside = index 27 in C# array.
                if (lines.Length < MisSetLanguageLineNumber)
                {
                    UseFallback("MIS_SET.SET thiếu line 28.");
                    return FallbackLangId;
                }

                string raw = (lines[MisSetLanguageLineNumber - 1] ?? string.Empty).Trim();

                int langId;
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out langId) || langId < 0)
                {
                    UseFallback("MIS_SET.SET line 28 không hợp lệ: " + raw);
                    return FallbackLangId;
                }

                return langId;
            }
            catch (System.Exception ex)
            {
                UseFallback("Lỗi đọc MIS_SET.SET: " + ex.Message);
                return FallbackLangId;
            }
        }

        private static string ReadEmbeddedLangDat()
        {
            try
            {
                Assembly asm = typeof(Lang).Assembly;
                string[] resourceNames = asm.GetManifestResourceNames();
                string resourceName = resourceNames
                    .FirstOrDefault(n => string.Equals(n, "lang.dat", StringComparison.OrdinalIgnoreCase)) ??
                    resourceNames.FirstOrDefault(n => n.EndsWith(".lang.dat", StringComparison.OrdinalIgnoreCase));

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

        private static void LoadLangDatFromText(string text)
        {
            int? currentLangId = null;
            Dictionary<string, string> currentBlock = null;

            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');

            foreach (string rawLine in lines)
            {
                string line = (rawLine ?? string.Empty).Trim();

                if (line.Length == 0)
                    continue;

                Match headerMatch = LangHeaderRegex.Match(line);

                if (headerMatch.Success)
                {
                    FlushBlock(currentLangId, currentBlock);

                    int id = int.Parse(headerMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    string fullName = headerMatch.Groups[2].Value;
                    string shortName = headerMatch.Groups[3].Value;

                    currentLangId = id;
                    currentBlock = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    _langFullNames[id] = fullName;
                    _langShortNames[id] = shortName;
                    continue;
                }

                if (line == "-")
                {
                    FlushBlock(currentLangId, currentBlock);
                    currentLangId = null;
                    currentBlock = null;
                    continue;
                }

                if (currentBlock == null)
                    continue;

                int equalIndex = line.IndexOf('=');
                if (equalIndex <= 0)
                    continue;

                string key = line.Substring(0, equalIndex).Trim();
                string value = line.Substring(equalIndex + 1);

                if (key.Length > 0)
                    currentBlock[key] = value;
            }

            FlushBlock(currentLangId, currentBlock);
        }

        private static void FlushBlock(int? langId, Dictionary<string, string> block)
        {
            if (!langId.HasValue || block == null || block.Count == 0)
                return;

            _textsByLang[langId.Value] = block;
        }

        private static bool HasRequiredFallbackBlock()
        {
            if (!_textsByLang.ContainsKey(FallbackLangId))
                return false;

            string fullName;
            string shortName;
            if (!_langFullNames.TryGetValue(FallbackLangId, out fullName) ||
                !_langShortNames.TryGetValue(FallbackLangId, out shortName))
                return false;

            return string.Equals(fullName, "English", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(shortName, "En", StringComparison.OrdinalIgnoreCase);
        }

        private static void UseFallback(string reason)
        {
            _currentLangId = FallbackLangId;

            if (_fallbackWarnedThisCommand)
                return;

            _fallbackWarnedThisCommand = true;

            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null && doc.Editor != null)
                {
                    doc.Editor.WriteMessage(
                        "\n[Language] " + reason + " Fallback: Lang_0(English_En).");
                }
            }
            catch
            {
                // Do not let fallback warning affect the main command.
            }
        }

        private static string FindDataFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            string supportPaths = GetAcadSupportPaths();

            // 1. Prefer DUser under AutoCAD Support File Search Path.
            foreach (string supportPath in SplitPaths(supportPaths))
            {
                string duserPath = Path.Combine(supportPath, "DUser", fileName);
                if (File.Exists(duserPath))
                    return duserPath;
            }

            // 2. Search directly in AutoCAD Support File Search Path.
            foreach (string supportPath in SplitPaths(supportPaths))
            {
                string directPath = Path.Combine(supportPath, fileName);
                if (File.Exists(directPath))
                    return directPath;
            }

            // 3. Search beside DLL and in DUser beside DLL.
            try
            {
                string dllDir = Path.GetDirectoryName(typeof(Lang).Assembly.Location);
                if (!string.IsNullOrEmpty(dllDir))
                {
                    string localPath = Path.Combine(dllDir, fileName);
                    if (File.Exists(localPath))
                        return localPath;

                    string localDUserPath = Path.Combine(dllDir, "DUser", fileName);
                    if (File.Exists(localDUserPath))
                        return localDUserPath;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string GetAcadSupportPaths()
        {
            try
            {
                object value = Application.GetSystemVariable("ACADPREFIX");
                return value == null ? string.Empty : value.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IEnumerable<string> SplitPaths(string paths)
        {
            if (string.IsNullOrEmpty(paths))
                yield break;

            string[] parts = paths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string p = part.Trim();
                if (p.Length > 0 && Directory.Exists(p))
                    yield return p;
            }
        }

        private static string DecodeValue(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\\n", "\n").Replace("\\t", "\t");
        }
    }
}
