using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using DDimnotes.Utils;

namespace DDimnotes
{
    internal sealed class DDimnotesNoteConfig
    {
        public double NoteTextH;
        public double MarkTextH;
        public double NoteWidthFactor;
        public short LineColorIndex;
        public short NoteTextColorIndex;
        public short MarkColorIndex;
        public int PitchToggle;
        public int DescToggle;
        public string MisPath = string.Empty;
        public bool CreateMarks = true;
        public string NoteLayerName = string.Empty;
        public string MarkLayerName = string.Empty;
        public string NoteTextStyleName = string.Empty;
        public string MarkTextStyleName = string.Empty;
    }

    internal static class DDimnotesNotes
    {
        public static DDimnotesNoteConfig LoadConfig(Database db, Editor ed, double noteTextH, double noteWidthFactor, short noteTextColorIndex)
        {
            short lineColorIndex = MisSetIO.DefaultLineColor;
            short markColorIndex = MisSetIO.DefaultMarkColor;
            int pitchToggle = MisSetIO.DefaultPitchToggle;
            int descToggle = MisSetIO.DefaultDescToggle;

            string misPath;
            MisSetIO.TryGetMisSetPath(ed, out misPath);

            // Language is loaded once per DDimnotes command by Lang.Reload().

            try
            {
                double wfFromFile;
                short noteColorFromFile;
                MisSetIO.TryReadColorsFactor(misPath, out lineColorIndex, out noteColorFromFile, out markColorIndex, out wfFromFile);

                // Values changed by DDimnotes S-settings override MIS_SET values for this run.
                if (noteWidthFactor <= 0.0) noteWidthFactor = wfFromFile;
                if (noteTextColorIndex < 1 || noteTextColorIndex > 255) noteTextColorIndex = noteColorFromFile;

                try
                {
                    MisSetIO.TryReadPitchToggle(misPath, out pitchToggle);
                    if (pitchToggle != 0 && pitchToggle != 1) pitchToggle = MisSetIO.DefaultPitchToggle;
                }
                catch { pitchToggle = MisSetIO.DefaultPitchToggle; }

                try
                {
                    MisSetIO.TryReadDescToggle(misPath, out descToggle);
                    if (descToggle != 0 && descToggle != 1) descToggle = MisSetIO.DefaultDescToggle;
                }
                catch { descToggle = MisSetIO.DefaultDescToggle; }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception aex)
            {
                ed.WriteMessage(NtLang.F("Msg_MisSetError", aex.Message));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(NtLang.F("Msg_MisSetError", ex.Message));
            }

            try { MisSetIO.WriteNoteTextColorFactor(misPath, noteTextColorIndex, noteWidthFactor); } catch { }

            return new DDimnotesNoteConfig
            {
                NoteTextH = noteTextH,
                MarkTextH = 0.8 * noteTextH,
                NoteWidthFactor = noteWidthFactor,
                LineColorIndex = lineColorIndex,
                NoteTextColorIndex = noteTextColorIndex,
                MarkColorIndex = markColorIndex,
                PitchToggle = pitchToggle,
                DescToggle = descToggle,
                MisPath = misPath ?? string.Empty
            };
        }

        public static void Run(Database db, Editor ed, IEnumerable<ObjectId> ids, DDimnotesNoteConfig cfg, Point3d noteInsertPoint)
        {
            if (ids == null || cfg == null) return;

            short oldOs = 0;
            try
            {
                oldOs = Convert.ToInt16(AcAp.GetSystemVariable("OSMODE"));
                AcAp.SetSystemVariable("OSMODE", 0);
            }
            catch { }

            try
            {
                RunCore(db, ed, ids.ToList(), cfg, noteInsertPoint);
            }
            finally
            {
                try { AcAp.SetSystemVariable("OSMODE", oldOs); } catch { }
            }
        }

