using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace DDimnotes
{
    internal sealed class LayerOption
    {
        public string Code { get; private set; }
        public string Value { get; private set; }
        public string RawName { get; private set; }
        public string LangKey { get; private set; }

        public LayerOption(string code, string rawName)
        {
            Code = code ?? string.Empty;
            RawName = rawName ?? string.Empty;
            Value = Code + "|" + RawName;
            LangKey = "LayerOpt_" + SanitizeKey(Code) + "_" + SanitizeKey(RawName);
        }

        public string DisplayName
        {
            get
            {
                string s = Lang.T(LangKey);
                if (string.Equals(s, LangKey, StringComparison.OrdinalIgnoreCase)) return RawName;
                return s;
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }

        private static string SanitizeKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "EMPTY";
            var chars = s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            string v = new string(chars).Trim('_');
            return string.IsNullOrEmpty(v) ? "SYM" : v;
        }
    }

    internal static class LayerCatalog
    {
        // Generated from UserData/layer.dat at build packaging time. Runtime does not read layer.dat.
        private static readonly List<LayerOption> _items = new List<LayerOption>
        {
            new LayerOption("c", "Hiện hành"),
            new LayerOption("src", "Layer đối tượng gốc"),
            new LayerOption("p", "Layer Tấm"),
            new LayerOption("d", "DIM"),
            new LayerOption("o", "*_O"),
            new LayerOption("w", "*_W"),
            new LayerOption("m", "*_M"),
            new LayerOption("_t", "*_TXT"),
            new LayerOption("t", "TEXT"),
            new LayerOption("*", "0"),
            new LayerOption("*_", "1")
        };

        public static List<LayerOption> Items()
        {
            return new List<LayerOption>(_items);
        }

        public static string DefaultValue
        {
            get { return _items.Count > 0 ? _items[0].Value : "c|Hiện hành"; }
        }

        public static string SourceObjectValue
        {
            get { return Find("src").Value; }
        }

        public static bool IsSourceObjectOption(string optionValue)
        {
            LayerOption opt = Find(optionValue);
            return opt != null && string.Equals(opt.Code, "src", StringComparison.OrdinalIgnoreCase);
        }

        public static LayerOption Find(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return _items[0];
            foreach (LayerOption item in _items)
            {
                if (string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase)) return item;
                if (string.Equals(item.Code, value, StringComparison.OrdinalIgnoreCase)) return item;
                if (string.Equals(item.RawName, value, StringComparison.OrdinalIgnoreCase)) return item;
            }

            string code;
            string raw;
            SplitValue(value, out code, out raw);
            foreach (LayerOption item in _items)
            {
                if (string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase) && string.Equals(item.RawName, raw, StringComparison.OrdinalIgnoreCase)) return item;
            }
            return _items[0];
        }

        public static string Resolve(Database db, string optionValue, string plateLayerName)
        {
            LayerOption opt = Find(optionValue);
            return Resolve(db, opt, plateLayerName);
        }

        public static string Resolve(Database db, LayerOption opt, string plateLayerName)
        {
            if (opt == null) opt = _items[0];
            string code = opt.Code ?? string.Empty;
            string raw = opt.RawName ?? string.Empty;
            string current = GetCurrentLayerName(db);
            string plate = string.IsNullOrWhiteSpace(plateLayerName) ? current : plateLayerName.Trim();
            string baseName = PlateBaseName(plate);
            string layer;

            if (string.Equals(code, "src", StringComparison.OrdinalIgnoreCase)) layer = plate;
            else if (string.Equals(code, "c", StringComparison.OrdinalIgnoreCase)) layer = current;
            else if (string.Equals(code, "p", StringComparison.OrdinalIgnoreCase)) layer = plate;
            else if (string.Equals(code, "d", StringComparison.OrdinalIgnoreCase)) layer = "DIM";
            else if (string.Equals(code, "t", StringComparison.OrdinalIgnoreCase)) layer = "TEXT";
            else if (string.Equals(code, "*", StringComparison.OrdinalIgnoreCase)) layer = raw;
            else if (string.Equals(code, "*_", StringComparison.OrdinalIgnoreCase)) layer = baseName + "_" + raw;
            else if (raw.IndexOf('*') >= 0) layer = raw.Replace("*", baseName);
            else layer = raw;

            if (string.IsNullOrWhiteSpace(layer)) layer = current;
            EnsureLayer(db, layer);
            return layer;
        }

        public static string ResolveSourceAware(Database db, string optionValue, string plateLayerName, string sourceLayerName)
        {
            if (IsSourceObjectOption(optionValue))
            {
                string layer = string.IsNullOrWhiteSpace(sourceLayerName) ? plateLayerName : sourceLayerName.Trim();
                if (string.IsNullOrWhiteSpace(layer)) layer = GetCurrentLayerName(db);
                EnsureLayer(db, layer);
                return layer;
            }
            return Resolve(db, optionValue, plateLayerName);
        }

        public static void EnsureLayer(Database db, string layerName)
        {
            if (db == null || string.IsNullOrWhiteSpace(layerName)) return;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord rec = new LayerTableRecord();
                    rec.Name = layerName;
                    lt.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                tr.Commit();
            }
        }

        private static string GetCurrentLayerName(Database db)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    LayerTableRecord rec = (LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead);
                    string name = rec.Name;
                    tr.Commit();
                    return string.IsNullOrWhiteSpace(name) ? "0" : name;
                }
            }
            catch { return "0"; }
        }

        private static string PlateBaseName(string layerName)
        {
            string s = (layerName ?? string.Empty).Trim();
            if (s.Length == 0) return "0";
            int idx = s.LastIndexOf('_');
            if (idx > 0) return s.Substring(0, idx);
            return s;
        }

        private static void SplitValue(string value, out string code, out string raw)
        {
            string s = value ?? string.Empty;
            int idx = s.IndexOf('|');
            if (idx >= 0)
            {
                code = s.Substring(0, idx);
                raw = s.Substring(idx + 1);
            }
            else
            {
                code = s;
                raw = s;
            }
        }
    }
}
