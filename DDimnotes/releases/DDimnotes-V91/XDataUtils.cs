using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace DDimnotes.Utils
{
    internal enum NameMaterMode
    {
        NONE,
        NAME_ONLY,
        NAME_BEFORE_MATER,
        MATER_BEFORE_NAME
    }

    internal static class XDataUtils
    {
        public static List<string>? GetMyTag1000Strings(DBObject dbo)
        {
            ResultBuffer? rb = null;
            try
            {
                rb = dbo.GetXDataForApplication("MY_TAG");
                if (rb == null) return null;

                var outList = new List<string>();
                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == 1000 && tv.Value is string s)
                        outList.Add(s);
                }
                return outList;
            }
            finally
            {
                // ResultBuffer from GetXDataForApplication must be disposed in some contexts.
                // Safe to dispose if not null.
                if (rb != null) rb.Dispose();
            }
        }

        public static string? GetAfterLabel(List<string> g1000, string label)
        {
            for (int i = 0; i < g1000.Count - 1; i++)
            {
                if (string.Equals(g1000[i], label, StringComparison.OrdinalIgnoreCase))
                    return g1000[i + 1];
            }
            return null;
        }

        public static bool HasLabelSeq_NameMaterLength(List<string> g1000)
        {
            bool seenName = false, seenMater = false, seenLength = false;

            foreach (string s0 in g1000)
            {
                var s = (s0 ?? "").ToUpperInvariant();

                if (!seenName && s == "NAME") { seenName = true; continue; }
                if (seenName && !seenMater && s == "MATER") { seenMater = true; continue; }
                if (seenName && seenMater && !seenLength && s == "LENGTH") { seenLength = true; break; }
            }

            return seenName && seenMater && seenLength;
        }

        public static NameMaterMode GetNameMaterMode(List<string> g1000)
        {
            int iN = g1000.FindIndex(s => string.Equals(s, "NAME", StringComparison.OrdinalIgnoreCase));
            int iM = g1000.FindIndex(s => string.Equals(s, "MATER", StringComparison.OrdinalIgnoreCase));

            if (iN >= 0 && iM < 0) return NameMaterMode.NAME_ONLY;
            if (iN >= 0 && iM >= 0 && iN < iM) return NameMaterMode.NAME_BEFORE_MATER;
            if (iN >= 0 && iM >= 0 && iM < iN) return NameMaterMode.MATER_BEFORE_NAME;
            return NameMaterMode.NONE;
        }

        // Used by the LISP to exclude "HDR_A plate" from being treated as Curver.
        public static bool IsHdrAPlate(List<string> g1000)
        {
            return GetNameMaterMode(g1000) == NameMaterMode.NAME_BEFORE_MATER;
        }
    }
}