        private static void RunCore(Database db, Editor ed, List<ObjectId> ids, DDimnotesNoteConfig cfg, Point3d noteInsertPoint)
        {
            double centerTol = 1.0;

            var pitchTables = (cfg.PitchToggle == 1) ? TiDatPitchCache.LoadOrEmpty(db) : TiDatPitchTables.Empty;
            Func<string, double, string> pitchLookup = (procKey, dia) => pitchTables.GetPitch(procKey, dia);

            var centers = new Dictionary<string, CenterBucket>(StringComparer.OrdinalIgnoreCase);
            var curvGroups = new Dictionary<string, CurverGroup>(StringComparer.OrdinalIgnoreCase);
            var curvMarks = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
            var t7Groups = new Dictionary<string, CurverGroup>(StringComparer.OrdinalIgnoreCase);
            var t7Marks = new Dictionary<string, List<Point3d>>(StringComparer.OrdinalIgnoreCase);
            var p8Items = new List<PlateItem>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    if (id.IsNull) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    var nameDataRaw = XDataUtil.GetNameDataFromMyTag(ent.XData);
                    if (nameDataRaw == null) continue;

                    var nameData = StrUtil.Norm(nameDataRaw);
                    var lst = StrUtil.SplitWs(nameData);
                    if (lst.Count == 0) continue;

                    int grp = NumUtil.Atoi((lst.Count > 0 ? lst[0] : "") ?? "");

                    if (grp == 1)
                    {
                        string code = lst.Count > 1 ? (lst[1] ?? "") : "";
                        double dia = NumUtil.Atof((lst.Count > 2 ? lst[2] : "") ?? "");
                        double depth = NumUtil.Atof((lst.Count > 3 ? lst[3] : "") ?? "");
                        string tolStr = lst.Count > 4 ? (lst[4] ?? "") : "";
                        string tail1 = lst.Count >= 6 ? (lst[5] ?? "") : "";

                        if (NtConfig.NoteWireHole == 0 && HoleLogic.IsWireHoleCode(code))
                            continue;
                        if (HoleLogic.Lookup4(code) == null)
                            continue;

                        var center = GeoUtil.Center(ent);
                        var key = GeoUtil.CenterKey(center, centerTol);

                        var item = new HoleItem
                        {
                            Code = code,
                            Dia = dia,
                            Depth = depth,
                            TolStr = tolStr,
                            Tail1 = tail1,
                            DynMethod = "",
                            EntId = ent.ObjectId,
                            NoteData = nameData
                        };

                        CenterBucket bucket;
                        if (!centers.TryGetValue(key, out bucket))
                        {
                            bucket = new CenterBucket { Key = key, Center = center };
                            centers[key] = bucket;
                        }
                        bucket.Items.Add(item);
                    }
                    else if (grp == 5)
                    {
                        string ckey = lst.Count > 1 ? (lst[1] ?? "") : "";
                        int cidx = NumUtil.Atoi(lst.Count > 2 ? lst[2] : null);
                        double cdepth = NumUtil.Atof(lst.Count > 3 ? lst[3] : null);
                        string ctol = lst.Count > 4 ? (lst[4] ?? "") : "";
                        string tail1 = lst.Count >= 6 ? (lst[5] ?? "") : "";

                        bool isW = StrUtil.EndsWithIgnoreCase(ent.Layer ?? "", "_W");
                        var cmap = CurverLogic.Lookup(ckey);
                        string csym = cmap.Sym;
                        string cmark = csym + cidx;

                        List<ObjectId> mlocs;
                        if (!curvMarks.TryGetValue(cmark, out mlocs))
                        {
                            mlocs = new List<ObjectId>();
                            curvMarks[cmark] = mlocs;
                        }
                        mlocs.Add(ent.ObjectId);

                        string tailKey = CurverLogic.TailKey(tail1, cdepth, ctol, isW);
                        AddToCurverGroup(curvGroups, tailKey, csym, cidx);
                    }
                    else if (grp == 7)
                    {
                        string ckey = lst.Count > 1 ? (lst[1] ?? "") : "";
                        int cidx = NumUtil.Atoi(lst.Count > 2 ? lst[2] : null);
                        double thick = NumUtil.Atof(lst.Count > 3 ? lst[3] : null);

                        var cmap = CurverLogic.Lookup(ckey);
                        string csym = cmap.Sym;
                        string tailKey = NtConfig.BaseThicknessLabel + NtConfig.FmtNumClean(thick) + NtConfig.BaseUnit;
                        AddToCurverGroup(t7Groups, tailKey, csym, cidx);

                        string cmark = csym + cidx;
                        List<Point3d> pts;
                        if (!t7Marks.TryGetValue(cmark, out pts))
                        {
                            pts = new List<Point3d>();
                            t7Marks[cmark] = pts;
                        }
                        pts.Add(GeoUtil.Center(ent));
                    }
                    else if (grp == 8)
                    {
                        string plateName = lst.Count > 1 ? (lst[1] ?? "") : "";
                        if (!StrUtil.EndsWithIgnoreCase(plateName, "_BOX"))
                            continue;

                        string thkStr = lst.Count > 2 ? (lst[2] ?? "") : "";
                        string materStr = lst.Count > 3 ? (lst[3] ?? "") : "";
                        string hrcStr = lst.Count > 4 ? (lst[4] ?? "") : "";
                        string densityStr = lst.Count >= 8 ? (lst[7] ?? "") : "";

                        var bb = GeoUtil.BboxDxDy(ent);
                        p8Items.Add(new PlateItem
                        {
                            PlateName = plateName,
                            ThkStr = thkStr,
                            Dx = bb.Item1,
                            Dy = bb.Item2,
                            MaterStr = materStr,
                            DensityStr = densityStr,
                            HrcStr = hrcStr,
                        });
                    }
                }

