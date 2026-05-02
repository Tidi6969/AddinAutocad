using System;
using System.Collections.Generic;
using System.Linq;

namespace DDimnotes
{

internal static class NoteBuilder
{
    private static string AppendTail(string cuse, string tail1)
    {
        if (string.IsNullOrWhiteSpace(tail1)) return cuse;

        // In many drawings tail is stored with a leading ".." (e.g. "..MatCat-3.0mm*1%%d").
        // If it already starts with a separator, append directly; otherwise add a '-' to keep readability.
        char c0 = tail1[0];
        if (c0 == '.' || c0 == '-' || c0 == '+' || c0 == '_' || c0 == ':')
            return cuse + tail1;

        return cuse + "-" + tail1;
    }

    public static List<HoleItem> SortByDia(List<HoleItem> items)
        => items.OrderBy(x => x.Dia).ToList();

    public static List<string> MakeHoleLines(string mark, int count, List<HoleItem> sortedItems, bool addPitch, Func<string, double, string?> pitchLookup, bool useDesc)
    {
        var lines = new List<string>();
        foreach (var it in sortedItems)
        {
            var rec = HoleLogic.Lookup4(it.Code);
            if (rec == null) continue;

            string B = rec.Value.B;
            string C = rec.Value.Process;
            string D = rec.Value.Desc;

            string cuse;
            if (it.Code.StartsWith("POS", StringComparison.OrdinalIgnoreCase) ||
                it.Code.StartsWith("GENR", StringComparison.OrdinalIgnoreCase))
            {
                cuse = C + "+" + NumUtil.Fmt2(NumUtil.Atof(it.TolStr));
            }
            else
            {
                cuse = (!string.IsNullOrWhiteSpace(it.DynMethod) ? it.DynMethod : C);
            }

            // grp=1: include nth5 tail token (same concept as grp=5 tail1)
            cuse = AppendTail(cuse, it.Tail1);
            string sizeTxt = NtConfig.FmtDia(it.Dia);

            if (addPitch && string.Equals(B, "M", StringComparison.OrdinalIgnoreCase))
            {
                string? pitch = pitchLookup != null ? pitchLookup(rec.Value.ProcKey, it.Dia) : null;
                if (!string.IsNullOrWhiteSpace(pitch))
                {
                    sizeTxt += "xP" + pitch;
                }
            }

            if (Math.Abs(it.Depth) > 1e-9)
                sizeTxt += NtConfig.DepthSuffix(it.Depth);

            string diaTxt = sizeTxt;

            string line;
            if (lines.Count == 0)
            {
                line = useDesc
                    ? $"{mark}:{count}-{B}{diaTxt}-{cuse}-{D}"
                    : $"{mark}:{count}-{B}{diaTxt}-{cuse}";
            }
            else
            {
                line = useDesc
                    ? $"    {B}{diaTxt}-{cuse}-{D}"
                    : $"    {B}{diaTxt}-{cuse}";
            }

            lines.Add(line);
        }
        return lines;
    }
}
}