                var sigGroups = new Dictionary<string, SigGroup>(StringComparer.OrdinalIgnoreCase);
                foreach (var bucket in centers.Values)
                {
                    foreach (var noteGroup in SplitSameCenterNotesByPlusRule(bucket.Items))
                    {
                        var sortedBundle = NoteBuilder.SortByDia(noteGroup);
                        var sigList = sortedBundle.Select(x => x.NoteData).ToList();
                        string sigKey = string.Join("||", sigList);

                        SigGroup sg;
                        if (!sigGroups.TryGetValue(sigKey, out sg))
                        {
                            sg = new SigGroup { SigKey = sigKey, Count = 0, SortedBundle = sortedBundle };
                            sigGroups[sigKey] = sg;
                        }
                        sg.Count += 1;
                        sg.SortedBundle = sortedBundle;
                        sg.Centers.Add(bucket.Center);
                    }
                }

                var symGroups = new Dictionary<string, SymGroup>(StringComparer.OrdinalIgnoreCase);
                foreach (var g in sigGroups.Values)
                {
                    HoleItem firstItem = (g.SortedBundle.Count > 0) ? g.SortedBundle[0] : null;
                    string sym = "?";
                    if (firstItem != null)
                    {
                        var rec = HoleLogic.Lookup4(firstItem.Code);
                        if (rec != null) sym = rec.Value.Sym;
                    }

                    SymGroup sgg;
                    if (!symGroups.TryGetValue(sym, out sgg))
                    {
                        sgg = new SymGroup { Sym = sym };
                        symGroups[sym] = sgg;
                    }
                    sgg.Groups.Add(g);
                }

                var plateLines = BuildPlateLines(p8Items);
                var p8HeaderLines = plateLines.Header;
                var p8DetailLines = plateLines.Detail;
                var curverLines = GroupLogic.CurverGroupsToLines(curvGroups.Values.ToList());
                var t7Lines = GroupLogic.CurverGroupsToLines(t7Groups.Values.ToList());

                var moveIds = new List<ObjectId>();
                var markIds = new List<ObjectId>();

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                double noteX = 0.0;
                double bodyTextH = cfg.NoteTextH;
                double headerTextH = cfg.NoteTextH * 1.2;
                double text1_to_line1 = headerTextH / 2.6;
                double doubleLineGap = bodyTextH / 5.7;
                double bodyLineStep = bodyTextH * 1.66;
                double bodyTextFromLine = bodyTextH * 1.17;
                double detailFromLine = bodyTextH * 1.39;
                double detailTextStep = bodyTextH * 1.7;
                double underlineRightPad = bodyTextH * 1.0;

                var placer = new MarkPlacer(cfg.MarkTextH);
                var holeBlocks = new List<HoleNoteBlock>();
                var bodyCandidates = new List<string>();

                foreach (var sg in symGroups.Values.OrderBy(x => x.Sym, StringComparer.OrdinalIgnoreCase))
                {
                    int idx = 0;
                    foreach (var g in sg.Groups.OrderBy(x => x.SigKey, StringComparer.OrdinalIgnoreCase))
                    {
                        string sym = sg.Sym;
                        string mark = (idx == 0) ? sym : sym + idx;

                        var lines = NoteBuilder.MakeHoleLines(mark, g.Count, g.SortedBundle, cfg.PitchToggle == 1, pitchLookup, cfg.DescToggle == 1);
                        holeBlocks.Add(new HoleNoteBlock { Mark = mark, Group = g, Lines = lines });
                        bodyCandidates.AddRange(lines);
                        idx++;
                    }
                }

                bodyCandidates.AddRange(curverLines);
                bodyCandidates.AddRange(t7Lines);

                double maxBodyWidth = DrawUtil.MaxWidth(bodyCandidates, bodyTextH, cfg.NoteWidthFactor);
                double xLineEndBody = noteX + maxBodyWidth + underlineRightPad;

                Func<double, double, ObjectId> addUnderline = (yLine, xEnd) =>
                    DrawUtil.AddLine(btr, tr, new Point3d(noteX, yLine, 0), new Point3d(xEnd, yLine, 0), cfg.LineColorIndex, true, cfg.NoteLayerName);

                double curLineY = 0.0;

                if (p8HeaderLines.Count > 0)
                {
                    string headerLine = p8HeaderLines[0];
                    double headerTextY = 0.0;
                    moveIds.Add(DrawUtil.AddText(btr, tr, headerLine, new Point3d(noteX, headerTextY, 0), headerTextH, cfg.NoteWidthFactor, cfg.NoteTextColorIndex, cfg.NoteTextStyleName, cfg.NoteLayerName));

                    double headerWidth = DrawUtil.ApproxTextWidth(headerLine, headerTextH, cfg.NoteWidthFactor);
                    double xLineEndHeader = noteX + headerWidth;

                    double yLine1 = headerTextY - text1_to_line1;
                    moveIds.Add(addUnderline(yLine1, xLineEndHeader));

                    double yLine2 = yLine1 - doubleLineGap;
                    moveIds.Add(addUnderline(yLine2, xLineEndHeader));

                    curLineY = yLine2;

                    for (int i = 1; i < p8HeaderLines.Count; i++)
                    {
                        string ln = p8HeaderLines[i];
                        double textY = curLineY - bodyTextFromLine;
                        double nextLineY = curLineY - bodyLineStep;
                        moveIds.Add(DrawUtil.AddText(btr, tr, ln, new Point3d(noteX, textY, 0), bodyTextH, cfg.NoteWidthFactor, cfg.NoteTextColorIndex, cfg.NoteTextStyleName, cfg.NoteLayerName));
                        moveIds.Add(addUnderline(nextLineY, xLineEndBody));
                        curLineY = nextLineY;
                    }
                }

                Action<string> drawBodyRow = (text) =>
                {
                    double textY = curLineY - bodyTextFromLine;
                    double nextLineY = curLineY - bodyLineStep;
                    moveIds.Add(DrawUtil.AddText(btr, tr, text, new Point3d(noteX, textY, 0), bodyTextH, cfg.NoteWidthFactor, cfg.NoteTextColorIndex, cfg.NoteTextStyleName, cfg.NoteLayerName));
                    moveIds.Add(addUnderline(nextLineY, xLineEndBody));
                    curLineY = nextLineY;
                };

                foreach (var hb in holeBlocks)
                {
                    var g = hb.Group;
                    int k = g.SortedBundle.Count;
                    double rmin = 1e99, rmax = 0.0;
                    foreach (var it in g.SortedBundle)
                    {
                        double rr = it.Dia / 2.0;
                        if (rr < rmin) rmin = rr;
                        if (rr > rmax) rmax = rr;
                    }

                    foreach (var cpt in g.Centers)
                    {
                        var mpt = placer.PickHoleMarkPoint(cpt, rmin, rmax, k);
                        if (cfg.CreateMarks)
                        {
                            markIds.Add(DrawUtil.AddText(btr, tr, hb.Mark, mpt, cfg.MarkTextH, cfg.NoteWidthFactor, cfg.MarkColorIndex, cfg.MarkTextStyleName, cfg.MarkLayerName));
                            placer.AddMarkPt(mpt);
                        }
                    }

                    foreach (var ln in hb.Lines)
                        drawBodyRow(ln);
                }

                foreach (var ln in curverLines)
                    drawBodyRow(ln);
                foreach (var ln in t7Lines)
                    drawBodyRow(ln);

                double bottomLine2 = curLineY - doubleLineGap;
                moveIds.Add(addUnderline(bottomLine2, xLineEndBody));
                curLineY = bottomLine2;

                if (p8DetailLines.Count > 0)
                {
                    double firstDetailY = curLineY - detailFromLine;
                    for (int i = 0; i < p8DetailLines.Count; i++)
                    {
                        double ty = firstDetailY - i * detailTextStep;
                        moveIds.Add(DrawUtil.AddText(btr, tr, p8DetailLines[i], new Point3d(noteX, ty, 0), headerTextH, cfg.NoteWidthFactor, cfg.NoteTextColorIndex, cfg.NoteTextStyleName, cfg.NoteLayerName));
                    }
                }

                foreach (var mk in curvMarks)
                {
                    string markText = mk.Key;
                    foreach (var oid in mk.Value)
                    {
                        var obj = tr.GetObject(oid, OpenMode.ForRead);
                        var pt = placer.PickCurverMarkPoint(obj, null);
                        if (cfg.CreateMarks)
                        {
                            markIds.Add(DrawUtil.AddText(btr, tr, markText, pt, cfg.MarkTextH, cfg.NoteWidthFactor, cfg.MarkColorIndex, cfg.MarkTextStyleName, cfg.MarkLayerName));
                            placer.AddMarkPt(pt);
                        }
                    }
                }

                foreach (var mk in t7Marks)
                {
                    string markText = mk.Key;
                    foreach (var p in mk.Value)
                    {
                        var pt = placer.PickCurverMarkPoint(null, p);
                        if (cfg.CreateMarks)
                        {
                            markIds.Add(DrawUtil.AddText(btr, tr, markText, pt, cfg.MarkTextH, cfg.NoteWidthFactor, cfg.MarkColorIndex, cfg.MarkTextStyleName, cfg.MarkLayerName));
                            placer.AddMarkPt(pt);
                        }
                    }
                }

                if (moveIds.Count > 0)
                {
                    Vector3d disp = Point3d.Origin.GetVectorTo(noteInsertPoint);
                    Matrix3d mat = Matrix3d.Displacement(disp);
                    foreach (ObjectId mid in moveIds)
                    {
                        var ent = tr.GetObject(mid, OpenMode.ForWrite) as Entity;
                        if (ent != null) ent.TransformBy(mat);
                    }
                }

                tr.Commit();
            }
        }

        private static IEnumerable<List<HoleItem>> SplitSameCenterNotesByPlusRule(List<HoleItem> centerItems)
        {
            if (centerItems == null || centerItems.Count == 0)
                yield break;

            if (centerItems.Count == 1)
            {
                yield return new List<HoleItem>(centerItems);
                yield break;
            }

            int n = centerItems.Count;
            bool[] visited = new bool[n];

            for (int i = 0; i < n; i++)
            {
                if (visited[i]) continue;

                var component = new List<HoleItem>();
                var stack = new Stack<int>();
                visited[i] = true;
                stack.Push(i);

                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    component.Add(centerItems[idx]);

                    for (int j = 0; j < n; j++)
                    {
                        if (visited[j]) continue;
                        if (!ShouldJoinSameCenterNoteHoles(centerItems[idx], centerItems[j])) continue;

                        visited[j] = true;
                        stack.Push(j);
                    }
                }

                yield return component;
            }
        }

        private static bool ShouldJoinSameCenterNoteHoles(HoleItem a, HoleItem b)
        {
            if (a == null || b == null) return false;

            double da = HoleNoteDiaEff(a);
            double db = HoleNoteDiaEff(b);

            // For DNOTES grouping, same center alone is not enough.
            // The '+' must come from tok1/code in the original XData, not from the formatted note text.
            // Different diameters: only join when the smaller same-center hole has '+'.
            // Same effective diameter: join when either same-center item has '+'.
            if (Math.Abs(da - db) <= 1e-9)
                return HasPlusCode(a) || HasPlusCode(b);

            HoleItem smaller = da < db ? a : b;
            return HasPlusCode(smaller);
        }

        private static bool HasPlusCode(HoleItem h)
        {
            return h != null && !string.IsNullOrEmpty(h.Code) && h.Code.IndexOf('+') >= 0;
        }

        private static double HoleNoteDiaEff(HoleItem h)
        {
            if (h == null) return 0.0;
            double tol = Math.Abs(StringUtils.ParseFirstNumberOrZero(h.TolStr));
            string code = h.Code ?? string.Empty;
            if (code.StartsWith("POS", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("GENR", StringComparison.OrdinalIgnoreCase))
                return h.Dia + 2.0 * tol;
            return h.Dia;
        }

        private class HoleNoteBlock
        {
            public string Mark = "";
            public SigGroup Group = null;
            public List<string> Lines = new List<string>();
        }

        private static void AddToCurverGroup(Dictionary<string, CurverGroup> groups, string tailKey, string sym, int idx)
        {
            CurverGroup g;
            if (!groups.TryGetValue(tailKey, out g))
            {
                g = new CurverGroup { TailKey = tailKey, Total = 0 };
                groups[tailKey] = g;
            }
            g.Total += 1;

            List<int> lst;
            if (!g.SymToIdxs.TryGetValue(sym, out lst))
            {
                lst = new List<int>();
                g.SymToIdxs[sym] = lst;
            }
            lst.Add(idx);
        }

        private static NotePlateLines BuildPlateLines(List<PlateItem> p8Items)
        {
            var header = new List<string>();
            var detail = new List<string>();

            if (p8Items.Count == 0)
            {
                header.Add("Notes: " + NtConfig.BaseUnknown);
                detail.Add(NtConfig.BaseUnknownKey + ": " + NtConfig.BaseUnknown2);
                detail.Add(NtConfig.BaseUnknownKey + ": " + NtConfig.BaseUnknown3);
                return new NotePlateLines { Header = header, Detail = detail };
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in p8Items)
            {
                string keyUC = PlateKeyUc(it.PlateName);
                string keyLC = keyUC.ToLowerInvariant();

                if (!seen.Contains(keyLC))
                {
                    string fullName, extra;
                    (string FullName, string Extra) rec;
                    if (NtConfig.PlateMap.TryGetValue(keyLC, out rec))
                    {
                        fullName = rec.FullName;
                        extra = rec.Extra;
                    }
                    else
                    {
                        fullName = NtConfig.BaseUnknown;
                        extra = "";
                    }

                    string ln = "Notes: " + keyUC + "-" + fullName + (string.IsNullOrEmpty(extra) ? "" : "-" + extra);
                    header.Add(ln);
                    seen.Add(keyLC);
                }

                string thkStr = it.ThkStr;
                double thk = NumUtil.Atof(thkStr);
                double dens = NumUtil.Atof(it.DensityStr);
                double mass = (dens * thk * it.Dx * it.Dy) / 1000000.0;
                string massStr = mass.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

                string line2 = keyUC + ":" + thkStr + NtConfig.BaseUnit + "*" +
                               NtConfig.FmtNumClean(it.Dx) + NtConfig.BaseUnit + "*" +
                               NtConfig.FmtNumClean(it.Dy) + NtConfig.BaseUnit;

                string mater = !string.IsNullOrWhiteSpace(it.MaterStr) ? it.MaterStr : NtConfig.BaseUnknown;
                string hrc = !string.IsNullOrWhiteSpace(it.HrcStr) ? it.HrcStr : NtConfig.BaseUnknown;
                string line3 = mater + ":(" + massStr + NtConfig.BaseKgUnit + ")(" + NtConfig.BaseHrcLabel + ":" + hrc + ")";

                detail.Add(line2);
                detail.Add(line3);
            }

            return new NotePlateLines { Header = header, Detail = detail };
        }

        private sealed class NotePlateLines
        {
            public List<string> Header = new List<string>();
            public List<string> Detail = new List<string>();
        }

        private static string PlateKeyUc(string plateName)
        {
            string up = (plateName ?? "").ToUpperInvariant();
            int p = up.IndexOf("_BOX", StringComparison.Ordinal);
            return p >= 0 ? up.Substring(0, p) : up;
        }
    }
}
