using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.GraphicsInterface;
using DDimnotes.Utils;

namespace DDimnotes
{
    /// <summary>
    /// DDimnotes: Run DDimnotes projection (same data & structure) and add dimensions:
    /// - Ordinate dims on original MAIN (datum = xmin,ymin) in "B" style
    /// - Ordinate dims for circle centers only (XY)
    /// - Add ordinate dims for point (0,0)
    /// - DimLinear thickness on the newly created MAIN rectangle (side view) according to the chosen direction
    /// </summary>
    public sealed class DDimnotesCommand
    {
        private enum DimnoteMeasureMode
        {
            DepthBoundary = 0,
            Boundary = 1,
            All = 2,
            NoNotes = 3
        }

        private enum BoundaryDimMode
        {
            Ordinate = 0,
            Linear = 1
        }

        private enum SidePlacementMode
        {
            Auto = 0,
            PickPoint = 1
        }

        private enum MarkPlacementMode
        {
            Auto = 0,
            Center = 1
        }

        private struct DimnotePromptResult
        {
            public double Dimtxt;
            public DimnoteMeasureMode ProcessingMode;
            public ViewDir Angle;
        }

        private sealed class RuntimeDimensionMode
        {
            public bool BoundaryXminYminDims = true;
            public bool BoundaryXminYmaxDims = false;
            public bool BoundaryXmaxYminDims = false;
            public bool BoundaryXmaxYmaxDims = true;
            public bool BoundaryCornerDims
            {
                get { return BoundaryXminYminDims || BoundaryXminYmaxDims || BoundaryXmaxYminDims || BoundaryXmaxYmaxDims; }
            }
            public bool OuterProfileVertexDims = false;
            public bool HoleCenterDims = true;
            public bool ArcCenterDims = true;
            public bool DepthHoleDims = false;
            public bool InnerProfileDims = false;
            public bool SideThicknessDims = true;
            public bool SideProjectionHoleDims = false;
            public bool SideProjectionDepthDims = false;
        }

        private sealed class RuntimeSettings
        {
            public DimnoteMeasureMode ProcessingMode = DimnoteMeasureMode.DepthBoundary;
            public ViewDir Angle = ViewDir.Deg270;
            public double NoteWidthFactor = 0.0;
            public short NoteTextColorIndex = 0;
            public double SideViewDistanceScale = 8.0;
            public double DimOffsetScale = 3.0;
            public double NoteOffsetXScale = 4.0;
            public double NoteOffsetYScale = 0.0;
            public double DimTextHeight = 0.0;
            public double DimTextScale = 1.0;
            public double MarkTextScale = 0.80;
            public short MarkColorIndex = 123;
            public short DimLineColorIndex = 3;
            public short DimTextColorIndex = 123;
            public double MarkDistanceScale = 0.60;
            public double MarkRingStepScale = 0.80;
            public double MarkMinDistanceScale = 1.50;
            public MarkPlacementMode MarkPlacement = MarkPlacementMode.Auto;
            public double MarkStartAngleDeg = 45.0;
            public double MarkAngleStepDeg = 45.0;
            public int MarkRotateTries = 8;
            public bool DeleteDuplicateDim = true;
            public RuntimeDimensionMode Mode = new RuntimeDimensionMode();

            public string TolBoundaryXminYmin = string.Empty;
            public string TolBoundaryXminYmax = string.Empty;
            public string TolBoundaryXmaxYmin = string.Empty;
            public string TolBoundaryXmaxYmax = string.Empty;
            public string TolOuterProfileVertices = string.Empty;
            public string TolHoleCenters = string.Empty;
            public string TolArcCenters = string.Empty;
            public string TolBlindHoles = string.Empty;
            public string TolInnerProfiles = string.Empty;
            public string TolSideThickness = string.Empty;
            public string TolSideProjectionHoles = string.Empty;
            public string TolSideProjectionDepths = string.Empty;
            public bool CreateNotes = true;
            public bool CreateNoteMarks = true;
            public bool NoteWireHole = false;
            public bool NoteThreadPitch = false;
            public string NoteTextStyleOption = string.Empty;
            public string MarkTextStyleOption = string.Empty;
            public string NoteInsertPositionKey = "XmaxYmax";
            public SidePlacementMode SidePlacement = SidePlacementMode.Auto;
            public BoundaryDimMode BoundaryDimMode = BoundaryDimMode.Ordinate;

            public string SideOutlineLayerOption = LayerCatalog.SourceObjectValue;
            public string SideViewLayerOption = LayerCatalog.DefaultValue;
            public string NoteLayerOption = "t|TEXT";
            public string MarkLayerOption = "t|TEXT";
            public string DimLayerOption = "d|DIM";
            public string SideHoleLayerOption = LayerCatalog.SourceObjectValue;

            public string ResolvedSideOutlineLayer = string.Empty;
            public string ResolvedSideViewLayer = string.Empty;
            public string ResolvedNoteLayer = string.Empty;
            public string ResolvedMarkLayer = string.Empty;
            public string ResolvedDimLayer = string.Empty;
            public string ResolvedSideHoleLayer = string.Empty;

            public bool UserSettingsChecked = false;
            public bool UserSettingsCanSave = false;
            public string UserSettingsPath = null;
        }

        private static readonly Dictionary<int, RuntimeSettings> _settingsByDatabase = new Dictionary<int, RuntimeSettings>();
        private static string _activeDimensionLayer = string.Empty;

        private static RuntimeSettings GetRuntimeSettings(Database db)
        {
            int key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(db);
            RuntimeSettings st;
            if (!_settingsByDatabase.TryGetValue(key, out st))
            {
                st = new RuntimeSettings();
                _settingsByDatabase[key] = st;
            }
            return st;
        }
        [CommandMethod("DDimnotes")]
        public void DDimnotes()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            Lang.BeginCommand();
            Lang.Reload();
            HoleData.Reload();

            try
            {
                // Runtime data files in UserData are reference-only. Runtime lookup must use AutoCAD Support File Search Path.
                // Fail safely instead of silently creating fallback data or letting AutoCAD crash.
                if (!RequireSupportFile(ed, db, new[] { "MIS_SET.SET", "mis_set.set", "Mis_Set.Set" }, "MIS_SET.SET"))
                    return;

                // 0) DIMTXT input FIRST. Default = last DDimnotes text height saved in DUser\DDimnotes_settings.ini;
                // if missing/invalid, fall back to current AutoCAD DIMTXT.
                double currentDimtxt = DimUtils.GetCurrentDimtxt(2.5);
                RuntimeSettings runtimeSettings = GetRuntimeSettings(db);
                EnsureUserSettingsLoaded(ed, db, runtimeSettings);
                double defaultDimtxt = runtimeSettings.DimTextHeight > 0.0 ? runtimeSettings.DimTextHeight : currentDimtxt;

                // Initialize note defaults from MIS_SET.SET once for this open drawing/session.
                DDimnotesNoteConfig initialNoteConfig = DDimnotesNotes.LoadConfig(db, ed, defaultDimtxt, runtimeSettings.NoteWidthFactor, runtimeSettings.NoteTextColorIndex);
                if (runtimeSettings.NoteWidthFactor <= 0.0)
                    runtimeSettings.NoteWidthFactor = initialNoteConfig.NoteWidthFactor;
                if (runtimeSettings.NoteTextColorIndex < 1 || runtimeSettings.NoteTextColorIndex > 255)
                    runtimeSettings.NoteTextColorIndex = initialNoteConfig.NoteTextColorIndex;

                DimnotePromptResult prompt = PromptDimtxtAndMode(ed, defaultDimtxt, runtimeSettings, db);
                double inputDimtxt = prompt.Dimtxt;
                double dimtxt = inputDimtxt * Clamp(runtimeSettings.DimTextScale, 0.01, 10.0, 1.0);
                DimnoteMeasureMode measureMode = DimnoteMeasureMode.DepthBoundary;
                ViewDir dir = prompt.Angle;

                // Build per-dimension style override data using the standard AutoCAD .NET path:
                // Database.GetDimstyleData -> edit DimStyleTableRecord copy -> Dimension.SetDimstyleData.
                // This does not change the drawing current DIM variables or DimStyle.
                DDimnotesNoteConfig noteConfig = DDimnotesNotes.LoadConfig(db, ed, dimtxt, runtimeSettings.NoteWidthFactor, runtimeSettings.NoteTextColorIndex);
                ApplyRuntimeNoteSettings(noteConfig, dimtxt, runtimeSettings);
                bool effectiveCreateNotes = runtimeSettings.CreateNotes;
                bool effectiveCreateNoteMarks = runtimeSettings.CreateNoteMarks;
                noteConfig.CreateMarks = effectiveCreateNoteMarks;
                DimStyleTableRecord ddimDimStyleData = DimUtils.CreateDDimnotesDimstyleData(
                    db,
                    dimtxt,
                    runtimeSettings.DimLineColorIndex,
                    runtimeSettings.DimTextColorIndex);

                // 1) Selection
                List<ObjectId> sel = GetSelectionIds(ed);
                if (sel.Count == 0)
                {
                    ed.WriteMessage(Lang.T("Msg_NoSelection"));
                    return;
                }

                // 2) Extract (same as DDimnotes)
                var thickness = new List<ThicknessRec>();
                var holes = new List<HoleRec>();
                var sideThreads = new List<SideThreadRec>();
                var curvers = new List<CurverRec>();
                var circles = new List<(ObjectId id, Point3d center)>();
                var arcs = new List<(ObjectId id, Point3d center)>();

                // V68: vertices used by processing checkboxes on the main view.
                // OuterProfileVertexDims = real vertices of the selected outer closed polyline.
                // InnerProfileDims = real vertices of selected closed polylines inside MAIN.
                List<Point2d> outerProfileVertices = new List<Point2d>();
                List<Point2d> innerProfileVertices = new List<Point2d>();

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in sel)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        ThicknessRec? th = DDimnotesExtractors.TryExtractThickness(ent);
                        if (th != null) thickness.Add(th);

                        SideThreadRec? st = DDimnotesExtractors.TryExtractSideThread(ent);
                        if (st != null)
                        {
                            sideThreads.Add(st);
                        }
                        else
                        {
                            HoleRec? ho = DDimnotesExtractors.TryExtractHole(ent);
                            if (ho != null) holes.Add(ho);
                        }

                        CurverRec? cu = DDimnotesExtractors.TryExtractCurver(ent);
                        if (cu != null) curvers.Add(cu);

                        if (ent is Circle c)
                        {
                            circles.Add((id, c.Center));
                        }
                        else if (ent is Arc a)
                        {
                            arcs.Add((id, a.Center));
                        }
                    }
                    tr.Commit();
                }

                if ((HasMetricThreadHole(holes) || HasMetricSideThread(sideThreads)) && !RequireSupportFile(ed, db, new[] { "ti.dat", "Ti.dat", "TI.DAT" }, "ti.dat"))
                    return;

                // 3) If no thickness source, force user pick MAIN (no auto-bbox for DDimnotes)
                if (thickness.Count == 0)
                {
                    ed.WriteMessage(Lang.T("Msg_NoThicknessSource"));
                    ObjectId? mainPick = PromptSelectMain(ed);
                    if (!mainPick.HasValue)
                    {
                        ed.WriteMessage(Lang.T("Msg_Cancelled"));
                        return;
                    }
                    ThicknessRec? mainRecPick = MakeUserOuterMain(db, mainPick.Value);
                    if (mainRecPick != null) thickness.Add(mainRecPick);
                }

                if (thickness.Count == 0)
                {
                    ed.WriteMessage(Lang.T("Msg_NoMain"));
                    return;
                }

                // 4) Choose MAIN (same rule)
                ThicknessRec mainRec = thickness
                    .OrderByDescending(r => r.Box.Area)
                    .ThenByDescending(r => r.Thickness)
                    .First();

                outerProfileVertices = CollectOuterProfileVertices(db, mainRec);
                innerProfileVertices = CollectInnerProfileVertices(db, sel, mainRec);

                if (!ValidateRuntimeLayerCompatibility(ed, runtimeSettings))
                    return;

                runtimeSettings.ResolvedSideOutlineLayer = LayerCatalog.ResolveSourceAware(db, runtimeSettings.SideOutlineLayerOption, mainRec.Layer, mainRec.Layer);
                runtimeSettings.ResolvedSideViewLayer = LayerCatalog.IsSourceObjectOption(runtimeSettings.SideViewLayerOption)
                    ? string.Empty
                    : LayerCatalog.Resolve(db, runtimeSettings.SideViewLayerOption, mainRec.Layer);
                runtimeSettings.ResolvedNoteLayer = LayerCatalog.Resolve(db, runtimeSettings.NoteLayerOption, mainRec.Layer);
                runtimeSettings.ResolvedMarkLayer = LayerCatalog.Resolve(db, runtimeSettings.MarkLayerOption, mainRec.Layer);
                runtimeSettings.ResolvedDimLayer = LayerCatalog.Resolve(db, runtimeSettings.DimLayerOption, mainRec.Layer);
                runtimeSettings.ResolvedSideHoleLayer = runtimeSettings.ResolvedSideViewLayer;
                ApplyRuntimeNoteSettings(noteConfig, dimtxt, runtimeSettings);

                // 5) Thickness
                double mainThk = PromptThickness(ed, mainRec.Thickness);

                // 6) Direction angle is controlled by S -> G setting. Default is 270 degrees per open drawing.

                // DDimnotes datum is fixed: (xmin, ymin) of MAIN
                Point3d datum = new Point3d(mainRec.Box.Xmin, mainRec.Box.Ymin, 0.0);

                // 6.2) Dimension insertion baselines are computed automatically (no extra prompts)
                // DimOffset (outside box) = settings DimOffsetScale * DIMTXT.
                var (dimOffset, _) = DimUtils.GetDimOffsets(dimtxt, runtimeSettings.DimOffsetScale);
                // Baselines around the MAIN (original) bbox. We will pick the nearest side per point/group.
                double xLineBottomY = mainRec.Box.Ymin - dimOffset;
                double xLineTopY = mainRec.Box.Ymax + dimOffset;
                double yLineLeftX = mainRec.Box.Xmin - dimOffset;
                double yLineRightX = mainRec.Box.Xmax + dimOffset;

// 7) Build & draw view side (same as DDimnotes)
                DrawUtils.EnsureLinetype(db, "HIDDEN");
                DrawUtils.EnsureLinetype(db, "CENTER");
                // Keep the side view separated from the MAIN boundary so it doesn't "stick" to min/max.
                // View gap rule: keep the generated side view separated from the MAIN boundary.
                // Side-view spacing is controlled in Settings as a multiplier of DIM text height.
                double gapFromMain = Math.Max(0.01, runtimeSettings.SideViewDistanceScale) * dimtxt;

                // Mode D: projection uses only depth-based detail objects.
                // MAIN is still required for the outside rectangle.
                List<ThicknessRec> planThickness = thickness;
                List<HoleRec> planHoles = holes;
                List<CurverRec> planCurvers = curvers;
                if (UsesDepthBoundaryProjection(measureMode))
                {
                    planThickness = new List<ThicknessRec> { mainRec };
                    // Mode D: choose representative depth-hole groups.
                    // A depth-hole group means one center group that has at least one hole Depth != 0,
                    // plus every same-center through hole Depth == 0 that completes that stepped hole.
                    // Groups with the same hole data are de-duplicated, but the kept representative is chosen
                    // to minimize overlap in the generated side view.
                    planHoles = SelectDepthRepresentativeHolesForProjection(holes, dir);
                    planCurvers = curvers.Where(c => Math.Abs(c.Depth) > 1e-9).ToList();
                }


                DDimnotesPlan plan = DDimnotesPlanner.BuildPlan(dir, mainRec, mainThk, planThickness, planHoles, sideThreads, planCurvers, gapFromMain, runtimeSettings.ResolvedSideOutlineLayer, runtimeSettings.ResolvedSideViewLayer, runtimeSettings.ResolvedSideHoleLayer);
                if (plan.Warnings != null && plan.Warnings.Count > 0)
                {
                    foreach (string warning in plan.Warnings)
                    {
                        if (!string.IsNullOrWhiteSpace(warning))
                            ed.WriteMessage("\n" + warning);
                    }
                }

                string blkName;
                ObjectId blkDefId = DrawUtils.CreateRectBlock(db, plan.Rects, plan.Anchor, out blkName);

                Point3d sideInsertPoint = new Point3d(plan.Anchor.X, plan.Anchor.Y, 0.0);
                Vector3d sideMoveTotal = new Vector3d(0.0, 0.0, 0.0);

                BlockReference br = new BlockReference(sideInsertPoint, blkDefId);
                br.SetDatabaseDefaults(db);

                // Create the side view first at the automatic position. If the user selected
                // custom placement, run a MOVE-like DrawJig that previews the whole created
                // group under the cursor and commits the transform only after OK.
                List<ObjectId> created = DrawUtils.ExplodeBlockRefToCurrentSpace(db, br);
                DrawUtils.TryEraseBlockDef(db, blkDefId);

                if (runtimeSettings.SidePlacement == SidePlacementMode.PickPoint && created.Count > 0)
                {
                    Vector3d sideMove;
                    if (DragCreatedObjectsWithOrtho(ed, db, created, sideInsertPoint, out sideMove))
                    {
                        sideInsertPoint = sideInsertPoint + sideMove;
                        sideMoveTotal = sideMoveTotal + sideMove;
                    }
                    else
                    {
                        // ESC/cancel keeps the side view at the automatic position.
                        ed.WriteMessage(Lang.T("Msg_SideViewKeptAutoPosition"));
                    }
                }

                // Leave the just-created side-view entities selected for quick inspection/grip editing.
                try
                {
                    ed.SetImpliedSelection(created.ToArray());
                    ed.WriteMessage(Lang.T("Msg_ObjectsCreatedSelected"));
                }
                catch
                {
                    // ignore selection issues
                }

                // Identify the newly created MAIN rectangle (Continuous linetype) for thickness dim
                BBox2d? sideMainBox = FindSideMainBox(db, created);
                if (sideMainBox == null)
                {
                    // fallback: use first rect spec transformed (best effort)
                    sideMainBox = GuessSideMainBoxFromCreated(db, created);
                }

                // 8) Create dimensions
                _activeDimensionLayer = runtimeSettings.ResolvedDimLayer;
                CreateDims(db, datum, mainRec, circles, arcs, holes, outerProfileVertices, innerProfileVertices, planHoles, plan.SideDepthDimSegments, sideMoveTotal, sideMainBox, dir, mainThk, dimtxt, runtimeSettings.DimOffsetScale, ddimDimStyleData,
                    xLineBottomY, xLineTopY, yLineLeftX, yLineRightX, runtimeSettings, created);

                // 9) Write DNOTES table and marks using the same selection.
                // Notes anchor is based on the total layout extents after the side view is created.
                // Offset scales are applied in DIM text-height units.
                BBox2d? sideLayoutBox = GeomUtils.UnionBBox(db, created);
                Point3d noteInsertPoint = ComputeNoteInsertPoint(
                    mainRec.Box,
                    sideLayoutBox,
                    runtimeSettings.NoteInsertPositionKey,
                    dimtxt,
                    runtimeSettings.NoteOffsetXScale,
                    runtimeSettings.NoteOffsetYScale);
                if (string.Equals(runtimeSettings.NoteInsertPositionKey, "PickPoint", StringComparison.OrdinalIgnoreCase))
                {
                    Point3d? pickedNotePoint = PromptPointOrNull(ed, Lang.T("Prompt_PickNotePosition"));
                    if (pickedNotePoint.HasValue) noteInsertPoint = pickedNotePoint.Value;
                }

                if (effectiveCreateNotes)
                {
                    noteConfig.CreateMarks = effectiveCreateNoteMarks;
                    DDimnotesNotes.Run(db, ed, sel, noteConfig, noteInsertPoint);
                }

                ed.WriteMessage(Lang.F(
                    "Msg_Done",
                    ModeToKeyword(measureMode),
                    (int)dir,
                    created.Count,
                    circles.Count,
                    effectiveCreateNotes ? "ON" : "OFF"));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(Lang.F("Msg_Error", ex.Message));
            }
        }

        private sealed class HoleCenterGroup
        {
            public List<HoleRec> Items = new List<HoleRec>();
            public string Signature = string.Empty;
            public double PlanMin;
            public double PlanMax;
        }

        private static List<HoleRec> SelectDepthRepresentativeHolesForProjection(List<HoleRec> holes, ViewDir dir)
        {
            const double centerTol = 1.0;
            const double depthTol = 1e-9;

            bool thkAxisIsY = (dir == ViewDir.Deg90 || dir == ViewDir.Deg270);

            var candidatesBySignature = new Dictionary<string, List<HoleCenterGroup>>(StringComparer.OrdinalIgnoreCase);

            foreach (List<HoleRec> holeGroup in BuildHoleStepGroups(holes, centerTol))
            {
                if (!holeGroup.Any(h => Math.Abs(h.Depth) > depthTol))
                    continue;

                HoleCenterGroup group = new HoleCenterGroup();
                group.Items = holeGroup
                    .OrderByDescending(h => Math.Abs(h.Depth) > depthTol)
                    .ThenByDescending(h => Math.Abs(h.Depth))
                    .ThenByDescending(h => h.Dia)
                    .ToList();
                group.Signature = BuildHoleGroupSignature(group.Items);

                double planCenter = thkAxisIsY ? group.Items[0].Center.X : group.Items[0].Center.Y;
                double half = group.Items.Max(h => HoleDiaEffForFilter(h) / 2.0);
                group.PlanMin = planCenter - half;
                group.PlanMax = planCenter + half;

                List<HoleCenterGroup> sigList;
                if (!candidatesBySignature.TryGetValue(group.Signature, out sigList))
                {
                    sigList = new List<HoleCenterGroup>();
                    candidatesBySignature[group.Signature] = sigList;
                }
                sigList.Add(group);
            }

            var selected = new List<HoleCenterGroup>();

            foreach (var sigPair in candidatesBySignature
                .OrderByDescending(kv => kv.Value.Max(g => g.PlanMax - g.PlanMin))
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                HoleCenterGroup best = sigPair.Value
                    .OrderBy(g => IntervalOverlapScore(g, selected))
                    .ThenByDescending(g => NearestGapScore(g, selected))
                    .ThenBy(g => g.PlanMin)
                    .First();

                selected.Add(best);
            }

            return selected.SelectMany(g => g.Items).ToList();
        }

        private static List<List<HoleRec>> BuildHoleStepGroups(List<HoleRec> holes, double centerTol)
        {
            var byCenter = new Dictionary<string, List<HoleRec>>(StringComparer.OrdinalIgnoreCase);
            foreach (HoleRec h in holes)
            {
                string centerKey = GeomUtils.CenterKey(h.Center, centerTol);
                List<HoleRec> list;
                if (!byCenter.TryGetValue(centerKey, out list))
                {
                    list = new List<HoleRec>();
                    byCenter[centerKey] = list;
                }
                list.Add(h);
            }

            var result = new List<List<HoleRec>>();
            foreach (List<HoleRec> centerItems in byCenter.Values)
            {
                result.AddRange(SplitSameCenterByPlusRule(centerItems));
            }

            return result;
        }

        private static IEnumerable<List<HoleRec>> SplitSameCenterByPlusRule(List<HoleRec> centerItems)
        {
            if (centerItems == null || centerItems.Count == 0)
                yield break;

            if (centerItems.Count == 1)
            {
                yield return new List<HoleRec>(centerItems);
                yield break;
            }

            int n = centerItems.Count;
            bool[] visited = new bool[n];

            for (int i = 0; i < n; i++)
            {
                if (visited[i]) continue;

                var component = new List<HoleRec>();
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
                        if (!ShouldJoinSameCenterHoles(centerItems[idx], centerItems[j])) continue;

                        visited[j] = true;
                        stack.Push(j);
                    }
                }

                yield return component;
            }
        }

        private static bool ShouldJoinSameCenterHoles(HoleRec a, HoleRec b)
        {
            if (a == null || b == null) return false;

            double da = HoleDiaEffForFilter(a);
            double db = HoleDiaEffForFilter(b);

            // Original stepped-hole rule: same center alone is not enough.
            // The smaller hole must be marked by '+' in tok1/code.
            // Example: genTSCR6+ can join with the larger same-center hole.
            // Example: M_SCREWHO + GENPIN without '+' must stay independent.
            if (Math.Abs(da - db) <= 1e-9)
                return HasPlusCode(a) || HasPlusCode(b);

            HoleRec smaller = da < db ? a : b;
            return HasPlusCode(smaller);
        }

        private static bool HasPlusCode(HoleRec h)
        {
            return h != null && !string.IsNullOrEmpty(h.Code) && h.Code.IndexOf('+') >= 0;
        }

        private static string BuildHoleGroupSignature(List<HoleRec> items)
        {
            var parts = items
                .Select(h => string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}|{1:0.########}|{2:0.########}|{3:0.########}",
                    (h.Code ?? string.Empty).Trim().ToUpperInvariant(),
                    h.Dia,
                    h.Depth,
                    Math.Abs(StringUtils.ParseFirstNumberOrZero(h.TolStr))))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            return string.Join(";", parts);
        }

        private static double HoleDiaEffForFilter(HoleRec h)
        {
            double tol = Math.Abs(StringUtils.ParseFirstNumberOrZero(h.TolStr));
            string code = h.Code ?? string.Empty;
            if (code.StartsWith("POS", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("GENR", StringComparison.OrdinalIgnoreCase))
                return h.Dia + 2.0 * tol;
            return h.Dia;
        }

        private static double IntervalOverlapScore(HoleCenterGroup candidate, List<HoleCenterGroup> selected)
        {
            double score = 0.0;
            foreach (HoleCenterGroup other in selected)
            {
                double overlap = Math.Min(candidate.PlanMax, other.PlanMax) - Math.Max(candidate.PlanMin, other.PlanMin);
                if (overlap > 0.0)
                    score += overlap;
            }
            return score;
        }

        private static double NearestGapScore(HoleCenterGroup candidate, List<HoleCenterGroup> selected)
        {
            if (selected.Count == 0) return 0.0;

            double bestGap = 0.0;
            foreach (HoleCenterGroup other in selected)
            {
                double gap;
                if (candidate.PlanMax < other.PlanMin)
                    gap = other.PlanMin - candidate.PlanMax;
                else if (other.PlanMax < candidate.PlanMin)
                    gap = candidate.PlanMin - other.PlanMax;
                else
                    gap = 0.0;

                if (gap > bestGap) bestGap = gap;
            }
            return bestGap;
        }


        private sealed class MainOrdinateStackState
        {
            public readonly List<double> BottomX = new List<double>();
            public readonly List<double> TopX = new List<double>();
            public readonly List<double> LeftY = new List<double>();
            public readonly List<double> RightY = new List<double>();
        }

        private static double StackMainXLeader(MainOrdinateStackState state, bool isBottom, double desiredX, double minSpacing)
        {
            if (state == null) return desiredX;

            // Bottom side stacks toward +X, top side stacks toward -X.
            // This keeps Xmin/Ymin behaviour unchanged and prevents Xmax/Ymax side
            // leaders from being pushed entirely toward the max corner.
            return StackLeaderCoordList(
                desiredX,
                isBottom ? state.BottomX : state.TopX,
                minSpacing,
                isBottom ? 1.0 : -1.0);
        }

        private static double StackMainYLeader(MainOrdinateStackState state, bool isLeft, double desiredY, double minSpacing)
        {
            if (state == null) return desiredY;

            // Left side stacks toward +Y, right side stacks toward -Y.
            return StackLeaderCoordList(
                desiredY,
                isLeft ? state.LeftY : state.RightY,
                minSpacing,
                isLeft ? 1.0 : -1.0);
        }

        private static double StackLeaderCoordList(double desired, List<double> used, double minSpacing, double directionSign)
        {
            if (used == null) return desired;
            if (minSpacing <= 1e-9)
            {
                used.Add(desired);
                return desired;
            }

            double candidate = desired;
            bool changed;
            int guard = 0;
            do
            {
                changed = false;
                for (int i = 0; i < used.Count; i++)
                {
                    if (Math.Abs(candidate - used[i]) < minSpacing)
                    {
                        candidate = used[i] + directionSign * minSpacing;
                        changed = true;
                    }
                }
                guard++;
            }
            while (changed && guard < 1000);

            used.Add(candidate);
            return candidate;
        }

        private static void CreateDims(
            Database db,
            Point3d datum,
            ThicknessRec mainRec,
            List<(ObjectId id, Point3d center)> circles,
            List<(ObjectId id, Point3d center)> arcs,
            List<HoleRec> holes,
            List<Point2d> outerProfileVertices,
            List<Point2d> innerProfileVertices,
            List<HoleRec> sideDimHoles,
            List<SideDepthDimSegment> sideDepthDimSegments,
            Vector3d sideMoveTotal,
            BBox2d? sideMainBox,
            ViewDir dir,
            double mainThk,
            double dimtxt,
            double dimOffsetScale,
            DimStyleTableRecord ddimDimStyleData,
            double xLineBottomY,
            double xLineTopY,
            double yLineLeftX,
            double yLineRightX,
            RuntimeSettings settings,
            List<ObjectId> sideCreatedIds)
        {
            Point3d origin = datum;
            var (dimOffset, _) = DimUtils.GetDimOffsets(dimtxt, dimOffsetScale);

            // Main dims (style B): X at (xmax,ymin), Y at (xmin,ymax)
            Point3d pX = new Point3d(mainRec.Box.Xmax, mainRec.Box.Ymin, 0.0);
            // Choose nearest side for X-ordinate: bottom vs top
            Point3d leaderX = new Point3d(pX.X, ChooseXLeaderY(mainRec.Box, pX, xLineBottomY, xLineTopY), 0.0);

            Point3d pY = new Point3d(mainRec.Box.Xmin, mainRec.Box.Ymax, 0.0);
            // Choose nearest side for Y-ordinate: left vs right
            Point3d leaderY = new Point3d(ChooseYLeaderX(mainRec.Box, pY, yLineLeftX, yLineRightX), pY.Y, 0.0);

            // Add dims for point (0,0) in the local datum system => the chosen datum
            Point3d p00 = origin;

            // Circle ordinate layout:
            // - Keep leader endpoints on straight baselines (no diagonal stacking).
            // - Collapse duplicate X (and duplicate Y) values: if N holes share the same X, create one ordinate
            //   with text override "N-<>" (prefix count and keep real value via <>).
            var centerTargets = new List<CenterDimTarget>();
            if (settings != null && settings.Mode.HoleCenterDims)
            {
                foreach (var c in circles) centerTargets.Add(new CenterDimTarget(new Point2d(c.center.X, c.center.Y), settings.TolHoleCenters, 10));
            }
            if (settings != null && settings.Mode.ArcCenterDims)
            {
                foreach (var a in arcs) centerTargets.Add(new CenterDimTarget(new Point2d(a.center.X, a.center.Y), settings.TolArcCenters, 20));
            }
            if (settings != null && settings.Mode.DepthHoleDims && holes != null)
            {
                foreach (HoleRec h in holes)
                {
                    if (h == null || Math.Abs(h.Depth) <= 1e-9) continue;
                    if (!IsCircleOrArcEntity(db, h.Id)) continue;
                    centerTargets.Add(new CenterDimTarget(h.Center, settings.TolBlindHoles, 30));
                }
            }

            var centers2d = centerTargets.Select(x => x.Point)
                .Distinct(new Point2dComparer(1e-8))
                .ToList();

            const double groupTol = 1e-4; // numeric tolerance for grouping same X/Y values
            var xGroups = GroupByAxisValue(centers2d, axis: 'X', tol: groupTol)
                .OrderBy(g => g.Key)
                .ToList();
            var yGroups = GroupByAxisValue(centers2d, axis: 'Y', tol: groupTol)
                .OrderBy(g => g.Key)
                .ToList();

            // When keeping text/leader endpoints perfectly aligned on a baseline, nearby coordinates can
            // cause text to overlap. To reduce overlap WITHOUT diagonal stacking, we allow the leader
            // endpoint to slide ALONG THE MEASURED AXIS (X for X-ordinate, Y for Y-ordinate).
            // Review threshold requested: 2 * DIMTXT.
            double minAxisSpacing = 2.0 * dimtxt;
            var mainOrdinateStack = new MainOrdinateStackState();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                bool anyBoundaryDim = settings != null && (settings.Mode.BoundaryXminYminDims || settings.Mode.BoundaryXminYmaxDims || settings.Mode.BoundaryXmaxYminDims || settings.Mode.BoundaryXmaxYmaxDims);
                if (anyBoundaryDim)
                {
                    if (settings.BoundaryDimMode == BoundaryDimMode.Linear)
                    {
                        Point3d bx1 = new Point3d(mainRec.Box.Xmin, mainRec.Box.Ymin, 0.0);
                        Point3d bx2 = new Point3d(mainRec.Box.Xmax, mainRec.Box.Ymin, 0.0);
                        Point3d bxLine = new Point3d((mainRec.Box.Xmin + mainRec.Box.Xmax) / 2.0, xLineBottomY, 0.0);
                        var bxd = DimUtils.MakeRotatedDim(db, 0.0, bx1, bx2, bxLine);
                        ApplyDimTextOverride(bxd, "<>", BoundaryLinearTolerance(settings, true));
                        AppendDim(space, tr, bxd, dimtxt, ddimDimStyleData);

                        Point3d by1 = new Point3d(mainRec.Box.Xmin, mainRec.Box.Ymin, 0.0);
                        Point3d by2 = new Point3d(mainRec.Box.Xmin, mainRec.Box.Ymax, 0.0);
                        Point3d byLine = new Point3d(yLineLeftX, (mainRec.Box.Ymin + mainRec.Box.Ymax) / 2.0, 0.0);
                        var byd = DimUtils.MakeRotatedDim(db, Math.PI / 2.0, by1, by2, byLine);
                        ApplyDimTextOverride(byd, "<>", BoundaryLinearTolerance(settings, false));
                        AppendDim(space, tr, byd, dimtxt, ddimDimStyleData);
                    }
                    else
                    {
                        var xDimKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var yDimKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (settings.Mode.BoundaryXminYminDims) AddBoundaryOrdinatePoint(db, space, tr, origin, mainRec.Box, new Point3d(mainRec.Box.Xmin, mainRec.Box.Ymin, 0.0), xLineBottomY, xLineTopY, yLineLeftX, yLineRightX, dimtxt, ddimDimStyleData, xDimKeys, yDimKeys, settings.TolBoundaryXminYmin, mainOrdinateStack, minAxisSpacing);
                        if (settings.Mode.BoundaryXminYmaxDims) AddBoundaryOrdinatePoint(db, space, tr, origin, mainRec.Box, new Point3d(mainRec.Box.Xmin, mainRec.Box.Ymax, 0.0), xLineBottomY, xLineTopY, yLineLeftX, yLineRightX, dimtxt, ddimDimStyleData, xDimKeys, yDimKeys, settings.TolBoundaryXminYmax, mainOrdinateStack, minAxisSpacing);
                        if (settings.Mode.BoundaryXmaxYminDims) AddBoundaryOrdinatePoint(db, space, tr, origin, mainRec.Box, new Point3d(mainRec.Box.Xmax, mainRec.Box.Ymin, 0.0), xLineBottomY, xLineTopY, yLineLeftX, yLineRightX, dimtxt, ddimDimStyleData, xDimKeys, yDimKeys, settings.TolBoundaryXmaxYmin, mainOrdinateStack, minAxisSpacing);
                        if (settings.Mode.BoundaryXmaxYmaxDims) AddBoundaryOrdinatePoint(db, space, tr, origin, mainRec.Box, new Point3d(mainRec.Box.Xmax, mainRec.Box.Ymax, 0.0), xLineBottomY, xLineTopY, yLineLeftX, yLineRightX, dimtxt, ddimDimStyleData, xDimKeys, yDimKeys, settings.TolBoundaryXmaxYmax, mainOrdinateStack, minAxisSpacing);
                    }
                }

                if (settings != null && settings.Mode.OuterProfileVertexDims && outerProfileVertices != null && outerProfileVertices.Count > 0)
                {
                    AddProfileVertexOrdinateDims(db, space, tr, origin, mainRec.Box, outerProfileVertices,
                        xLineBottomY, xLineTopY, yLineLeftX, yLineRightX, dimtxt, ddimDimStyleData,
                        settings.TolOuterProfileVertices, BuildActiveBoundaryPointSet(settings, mainRec.Box), mainOrdinateStack, minAxisSpacing);
                }

                if (settings != null && settings.Mode.InnerProfileDims && innerProfileVertices != null && innerProfileVertices.Count > 0)
                {
                    AddProfileVertexOrdinateDims(db, space, tr, origin, mainRec.Box, innerProfileVertices,
                        xLineBottomY, xLineTopY, yLineLeftX, yLineRightX, dimtxt, ddimDimStyleData,
                        settings.TolInnerProfiles, null, mainOrdinateStack, minAxisSpacing);
                }

                if (centers2d.Count > 0)
                {
                    // Circle/arc center ordinate dims.
                    // DeleteDuplicateDim = true keeps the historical grouped form like "2-10".
                    // DeleteDuplicateDim = false creates a dimension for every selected point, even if X/Y is repeated.
                    if (settings == null || settings.DeleteDuplicateDim)
                    {
                        foreach (var g in xGroups)
                        {
                            Point2d cp = g.Representative;
                            Point3d def = new Point3d(cp.X, cp.Y, 0.0);
                            double leaderYx = ChooseXLeaderY(mainRec.Box, def, xLineBottomY, xLineTopY);
                            bool isBottom = Math.Abs(leaderYx - xLineBottomY) <= 1e-9;
                            double adjX = StackMainXLeader(mainOrdinateStack, isBottom, cp.X, minAxisSpacing);
                            var od = DimUtils.MakeOrdinate(db, origin, def, new Point3d(adjX, leaderYx, 0.0), usingXAxis: true);
                            if (g.Count > 1) ApplyDimTextOverride(od, g.Count.ToString() + "-<>", ToleranceForAxisGroup(centerTargets, g, 'X', groupTol));
                            else ApplyDimTextOverride(od, "<>", ToleranceForAxisGroup(centerTargets, g, 'X', groupTol));
                            AppendDim(space, tr, od, dimtxt, ddimDimStyleData);
                        }

                        foreach (var g in yGroups)
                        {
                            Point2d cp = g.Representative;
                            Point3d def = new Point3d(cp.X, cp.Y, 0.0);
                            double leaderXy = ChooseYLeaderX(mainRec.Box, def, yLineLeftX, yLineRightX);
                            bool isLeft = Math.Abs(leaderXy - yLineLeftX) <= 1e-9;
                            double adjY = StackMainYLeader(mainOrdinateStack, isLeft, cp.Y, minAxisSpacing);
                            var od = DimUtils.MakeOrdinate(db, origin, def, new Point3d(leaderXy, adjY, 0.0), usingXAxis: false);
                            if (g.Count > 1) ApplyDimTextOverride(od, g.Count.ToString() + "-<>", ToleranceForAxisGroup(centerTargets, g, 'Y', groupTol));
                            else ApplyDimTextOverride(od, "<>", ToleranceForAxisGroup(centerTargets, g, 'Y', groupTol));
                            AppendDim(space, tr, od, dimtxt, ddimDimStyleData);
                        }
                    }
                    else
                    {
                        foreach (Point2d cp in centers2d.OrderBy(p => p.X).ThenBy(p => p.Y))
                        {
                            Point3d def = new Point3d(cp.X, cp.Y, 0.0);
                            double leaderYx = ChooseXLeaderY(mainRec.Box, def, xLineBottomY, xLineTopY);
                            bool isBottom = Math.Abs(leaderYx - xLineBottomY) <= 1e-9;
                            double adjX = StackMainXLeader(mainOrdinateStack, isBottom, cp.X, minAxisSpacing);
                            var od = DimUtils.MakeOrdinate(db, origin, def, new Point3d(adjX, leaderYx, 0.0), usingXAxis: true);
                            ApplyDimTextOverride(od, "<>", ToleranceForPoint(centerTargets, cp));
                            AppendDim(space, tr, od, dimtxt, ddimDimStyleData);
                        }

                        foreach (Point2d cp in centers2d.OrderBy(p => p.Y).ThenBy(p => p.X))
                        {
                            Point3d def = new Point3d(cp.X, cp.Y, 0.0);
                            double leaderXy = ChooseYLeaderX(mainRec.Box, def, yLineLeftX, yLineRightX);
                            bool isLeft = Math.Abs(leaderXy - yLineLeftX) <= 1e-9;
                            double adjY = StackMainYLeader(mainOrdinateStack, isLeft, cp.Y, minAxisSpacing);
                            var od = DimUtils.MakeOrdinate(db, origin, def, new Point3d(leaderXy, adjY, 0.0), usingXAxis: false);
                            ApplyDimTextOverride(od, "<>", ToleranceForPoint(centerTargets, cp));
                            AppendDim(space, tr, od, dimtxt, ddimDimStyleData);
                        }
                    }
                }

                // Thickness / side-view dimensions.
                if (sideMainBox != null)
                {
                    if (settings == null || settings.Mode.SideThicknessDims)
                        AddSideThicknessDim(db, space, tr, sideMainBox, dir, mainThk, dimOffset, dimtxt, ddimDimStyleData, settings);

                    if (settings != null && settings.Mode.SideProjectionHoleDims)
                    {
                        List<Point3d> sideCircleCenters = CollectSideProjectionCircleCenters(tr, sideCreatedIds, sideMainBox);
                        if (sideCircleCenters.Count > 0)
                            AddSideProjectionHoleDims(db, space, tr, datum, sideMainBox, dir, sideCircleCenters, dimOffset, dimtxt, ddimDimStyleData, settings);
                    }

                    if (settings != null && settings.Mode.SideProjectionDepthDims && sideDepthDimSegments != null && sideDepthDimSegments.Count > 0)
                        AddSideProjectionDepthDims(db, space, tr, sideDepthDimSegments, sideMoveTotal, dir, dimOffset, dimtxt, ddimDimStyleData, settings);
                }

                tr.Commit();
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;
            foreach (string v in values)
            {
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return string.Empty;
        }

        private static string BoundaryLinearTolerance(RuntimeSettings settings, bool forXDimension)
        {
            if (settings == null) return string.Empty;
            if (forXDimension)
            {
                return FirstNonEmpty(
                    settings.Mode.BoundaryXminYminDims ? settings.TolBoundaryXminYmin : string.Empty,
                    settings.Mode.BoundaryXmaxYminDims ? settings.TolBoundaryXmaxYmin : string.Empty,
                    settings.Mode.BoundaryXminYmaxDims ? settings.TolBoundaryXminYmax : string.Empty,
                    settings.Mode.BoundaryXmaxYmaxDims ? settings.TolBoundaryXmaxYmax : string.Empty);
            }

            return FirstNonEmpty(
                settings.Mode.BoundaryXminYminDims ? settings.TolBoundaryXminYmin : string.Empty,
                settings.Mode.BoundaryXminYmaxDims ? settings.TolBoundaryXminYmax : string.Empty,
                settings.Mode.BoundaryXmaxYminDims ? settings.TolBoundaryXmaxYmin : string.Empty,
                settings.Mode.BoundaryXmaxYmaxDims ? settings.TolBoundaryXmaxYmax : string.Empty);
        }

        private sealed class CenterDimTarget
        {
            public Point2d Point;
            public string ToleranceText;
            public int Priority;

            public CenterDimTarget(Point2d point, string toleranceText, int priority)
            {
                Point = point;
                ToleranceText = toleranceText ?? string.Empty;
                Priority = priority;
            }
        }

        private static string ToleranceForPoint(List<CenterDimTarget> targets, Point2d point)
        {
            if (targets == null || targets.Count == 0) return string.Empty;
            CenterDimTarget best = null;
            foreach (CenterDimTarget t in targets)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.ToleranceText)) continue;
                if (Math.Abs(t.Point.X - point.X) > 1e-8 || Math.Abs(t.Point.Y - point.Y) > 1e-8) continue;
                if (best == null || t.Priority > best.Priority) best = t;
            }
            return best == null ? string.Empty : best.ToleranceText;
        }

        private static string ToleranceForAxisGroup(List<CenterDimTarget> targets, AxisGroup group, char axis, double tol)
        {
            if (targets == null || group == null) return string.Empty;
            double eps = Math.Max(1e-12, tol);
            long groupKey = (long)Math.Round(group.Key / eps);
            CenterDimTarget best = null;
            foreach (CenterDimTarget t in targets)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.ToleranceText)) continue;
                double v = (axis == 'Y') ? t.Point.Y : t.Point.X;
                long key = (long)Math.Round(v / eps);
                if (key != groupKey) continue;
                if (best == null || t.Priority > best.Priority) best = t;
            }
            return best == null ? string.Empty : best.ToleranceText;
        }

        private static string CenterGroupTolerance(RuntimeSettings settings)
        {
            if (settings == null) return string.Empty;
            if (settings.Mode.DepthHoleDims && !string.IsNullOrWhiteSpace(settings.TolBlindHoles)) return settings.TolBlindHoles;
            if (settings.Mode.HoleCenterDims && !string.IsNullOrWhiteSpace(settings.TolHoleCenters)) return settings.TolHoleCenters;
            if (settings.Mode.ArcCenterDims && !string.IsNullOrWhiteSpace(settings.TolArcCenters)) return settings.TolArcCenters;
            return string.Empty;
        }

        private static string CleanToleranceText(string text)
        {
            return (text ?? string.Empty).Trim().Replace(" ", string.Empty).Replace(',', '.');
        }

        private static string StripLeadingSign(string value)
        {
            string s = (value ?? string.Empty).Trim();
            while (s.StartsWith("+", StringComparison.Ordinal) || s.StartsWith("-", StringComparison.Ordinal)) s = s.Substring(1);
            return s;
        }

        private sealed class CadToleranceSpec
        {
            public double Upper;
            public double Lower;
            public bool Numeric;
        }

        private static bool TryParseCadTolerance(string toleranceText, out CadToleranceSpec spec)
        {
            spec = null;
            string t = CleanToleranceText(toleranceText);
            if (string.IsNullOrWhiteSpace(t)) return false;

            t = t.Replace("+-", "%%p");
            t = t.Replace("\u00b1", "%%p");

            if (t.StartsWith("%%p", StringComparison.OrdinalIgnoreCase))
            {
                string valueText = t.Substring(3);
                if (!TryParseToleranceNumber(valueText, out double value)) return false;
                value = Math.Abs(value);
                spec = new CadToleranceSpec { Upper = value, Lower = value, Numeric = true };
                return value > 0.0;
            }

            int slash = t.IndexOf('/');
            if (slash >= 0)
            {
                string upperText = t.Substring(0, slash);
                string lowerText = t.Substring(slash + 1);
                if (!TryParseToleranceNumber(upperText, out double upper)) return false;
                if (!TryParseToleranceNumber(lowerText, out double lower)) return false;

                spec = new CadToleranceSpec
                {
                    Upper = Math.Abs(upper),
                    Lower = Math.Abs(lower),
                    Numeric = true
                };
                return spec.Upper > 0.0 || spec.Lower > 0.0;
            }

            if (!TryParseToleranceNumber(t, out double symmetric)) return false;
            symmetric = Math.Abs(symmetric);
            spec = new CadToleranceSpec { Upper = symmetric, Lower = symmetric, Numeric = true };
            return symmetric > 0.0;
        }

        private static bool TryParseToleranceNumber(string valueText, out double value)
        {
            value = 0.0;
            string s = (valueText ?? string.Empty).Trim();
            if (s.StartsWith("+", StringComparison.Ordinal)) s = s.Substring(1);
            if (string.IsNullOrWhiteSpace(s)) s = "0";
            return double.TryParse(
                s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        private static string BuildToleranceSuffix(string toleranceText)
        {
            string t = CleanToleranceText(toleranceText);
            if (string.IsNullOrWhiteSpace(t)) return string.Empty;

            if (TryParseCadTolerance(t, out _)) return string.Empty;

            return "\\X" + t;
        }

        private static void ApplyCadTolerance(Dimension dim, string toleranceText)
        {
            if (dim == null) return;

            TrySetDimProperty(dim, "Dimtol", false);
            TrySetDimProperty(dim, "Dimlim", false);

            if (!TryParseCadTolerance(toleranceText, out CadToleranceSpec tol) || !tol.Numeric) return;

            TrySetDimProperty(dim, "Dimtol", true);
            TrySetDimProperty(dim, "Dimlim", false);
            TrySetDimProperty(dim, "Dimtp", tol.Upper);
            TrySetDimProperty(dim, "Dimtm", tol.Lower);
            TrySetDimProperty(dim, "Dimtolj", 1);
        }

        private static void TrySetDimProperty(object obj, string propName, object value)
        {
            if (obj == null) return;
            try
            {
                var p = obj.GetType().GetProperty(
                    propName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.IgnoreCase);
                if (p == null || !p.CanWrite) return;

                Type t = p.PropertyType;
                object v = value;
                if (v != null && !t.IsInstanceOfType(v))
                {
                    if (t == typeof(bool)) v = Convert.ToBoolean(v);
                    else if (t == typeof(short)) v = Convert.ToInt16(v);
                    else if (t == typeof(int)) v = Convert.ToInt32(v);
                    else if (t == typeof(double)) v = Convert.ToDouble(v);
                    else if (t.IsEnum) v = Enum.ToObject(t, Convert.ToInt32(v));
                    else return;
                }
                p.SetValue(obj, v, null);
            }
            catch
            {
            }
        }

        private static object TryGetDimProperty(object obj, string propName)
        {
            if (obj == null) return null;
            try
            {
                var p = obj.GetType().GetProperty(
                    propName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.IgnoreCase);
                if (p == null || !p.CanRead) return null;
                return p.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsDimToleranceEnabled(Dimension dim)
        {
            object value = TryGetDimProperty(dim, "Dimtol");
            if (value == null) return false;
            try { return Convert.ToBoolean(value); }
            catch { return false; }
        }

        private static void RestoreCadTolerance(Dimension dim, object dimtp, object dimtm, object dimtolj)
        {
            if (dim == null) return;
            TrySetDimProperty(dim, "Dimtol", true);
            TrySetDimProperty(dim, "Dimlim", false);
            if (dimtp != null) TrySetDimProperty(dim, "Dimtp", dimtp);
            if (dimtm != null) TrySetDimProperty(dim, "Dimtm", dimtm);
            if (dimtolj != null) TrySetDimProperty(dim, "Dimtolj", dimtolj);
        }

        private static void ApplyDimTextOverride(Dimension dim, string baseText, string toleranceText)
        {
            if (dim == null) return;

            ApplyCadTolerance(dim, toleranceText);

            string suffix = BuildToleranceSuffix(toleranceText);
            if (string.IsNullOrEmpty(suffix))
            {
                if (!string.IsNullOrEmpty(baseText) && !string.Equals(baseText, "<>", StringComparison.Ordinal)) dim.DimensionText = baseText;
                return;
            }
            dim.DimensionText = (string.IsNullOrEmpty(baseText) ? "<>" : baseText) + suffix;
        }

        private static string FormatDimNumber(double value)
        {
            double v = Math.Abs(value);
            return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void ApplySideThicknessDimText(Dimension dim, double mainThk, string toleranceText)
        {
            if (TryParseCadTolerance(toleranceText, out CadToleranceSpec tol) && tol != null && tol.Numeric)
                ApplyDimTextOverride(dim, "<>", toleranceText);
            else
                ApplyDimTextOverride(dim, FormatDimNumber(mainThk), toleranceText);
        }

        private static double SideFarCoord(BBox2d box, ViewDir dir, bool thkAxisIsY)
        {
            if (thkAxisIsY) return dir == ViewDir.Deg90 ? box.Ymax : box.Ymin;
            return dir == ViewDir.Deg0 ? box.Xmax : box.Xmin;
        }

        private static double SideNearCoord(BBox2d box, ViewDir dir, bool thkAxisIsY)
        {
            if (thkAxisIsY) return dir == ViewDir.Deg90 ? box.Ymin : box.Ymax;
            return dir == ViewDir.Deg0 ? box.Xmin : box.Xmax;
        }

        private static double SideCoordFromT(BBox2d box, ViewDir dir, bool thkAxisIsY, double mainThk, double t)
        {
            t = Math.Max(0.0, Math.Min(mainThk, t));
            double far = SideFarCoord(box, dir, thkAxisIsY);
            double near = SideNearCoord(box, dir, thkAxisIsY);
            if (mainThk <= 1e-9) return near;
            return far + (near - far) * (t / mainThk);
        }

        private static void AddSideThicknessDim(Database db, BlockTableRecord space, Transaction tr, BBox2d sideMainBox, ViewDir dir, double mainThk, double dimOffset, double dimtxt, DimStyleTableRecord ddimDimStyleData, RuntimeSettings settings)
        {
            bool thkAxisIsY = (dir == ViewDir.Deg90 || dir == ViewDir.Deg270);
            double far = SideFarCoord(sideMainBox, dir, thkAxisIsY);
            double near = SideNearCoord(sideMainBox, dir, thkAxisIsY);
            string tol = settings == null ? string.Empty : settings.TolSideThickness;

            if (settings != null && settings.BoundaryDimMode == BoundaryDimMode.Ordinate)
            {
                if (thkAxisIsY)
                {
                    double x = sideMainBox.Xmin;
                    Point3d origin = new Point3d(x, far, 0.0);
                    double leaderX = x - dimOffset;
                    var od0 = DimUtils.MakeOrdinate(db, origin, new Point3d(x, far, 0.0), new Point3d(leaderX, far, 0.0), usingXAxis: false);
                    od0.DimensionText = "0";
                    AppendDim(space, tr, od0, dimtxt, ddimDimStyleData);
                    var odT = DimUtils.MakeOrdinate(db, origin, new Point3d(x, near, 0.0), new Point3d(leaderX, near, 0.0), usingXAxis: false);
                    ApplySideThicknessDimText(odT, mainThk, tol);
                    AppendDim(space, tr, odT, dimtxt, ddimDimStyleData);
                }
                else
                {
                    double y = sideMainBox.Ymin;
                    Point3d origin = new Point3d(far, y, 0.0);
                    double leaderY = y - dimOffset;
                    var od0 = DimUtils.MakeOrdinate(db, origin, new Point3d(far, y, 0.0), new Point3d(far, leaderY, 0.0), usingXAxis: true);
                    od0.DimensionText = "0";
                    AppendDim(space, tr, od0, dimtxt, ddimDimStyleData);
                    var odT = DimUtils.MakeOrdinate(db, origin, new Point3d(near, y, 0.0), new Point3d(near, leaderY, 0.0), usingXAxis: true);
                    ApplySideThicknessDimText(odT, mainThk, tol);
                    AppendDim(space, tr, odT, dimtxt, ddimDimStyleData);
                }
                return;
            }

            if (!thkAxisIsY)
            {
                double yNear = sideMainBox.Ymin;
                var rd = DimUtils.MakeRotatedDim(db, 0.0, new Point3d(sideMainBox.Xmin, yNear, 0.0), new Point3d(sideMainBox.Xmax, yNear, 0.0), new Point3d((sideMainBox.Xmin + sideMainBox.Xmax) / 2.0, yNear - dimOffset, 0.0));
                ApplyDimTextOverride(rd, "<>", tol);
                AppendDim(space, tr, rd, dimtxt, ddimDimStyleData);
            }
            else
            {
                double xNear = sideMainBox.Xmin;
                var rd = DimUtils.MakeRotatedDim(db, Math.PI / 2.0, new Point3d(xNear, sideMainBox.Ymin, 0.0), new Point3d(xNear, sideMainBox.Ymax, 0.0), new Point3d(xNear - dimOffset, (sideMainBox.Ymin + sideMainBox.Ymax) / 2.0, 0.0));
                ApplyDimTextOverride(rd, "<>", tol);
                AppendDim(space, tr, rd, dimtxt, ddimDimStyleData);
            }
        }

        private static List<Point3d> CollectSideProjectionCircleCenters(Transaction tr, IEnumerable<ObjectId> createdIds, BBox2d sideMainBox)
        {
            var result = new List<Point3d>();
            if (tr == null || createdIds == null) return result;

            foreach (ObjectId id in createdIds)
            {
                if (id == ObjectId.Null || id.IsErased) continue;

                Circle c = null;
                try { c = tr.GetObject(id, OpenMode.ForRead, false) as Circle; }
                catch { c = null; }

                if (c == null) continue;

                Point3d p = c.Center;
                if (p.X < sideMainBox.Xmin - 1e-6 || p.X > sideMainBox.Xmax + 1e-6) continue;
                if (p.Y < sideMainBox.Ymin - 1e-6 || p.Y > sideMainBox.Ymax + 1e-6) continue;

                result.Add(p);
            }

            return result
                .Distinct(new Point3dComparer(1e-8))
                .OrderBy(p => p.X)
                .ThenBy(p => p.Y)
                .ToList();
        }

        private static double SideFarOutwardSign(BBox2d box, ViewDir dir, bool thkAxisIsY)
        {
            double far = SideFarCoord(box, dir, thkAxisIsY);
            double near = SideNearCoord(box, dir, thkAxisIsY);
            return far <= near ? -1.0 : 1.0;
        }

        private static double StackLeaderCoord(double desired, ref double last, double minSpacing, double directionSign)
        {
            if (double.IsNegativeInfinity(last))
            {
                last = desired;
                return desired;
            }

            double signedGap = (desired - last) * directionSign;
            if (signedGap < minSpacing)
                desired = last + directionSign * minSpacing;

            last = desired;
            return desired;
        }

        private static void AddSideProjectionHoleDims(Database db, BlockTableRecord space, Transaction tr, Point3d datum, BBox2d sideMainBox, ViewDir dir, List<Point3d> sideCircleCenters, double dimOffset, double dimtxt, DimStyleTableRecord ddimDimStyleData, RuntimeSettings settings)
        {
            if (sideCircleCenters == null || sideCircleCenters.Count == 0) return;

            bool thkAxisIsY = (dir == ViewDir.Deg90 || dir == ViewDir.Deg270);
            double far = SideFarCoord(sideMainBox, dir, thkAxisIsY);
            double farSign = SideFarOutwardSign(sideMainBox, dir, thkAxisIsY);
            string tol = settings == null ? string.Empty : settings.TolSideProjectionHoles;
            double minTextSpacing = Math.Max(dimtxt * 2.0, 1e-6);

            var points = sideCircleCenters
                .Select(p => new Point2d(p.X, p.Y))
                .Distinct(new Point2dComparer(1e-8))
                .ToList();
            if (points.Count == 0) return;

            if (settings != null && settings.BoundaryDimMode == BoundaryDimMode.Linear)
            {
                double lastLine = double.NegativeInfinity;
                foreach (Point2d p in points.OrderBy(q => thkAxisIsY ? q.X : q.Y))
                {
                    if (thkAxisIsY)
                    {
                        double lineY = StackLeaderCoord(far + farSign * dimOffset, ref lastLine, minTextSpacing, farSign);
                        var rd = DimUtils.MakeRotatedDim(db, 0.0, new Point3d(datum.X, p.Y, 0.0), new Point3d(p.X, p.Y, 0.0), new Point3d((datum.X + p.X) / 2.0, lineY, 0.0));
                        ApplyDimTextOverride(rd, "<>", tol);
                        AppendDim(space, tr, rd, dimtxt, ddimDimStyleData);
                    }
                    else
                    {
                        double lineX = StackLeaderCoord(far + farSign * dimOffset, ref lastLine, minTextSpacing, farSign);
                        var rd = DimUtils.MakeRotatedDim(db, Math.PI / 2.0, new Point3d(p.X, datum.Y, 0.0), new Point3d(p.X, p.Y, 0.0), new Point3d(lineX, (datum.Y + p.Y) / 2.0, 0.0));
                        ApplyDimTextOverride(rd, "<>", tol);
                        AppendDim(space, tr, rd, dimtxt, ddimDimStyleData);
                    }
                }
                return;
            }

            if (thkAxisIsY)
            {
                Point3d origin = new Point3d(datum.X, far, 0.0);
                double leaderY = far + farSign * dimOffset;
                double lastLeaderX = double.NegativeInfinity;
                foreach (Point2d p in points.OrderBy(q => q.X).ThenBy(q => q.Y))
                {
                    double lx = StackLeaderCoord(p.X, ref lastLeaderX, minTextSpacing, 1.0);
                    var od = DimUtils.MakeOrdinate(db, origin, new Point3d(p.X, p.Y, 0.0), new Point3d(lx, leaderY, 0.0), usingXAxis: true);
                    ApplyDimTextOverride(od, "<>", tol);
                    AppendDim(space, tr, od, dimtxt, ddimDimStyleData);
                }
            }
            else
            {
                Point3d origin = new Point3d(far, datum.Y, 0.0);
                double leaderX = far + farSign * dimOffset;
                double lastLeaderY = double.NegativeInfinity;
                foreach (Point2d p in points.OrderBy(q => q.Y).ThenBy(q => q.X))
                {
                    double ly = StackLeaderCoord(p.Y, ref lastLeaderY, minTextSpacing, 1.0);
                    var od = DimUtils.MakeOrdinate(db, origin, new Point3d(p.X, p.Y, 0.0), new Point3d(leaderX, ly, 0.0), usingXAxis: false);
                    ApplyDimTextOverride(od, "<>", tol);
                    AppendDim(space, tr, od, dimtxt, ddimDimStyleData);
                }
            }
        }


        private static void AddSideProjectionDepthDims(Database db, BlockTableRecord space, Transaction tr, List<SideDepthDimSegment> segments, Vector3d sideMoveTotal, ViewDir dir, double dimOffset, double dimtxt, DimStyleTableRecord ddimDimStyleData, RuntimeSettings settings)
        {
            // Depth dimensions use the exact segment emitted by the side-view planner:
            // point 1 = depth bottom inside the side view, point 2 = hole opening edge.
            bool thkAxisIsY = (dir == ViewDir.Deg90 || dir == ViewDir.Deg270);
            string tol = settings == null ? string.Empty : settings.TolSideProjectionDepths;
            double minTextSpacing = Math.Max(dimtxt * 2.0, 1e-6);
            var usedDepthLines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (SideDepthDimSegment seg in segments)
            {
                if (seg == null) continue;

                Point2d inner = new Point2d(seg.InnerPoint.X + sideMoveTotal.X, seg.InnerPoint.Y + sideMoveTotal.Y);
                Point2d edge = new Point2d(seg.EdgePoint.X + sideMoveTotal.X, seg.EdgePoint.Y + sideMoveTotal.Y);

                if (inner.GetDistanceTo(edge) <= 1e-9) continue;

                if (thkAxisIsY)
                {
                    double x = inner.X;
                    string key = x.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
                    int stackIndex;
                    usedDepthLines.TryGetValue(key, out stackIndex);
                    usedDepthLines[key] = stackIndex + 1;
                    double lineX = x - dimOffset - stackIndex * minTextSpacing;

                    var rd = DimUtils.MakeRotatedDim(db, Math.PI / 2.0, new Point3d(inner.X, inner.Y, 0.0), new Point3d(edge.X, edge.Y, 0.0), new Point3d(lineX, (inner.Y + edge.Y) / 2.0, 0.0));
                    ApplyDimTextOverride(rd, "<>", tol);
                    AppendDim(space, tr, rd, dimtxt, ddimDimStyleData);
                }
                else
                {
                    double y = inner.Y;
                    string key = y.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
                    int stackIndex;
                    usedDepthLines.TryGetValue(key, out stackIndex);
                    usedDepthLines[key] = stackIndex + 1;
                    double lineY = y - dimOffset - stackIndex * minTextSpacing;

                    var rd = DimUtils.MakeRotatedDim(db, 0.0, new Point3d(inner.X, inner.Y, 0.0), new Point3d(edge.X, edge.Y, 0.0), new Point3d((inner.X + edge.X) / 2.0, lineY, 0.0));
                    ApplyDimTextOverride(rd, "<>", tol);
                    AppendDim(space, tr, rd, dimtxt, ddimDimStyleData);
                }
            }
        }

        private static List<Point2d> CollectOuterProfileVertices(Database db, ThicknessRec mainRec)
        {
            var result = new List<Point2d>();
            if (db == null || mainRec == null || mainRec.Id == ObjectId.Null) return result;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Autodesk.AutoCAD.DatabaseServices.Polyline pl = null;
                try { pl = tr.GetObject(mainRec.Id, OpenMode.ForRead, false) as Autodesk.AutoCAD.DatabaseServices.Polyline; }
                catch { pl = null; }

                if (pl != null && pl.Closed)
                    AddPolylineVertices(result, pl);

                tr.Commit();
            }

            return DistinctPoints(result);
        }

        private static List<Point2d> CollectInnerProfileVertices(Database db, IEnumerable<ObjectId> selectedIds, ThicknessRec mainRec)
        {
            var result = new List<Point2d>();
            if (db == null || selectedIds == null || mainRec == null) return result;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in selectedIds)
                {
                    if (id == ObjectId.Null || id == mainRec.Id) continue;

                    Autodesk.AutoCAD.DatabaseServices.Polyline pl = null;
                    try { pl = tr.GetObject(id, OpenMode.ForRead, false) as Autodesk.AutoCAD.DatabaseServices.Polyline; }
                    catch { pl = null; }

                    if (pl == null || !pl.Closed) continue;

                    BBox2d box = GeomUtils.GetBBox2d(pl);
                    if (box == null) continue;
                    if (!BoxInside(box, mainRec.Box, 1e-6)) continue;
                    if (box.Area >= mainRec.Box.Area - 1e-6) continue;

                    AddPolylineVertices(result, pl);
                }
                tr.Commit();
            }

            return DistinctPoints(result);
        }

        private static void AddPolylineVertices(List<Point2d> target, Autodesk.AutoCAD.DatabaseServices.Polyline pl)
        {
            if (target == null || pl == null) return;
            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
                target.Add(pl.GetPoint2dAt(i));
        }

        private static List<Point2d> DistinctPoints(IEnumerable<Point2d> points)
        {
            if (points == null) return new List<Point2d>();
            return points
                .Distinct(new Point2dComparer(1e-8))
                .OrderBy(p => p.X)
                .ThenBy(p => p.Y)
                .ToList();
        }

        private static bool BoxInside(BBox2d inner, BBox2d outer, double tol)
        {
            if (inner == null || outer == null) return false;
            return inner.Xmin >= outer.Xmin - tol
                && inner.Xmax <= outer.Xmax + tol
                && inner.Ymin >= outer.Ymin - tol
                && inner.Ymax <= outer.Ymax + tol;
        }

        private static bool IsCircleOrArcEntity(Database db, ObjectId id)
        {
            if (db == null || id == ObjectId.Null) return false;
            bool ok = false;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = null;
                try { ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                catch { ent = null; }
                ok = ent is Circle || ent is Arc;
                tr.Commit();
            }
            return ok;
        }

        private static HashSet<string> BuildActiveBoundaryPointSet(RuntimeSettings settings, BBox2d box)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings == null || box == null) return set;

            if (settings.Mode.BoundaryXminYminDims) set.Add(PointKey(new Point2d(box.Xmin, box.Ymin)));
            if (settings.Mode.BoundaryXminYmaxDims) set.Add(PointKey(new Point2d(box.Xmin, box.Ymax)));
            if (settings.Mode.BoundaryXmaxYminDims) set.Add(PointKey(new Point2d(box.Xmax, box.Ymin)));
            if (settings.Mode.BoundaryXmaxYmaxDims) set.Add(PointKey(new Point2d(box.Xmax, box.Ymax)));
            return set;
        }

        private static string PointKey(Point2d p)
        {
            return p.X.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)
                + ","
                + p.Y.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void AddProfileVertexOrdinateDims(
            Database db,
            BlockTableRecord space,
            Transaction tr,
            Point3d origin,
            BBox2d box,
            IEnumerable<Point2d> vertices,
            double xLineBottomY,
            double xLineTopY,
            double yLineLeftX,
            double yLineRightX,
            double dimtxt,
            DimStyleTableRecord ddimDimStyleData,
            string toleranceText,
            HashSet<string> skipPointKeys,
            MainOrdinateStackState stackState,
            double minAxisSpacing)
        {
            if (vertices == null) return;

            var xDimKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var yDimKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Point2d v in DistinctPoints(vertices))
            {
                if (skipPointKeys != null && skipPointKeys.Contains(PointKey(v))) continue;
                AddBoundaryOrdinatePoint(db, space, tr, origin, box, new Point3d(v.X, v.Y, 0.0),
                    xLineBottomY, xLineTopY, yLineLeftX, yLineRightX, dimtxt, ddimDimStyleData,
                    xDimKeys, yDimKeys, toleranceText, stackState, minAxisSpacing);
            }
        }

        private static void AddBoundaryOrdinatePoint(
            Database db,
            BlockTableRecord space,
            Transaction tr,
            Point3d origin,
            BBox2d box,
            Point3d point,
            double xLineBottomY,
            double xLineTopY,
            double yLineLeftX,
            double yLineRightX,
            double dimtxt,
            DimStyleTableRecord ddimDimStyleData,
            HashSet<string> xDimKeys,
            HashSet<string> yDimKeys,
            string toleranceText,
            MainOrdinateStackState stackState,
            double minAxisSpacing)
        {
            string xKey = point.X.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
            if (xDimKeys == null || xDimKeys.Add(xKey))
            {
                double leaderYValue = ChooseXLeaderY(box, point, xLineBottomY, xLineTopY);
                bool isBottom = Math.Abs(leaderYValue - xLineBottomY) <= 1e-9;
                double leaderXValue = StackMainXLeader(stackState, isBottom, point.X, minAxisSpacing);
                Point3d leaderX = new Point3d(leaderXValue, leaderYValue, 0.0);
                var odx = DimUtils.MakeOrdinate(db, origin, point, leaderX, usingXAxis: true);
                ApplyDimTextOverride(odx, "<>", toleranceText);
                AppendDim(space, tr, odx, dimtxt, ddimDimStyleData);
            }

            string yKey = point.Y.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
            if (yDimKeys == null || yDimKeys.Add(yKey))
            {
                double leaderXValue = ChooseYLeaderX(box, point, yLineLeftX, yLineRightX);
                bool isLeft = Math.Abs(leaderXValue - yLineLeftX) <= 1e-9;
                double leaderYValue = StackMainYLeader(stackState, isLeft, point.Y, minAxisSpacing);
                Point3d leaderY = new Point3d(leaderXValue, leaderYValue, 0.0);
                var ody = DimUtils.MakeOrdinate(db, origin, point, leaderY, usingXAxis: false);
                ApplyDimTextOverride(ody, "<>", toleranceText);
                AppendDim(space, tr, ody, dimtxt, ddimDimStyleData);
            }
        }

        internal static void AppendDim(BlockTableRecord space, Transaction tr, Entity dim, double dimtxt, DimStyleTableRecord ddimDimStyleData)
        {
            Dimension d = dim as Dimension;

            bool keepTolerance = false;
            object keepDimtp = null;
            object keepDimtm = null;
            object keepDimtolj = null;
            if (d != null)
            {
                keepTolerance = IsDimToleranceEnabled(d);
                if (keepTolerance)
                {
                    keepDimtp = TryGetDimProperty(d, "Dimtp");
                    keepDimtm = TryGetDimProperty(d, "Dimtm");
                    keepDimtolj = TryGetDimProperty(d, "Dimtolj");
                }

                // Standard AutoCAD .NET way: apply a DimStyleTableRecord data copy
                // to the dimension object. No reflection and no global DIM variable changes.
                // This can reset per-dimension tolerance overrides, so tolerance is restored after append.
                DimUtils.ApplyDDimnotesDimstyleData(space.Database, d, ddimDimStyleData);
            }

            ObjectId id = space.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);

            if (d != null)
            {
                if (keepTolerance)
                    RestoreCadTolerance(d, keepDimtp, keepDimtm, keepDimtolj);

                try { d.RecomputeDimensionBlock(true); } catch { /* ignore */ }

                if (keepTolerance)
                    RestoreCadTolerance(d, keepDimtp, keepDimtm, keepDimtolj);
            }

            // Dimension layer option must only change the parent Dimension entity.
            // Keep dimension internals/colors/style data exactly as configured above.
            if (!string.IsNullOrWhiteSpace(_activeDimensionLayer))
            {
                try { dim.Layer = _activeDimensionLayer; } catch { }
            }
        }

        private static bool IsVietnameseLanguage()
        {
            if (Lang.CurrentLangId == 1) return true;
            string name = Lang.CurrentLanguageName ?? string.Empty;
            return name.IndexOf("Vietnamese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Viet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DefaultNoteTextStyleOption()
        {
            return IsVietnameseLanguage() ? "Arial" : "Isocp";
        }

        private static string DefaultMarkTextStyleOption()
        {
            return "Isocp";
        }

        private static string NormalizeTextStyleOption(string value, string fallback)
        {
            string s = (value ?? string.Empty).Trim();
            if (s.Length == 0 || string.Equals(s, "Auto", StringComparison.OrdinalIgnoreCase))
                return fallback;
            if (string.Equals(s, "Current", StringComparison.OrdinalIgnoreCase)) return "Current";
            if (string.Equals(s, "Isocp", StringComparison.OrdinalIgnoreCase)) return "Isocp";
            if (string.Equals(s, "txt", StringComparison.OrdinalIgnoreCase)) return "txt";
            if (string.Equals(s, "Arial", StringComparison.OrdinalIgnoreCase)) return "Arial";
            return fallback;
        }

        private static string NormalizeNoteTextStyleOption(string value)
        {
            return NormalizeTextStyleOption(value, DefaultNoteTextStyleOption());
        }

        private static string NormalizeMarkTextStyleOption(string value)
        {
            return NormalizeTextStyleOption(value, DefaultMarkTextStyleOption());
        }

        private static string TextStyleNameFromOption(string option)
        {
            string s = (option ?? string.Empty).Trim();
            if (s.Length == 0 || string.Equals(s, "Current", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (string.Equals(s, "Isocp", StringComparison.OrdinalIgnoreCase)) return "Isocp";
            if (string.Equals(s, "txt", StringComparison.OrdinalIgnoreCase)) return "txt";
            if (string.Equals(s, "Arial", StringComparison.OrdinalIgnoreCase)) return "Arial";
            return string.Empty;
        }

        private static void ApplyRuntimeNoteSettings(DDimnotesNoteConfig cfg, double dimtxt, RuntimeSettings settings)
        {
            if (cfg == null || settings == null) return;
            cfg.MarkTextH = Clamp(settings.MarkTextScale, 0.01, 5.0, 0.80) * dimtxt;
            if (settings.MarkColorIndex >= 1 && settings.MarkColorIndex <= 255) cfg.MarkColorIndex = settings.MarkColorIndex;
            cfg.NoteLayerName = settings.ResolvedNoteLayer;
            cfg.MarkLayerName = settings.ResolvedMarkLayer;
            cfg.NoteTextStyleName = TextStyleNameFromOption(NormalizeNoteTextStyleOption(settings.NoteTextStyleOption));
            cfg.MarkTextStyleName = TextStyleNameFromOption(NormalizeMarkTextStyleOption(settings.MarkTextStyleOption));
            NtConfig.MarkPlacementMode = settings.MarkPlacement == MarkPlacementMode.Center ? 1 : 0;
            NtConfig.CircleStartAngleDeg = Clamp(settings.MarkStartAngleDeg, -360.0, 360.0, 45.0);
            NtConfig.CircleAngleStepDeg = Clamp(settings.MarkAngleStepDeg, 1.0, 360.0, 45.0);
            NtConfig.CircleMaxRotateTries = ClampInt(settings.MarkRotateTries, 1, 72, 8);
            NtConfig.CircleOffsetMarginFactor = Clamp(settings.MarkDistanceScale, 0.01, 100.0, 0.60);
            NtConfig.CircleRingStepFactor = Clamp(settings.MarkRingStepScale, 0.01, 100.0, 0.80);
            NtConfig.MarkMinDistFactor = Clamp(settings.MarkMinDistanceScale, 0.01, 100.0, 1.50);
            NtConfig.NoteWireHole = settings.NoteWireHole ? 1 : 0;
            cfg.PitchToggle = settings.NoteThreadPitch ? 1 : 0;
        }

        private static Point3d? PromptPointOrNull(Editor ed, string message)
        {
            if (ed == null) return null;
            var opt = new PromptPointOptions("\n" + (string.IsNullOrWhiteSpace(message) ? "Pick point:" : message));
            PromptPointResult res = ed.GetPoint(opt);
            if (res.Status == PromptStatus.OK) return res.Value;
            return null;
        }

        private static DimnotePromptResult PromptDimtxtAndMode(Editor ed, double defaultValue, RuntimeSettings settings, Database db)
        {
            string defaultText = defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

            while (true)
            {
                string angleText = ((int)settings.Angle).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var opt = new PromptStringOptions(Lang.F("Prompt_DimtxtSettings", angleText, defaultText))
                {
                    AllowSpaces = false
                };

                PromptResult res = ed.GetString(opt);
                if (res.Status == PromptStatus.None || res.Status == PromptStatus.Cancel)
                {
                    return new DimnotePromptResult { Dimtxt = defaultValue, ProcessingMode = settings.ProcessingMode, Angle = settings.Angle };
                }
                if (res.Status != PromptStatus.OK)
                {
                    return new DimnotePromptResult { Dimtxt = defaultValue, ProcessingMode = settings.ProcessingMode, Angle = settings.Angle };
                }

                string value = (res.StringResult ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(value))
                {
                    return new DimnotePromptResult { Dimtxt = defaultValue, ProcessingMode = settings.ProcessingMode, Angle = settings.Angle };
                }

                if (string.Equals(value, "S", StringComparison.OrdinalIgnoreCase))
                {
                    PromptForSettings(ed, settings, db);
                    continue;
                }

                string normalized = value.Replace(',', '.');
                double parsed;
                if (double.TryParse(normalized,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out parsed) && parsed > 0.0)
                {
                    settings.DimTextHeight = parsed;
                    SaveUserSettings(ed, db, settings);
                    return new DimnotePromptResult { Dimtxt = parsed, ProcessingMode = settings.ProcessingMode, Angle = settings.Angle };
                }

                ed.WriteMessage(Lang.T("Err_InvalidDimtxtOrSettings"));
            }
        }

        private static void PromptForSettings(Editor ed, RuntimeSettings settings, Database db)
        {
            if (settings == null || db == null) return;

            double dimtxt = DimUtils.GetCurrentDimtxt(2.5);
            DDimnotesNoteConfig cfg = DDimnotesNotes.LoadConfig(db, ed, dimtxt, settings.NoteWidthFactor, settings.NoteTextColorIndex);

            double resetWidth = cfg.NoteWidthFactor > 0.0 ? cfg.NoteWidthFactor : MisSetIO.DefaultFactor;
            short resetNoteColor = cfg.NoteTextColorIndex >= 1 && cfg.NoteTextColorIndex <= 255 ? cfg.NoteTextColorIndex : MisSetIO.DefaultNoteTextColor;
            short resetLineColor = cfg.LineColorIndex >= 1 && cfg.LineColorIndex <= 255 ? cfg.LineColorIndex : MisSetIO.DefaultLineColor;
            short resetMarkColor = cfg.MarkColorIndex >= 1 && cfg.MarkColorIndex <= 255 ? cfg.MarkColorIndex : MisSetIO.DefaultMarkColor;

            if (settings.NoteWidthFactor <= 0.0) settings.NoteWidthFactor = resetWidth;
            if (settings.NoteTextColorIndex < 1 || settings.NoteTextColorIndex > 255) settings.NoteTextColorIndex = resetNoteColor;
            if (settings.DimLineColorIndex < 1 || settings.DimLineColorIndex > 255) settings.DimLineColorIndex = resetLineColor;
            if (settings.DimTextColorIndex < 1 || settings.DimTextColorIndex > 255) settings.DimTextColorIndex = resetNoteColor;
            if (settings.MarkColorIndex < 1 || settings.MarkColorIndex > 255) settings.MarkColorIndex = resetMarkColor;

            using (var form = new DDimnotesSettingsForm(
                settings.Mode.BoundaryXminYminDims,
                settings.Mode.BoundaryXminYmaxDims,
                settings.Mode.BoundaryXmaxYminDims,
                settings.Mode.BoundaryXmaxYmaxDims,
                settings.Mode.OuterProfileVertexDims,
                settings.Mode.HoleCenterDims,
                settings.Mode.ArcCenterDims,
                settings.Mode.DepthHoleDims,
                settings.Mode.InnerProfileDims,
                settings.Mode.SideThicknessDims,
                settings.Mode.SideProjectionHoleDims,
                settings.Mode.SideProjectionDepthDims,
                settings.TolBoundaryXminYmin,
                settings.TolBoundaryXminYmax,
                settings.TolBoundaryXmaxYmin,
                settings.TolBoundaryXmaxYmax,
                settings.TolOuterProfileVertices,
                settings.TolHoleCenters,
                settings.TolArcCenters,
                settings.TolBlindHoles,
                settings.TolInnerProfiles,
                settings.TolSideThickness,
                settings.TolSideProjectionHoles,
                settings.TolSideProjectionDepths,
                (int)settings.Angle,
                settings.SidePlacement == SidePlacementMode.PickPoint ? 1 : 0,
                settings.SideViewDistanceScale,
                settings.DimTextScale,
                settings.BoundaryDimMode == BoundaryDimMode.Linear ? 1 : 0,
                settings.DimOffsetScale,
                settings.DeleteDuplicateDim,
                settings.DimLineColorIndex,
                settings.DimTextColorIndex,
                settings.CreateNotes,
                settings.CreateNoteMarks,
                settings.NoteWireHole,
                settings.NoteThreadPitch,
                NormalizeNoteTextStyleOption(settings.NoteTextStyleOption),
                NormalizeMarkTextStyleOption(settings.MarkTextStyleOption),
                settings.NoteWidthFactor > 0.0 ? settings.NoteWidthFactor : resetWidth,
                settings.MarkTextScale,
                settings.NoteTextColorIndex >= 1 && settings.NoteTextColorIndex <= 255 ? settings.NoteTextColorIndex : resetNoteColor,
                settings.MarkColorIndex >= 1 && settings.MarkColorIndex <= 255 ? settings.MarkColorIndex : resetMarkColor,
                settings.NoteInsertPositionKey,
                settings.NoteOffsetXScale,
                settings.NoteOffsetYScale,
                settings.MarkPlacement == MarkPlacementMode.Center ? 1 : 0,
                settings.MarkStartAngleDeg,
                settings.MarkAngleStepDeg,
                settings.MarkRotateTries,
                settings.MarkDistanceScale,
                settings.MarkRingStepScale,
                settings.MarkMinDistanceScale,
                settings.SideOutlineLayerOption,
                settings.SideViewLayerOption,
                settings.NoteLayerOption,
                settings.MarkLayerOption,
                settings.DimLayerOption))
            {
                System.Windows.Forms.DialogResult result;
                try
                {
                    result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalDialog(form);
                }
                catch
                {
                    result = form.ShowDialog();
                }

                if (result != System.Windows.Forms.DialogResult.OK)
                    return;

                settings.Mode.BoundaryXminYminDims = form.Mode.BoundaryXminYminDims;
                settings.Mode.BoundaryXminYmaxDims = form.Mode.BoundaryXminYmaxDims;
                settings.Mode.BoundaryXmaxYminDims = form.Mode.BoundaryXmaxYminDims;
                settings.Mode.BoundaryXmaxYmaxDims = form.Mode.BoundaryXmaxYmaxDims;
                settings.Mode.OuterProfileVertexDims = form.Mode.OuterProfileVertexDims;
                settings.Mode.HoleCenterDims = form.Mode.HoleCenterDims;
                settings.Mode.ArcCenterDims = form.Mode.ArcCenterDims;
                settings.Mode.DepthHoleDims = form.Mode.DepthHoleDims;
                settings.Mode.InnerProfileDims = form.Mode.InnerProfileDims;
                settings.Mode.SideThicknessDims = form.Mode.SideThicknessDims;
                settings.Mode.SideProjectionHoleDims = form.Mode.SideProjectionHoleDims;
                settings.Mode.SideProjectionDepthDims = form.Mode.SideProjectionDepthDims;
                settings.TolBoundaryXminYmin = form.TolBoundaryXminYmin;
                settings.TolBoundaryXminYmax = form.TolBoundaryXminYmax;
                settings.TolBoundaryXmaxYmin = form.TolBoundaryXmaxYmin;
                settings.TolBoundaryXmaxYmax = form.TolBoundaryXmaxYmax;
                settings.TolOuterProfileVertices = form.TolOuterProfileVertices;
                settings.TolHoleCenters = form.TolHoleCenters;
                settings.TolArcCenters = form.TolArcCenters;
                settings.TolBlindHoles = form.TolBlindHoles;
                settings.TolInnerProfiles = form.TolInnerProfiles;
                settings.TolSideThickness = form.TolSideThickness;
                settings.TolSideProjectionHoles = form.TolSideProjectionHoles;
                settings.TolSideProjectionDepths = form.TolSideProjectionDepths;
                if (form.RequestCustomProjectionAngle)
                    PromptForProjectionAngle(ed, settings);
                else
                    settings.Angle = AngleToViewDir(form.ProjectionAngle);
                settings.SidePlacement = form.SidePlacementModeIndex == 1 ? SidePlacementMode.PickPoint : SidePlacementMode.Auto;
                settings.SideViewDistanceScale = form.SideViewDistanceScale;
                settings.DimTextScale = form.DimTextScale;
                settings.BoundaryDimMode = form.BoundaryDimModeIndex == 1 ? BoundaryDimMode.Linear : BoundaryDimMode.Ordinate;
                settings.DimOffsetScale = form.DimOffsetScale;
                settings.DeleteDuplicateDim = form.DeleteDuplicateDim;
                settings.DimLineColorIndex = form.DimLineColorIndex;
                settings.DimTextColorIndex = form.DimTextColorIndex;
                settings.CreateNotes = form.CreateNotes;
                settings.CreateNoteMarks = form.CreateNoteMarks;
                settings.NoteWireHole = form.NoteWireHole;
                settings.NoteThreadPitch = form.NoteThreadPitch;
                settings.NoteTextStyleOption = NormalizeNoteTextStyleOption(form.NoteTextStyleOption);
                settings.MarkTextStyleOption = NormalizeMarkTextStyleOption(form.MarkTextStyleOption);
                settings.NoteWidthFactor = form.NoteWidthFactor;
                settings.MarkTextScale = form.MarkTextScale;
                settings.NoteTextColorIndex = form.NoteTextColorIndex;
                settings.MarkColorIndex = form.MarkColorIndex;
                settings.NoteInsertPositionKey = form.NoteInsertPositionKey;
                settings.NoteOffsetXScale = form.NoteOffsetXScale;
                settings.NoteOffsetYScale = form.NoteOffsetYScale;
                settings.MarkPlacement = form.MarkPlacementModeIndex == 1 ? MarkPlacementMode.Center : MarkPlacementMode.Auto;
                settings.MarkStartAngleDeg = form.MarkStartAngleDeg;
                settings.MarkAngleStepDeg = form.MarkAngleStepDeg;
                settings.MarkRotateTries = form.MarkRotateTries;
                settings.MarkDistanceScale = form.MarkDistanceScale;
                settings.MarkRingStepScale = form.MarkRingStepScale;
                settings.MarkMinDistanceScale = form.MarkMinDistanceScale;
                settings.SideOutlineLayerOption = form.SideOutlineLayerOption;
                settings.SideViewLayerOption = form.SideViewLayerOption;
                settings.NoteLayerOption = form.NoteLayerOption;
                settings.MarkLayerOption = form.MarkLayerOption;
                settings.DimLayerOption = form.DimLayerOption;
                settings.SideHoleLayerOption = settings.SideViewLayerOption;

                SaveUserSettings(ed, db, settings);
            }
        }

        private static bool ValidateRuntimeLayerCompatibility(Editor ed, RuntimeSettings settings)
        {
            if (settings == null) return true;
            if (LayerCatalog.IsSourceObjectOption(settings.NoteLayerOption))
                return WriteLayerIncompatibleMessage(ed, Lang.T("Form_LayerNotes"));
            if (LayerCatalog.IsSourceObjectOption(settings.MarkLayerOption))
                return WriteLayerIncompatibleMessage(ed, Lang.T("Form_LayerMarks"));
            if (LayerCatalog.IsSourceObjectOption(settings.DimLayerOption))
                return WriteLayerIncompatibleMessage(ed, Lang.T("Form_LayerDimensions"));
            return true;
        }

        private static bool WriteLayerIncompatibleMessage(Editor ed, string rowName)
        {
            string message = Lang.F("Msg_LayerSourceNotCompatible", rowName);
            if (string.IsNullOrWhiteSpace(message) || message.IndexOf("Msg_LayerSourceNotCompatible", StringComparison.OrdinalIgnoreCase) >= 0)
                message = rowName + ": Layer của đối tượng gốc không tương thích với mục này.";
            if (ed != null) ed.WriteMessage("\n" + message);
            return false;
        }

        private const string UserSettingsDirectoryName = "DUser";
        private const string UserSettingsFileName = "DDimnotes_settings.ini";

        private static void EnsureUserSettingsLoaded(Editor ed, Database db, RuntimeSettings settings)
        {
            if (settings == null || db == null || settings.UserSettingsChecked) return;
            settings.UserSettingsChecked = true;

            string path;
            string dir;
            if (!TryFindUserSettingsPath(db, out path, out dir))
            {
                settings.UserSettingsCanSave = false;
                settings.UserSettingsPath = null;
                ShowUserSettingsMissingMessage(ed);
                return;
            }

            settings.UserSettingsCanSave = true;
            settings.UserSettingsPath = path;

            if (!File.Exists(path)) return;

            try
            {
                string[] raw = File.ReadAllLines(path);
                var values = new List<string>();
                foreach (string line in raw)
                {
                    string v = (line ?? string.Empty).Trim();
                    // Keep blank lines because settings are position-based.
                    values.Add(v);
                }

                int iv;
                double dv;

                if (TryReadInt(values, 0, out iv)) settings.ProcessingMode = IndexToMode(iv, settings.ProcessingMode);
                if (TryReadInt(values, 1, out iv)) settings.Angle = IndexToAngle(iv, settings.Angle);
                if (TryReadInt(values, 2, out iv)) settings.CreateNotes = iv != 0;
                if (TryReadInt(values, 3, out iv)) settings.CreateNoteMarks = iv != 0;
                if (TryReadDouble(values, 4, out dv)) settings.NoteWidthFactor = Clamp(dv, 0.01, 5.0, settings.NoteWidthFactor);
                if (TryReadInt(values, 5, out iv)) settings.NoteTextColorIndex = (short)ClampInt(iv, 1, 255, settings.NoteTextColorIndex);
                if (TryReadDouble(values, 6, out dv)) settings.SideViewDistanceScale = Clamp(dv, 0.01, 100.0, settings.SideViewDistanceScale);
                if (TryReadDouble(values, 7, out dv)) settings.DimOffsetScale = Clamp(dv, 0.01, 100.0, settings.DimOffsetScale);
                if (TryReadInt(values, 8, out iv)) settings.NoteInsertPositionKey = IndexToNoteAnchor(iv);
                if (TryReadDouble(values, 9, out dv)) settings.NoteOffsetXScale = Clamp(dv, -100.0, 100.0, settings.NoteOffsetXScale);
                if (TryReadDouble(values, 10, out dv)) settings.NoteOffsetYScale = Clamp(dv, -100.0, 100.0, settings.NoteOffsetYScale);
                if (TryReadDouble(values, 11, out dv)) settings.DimTextHeight = Clamp(dv, 0.01, 100000.0, settings.DimTextHeight);
                if (TryReadInt(values, 12, out iv)) settings.BoundaryDimMode = iv == 1 ? BoundaryDimMode.Linear : BoundaryDimMode.Ordinate;
                if (TryReadDouble(values, 13, out dv)) settings.DimTextScale = Clamp(dv, 0.01, 10.0, settings.DimTextScale);
                if (TryReadInt(values, 14, out iv)) settings.DimLineColorIndex = (short)ClampInt(iv, 1, 255, settings.DimLineColorIndex);
                if (TryReadInt(values, 15, out iv)) settings.DimTextColorIndex = (short)ClampInt(iv, 1, 255, settings.DimTextColorIndex);
                if (TryReadDouble(values, 16, out dv)) settings.MarkTextScale = Clamp(dv, 0.01, 5.0, settings.MarkTextScale);
                if (TryReadInt(values, 17, out iv)) settings.MarkColorIndex = (short)ClampInt(iv, 1, 255, settings.MarkColorIndex);
                if (TryReadInt(values, 18, out iv)) settings.SidePlacement = iv == 1 ? SidePlacementMode.PickPoint : SidePlacementMode.Auto;
                if (TryReadDouble(values, 19, out dv)) settings.MarkDistanceScale = Clamp(dv, 0.01, 100.0, settings.MarkDistanceScale);
                if (TryReadDouble(values, 20, out dv)) settings.MarkMinDistanceScale = Clamp(dv, 0.01, 100.0, settings.MarkMinDistanceScale);
                if (TryReadInt(values, 21, out iv)) settings.Mode.BoundaryXminYminDims = iv != 0;
                if (TryReadInt(values, 22, out iv)) settings.Mode.BoundaryXminYmaxDims = iv != 0;
                if (TryReadInt(values, 23, out iv)) settings.Mode.BoundaryXmaxYminDims = iv != 0;
                if (TryReadInt(values, 24, out iv)) settings.Mode.BoundaryXmaxYmaxDims = iv != 0;
                if (TryReadInt(values, 25, out iv)) settings.Mode.OuterProfileVertexDims = iv != 0;
                if (TryReadInt(values, 26, out iv)) settings.Mode.HoleCenterDims = iv != 0;
                if (TryReadInt(values, 27, out iv)) settings.Mode.ArcCenterDims = iv != 0;
                if (TryReadInt(values, 28, out iv)) settings.Mode.DepthHoleDims = iv != 0;
                if (TryReadInt(values, 29, out iv)) settings.Mode.InnerProfileDims = iv != 0;
                if (TryReadInt(values, 30, out iv)) settings.DeleteDuplicateDim = iv != 0;
                if (TryReadInt(values, 31, out iv)) settings.MarkPlacement = iv == 1 ? MarkPlacementMode.Center : MarkPlacementMode.Auto;
                if (TryReadDouble(values, 32, out dv)) settings.MarkStartAngleDeg = Clamp(dv, -360.0, 360.0, settings.MarkStartAngleDeg);
                if (TryReadDouble(values, 33, out dv)) settings.MarkAngleStepDeg = Clamp(dv, 1.0, 360.0, settings.MarkAngleStepDeg);
                if (TryReadInt(values, 34, out iv)) settings.MarkRotateTries = ClampInt(iv, 1, 72, settings.MarkRotateTries);
                if (TryReadDouble(values, 35, out dv)) settings.MarkRingStepScale = Clamp(dv, 0.01, 100.0, settings.MarkRingStepScale);
                if (TryReadInt(values, 36, out iv)) settings.Mode.SideThicknessDims = iv != 0;
                if (TryReadInt(values, 37, out iv)) settings.Mode.SideProjectionHoleDims = iv != 0;
                if (TryReadInt(values, 38, out iv)) settings.Mode.SideProjectionDepthDims = iv != 0;
                settings.TolBoundaryXminYmin = TryReadString(values, 39, settings.TolBoundaryXminYmin);
                settings.TolBoundaryXminYmax = TryReadString(values, 40, settings.TolBoundaryXminYmax);
                settings.TolBoundaryXmaxYmin = TryReadString(values, 41, settings.TolBoundaryXmaxYmin);
                settings.TolBoundaryXmaxYmax = TryReadString(values, 42, settings.TolBoundaryXmaxYmax);
                settings.TolOuterProfileVertices = TryReadString(values, 43, settings.TolOuterProfileVertices);
                settings.TolHoleCenters = TryReadString(values, 44, settings.TolHoleCenters);
                settings.TolArcCenters = TryReadString(values, 45, settings.TolArcCenters);
                settings.TolBlindHoles = TryReadString(values, 46, settings.TolBlindHoles);
                settings.TolInnerProfiles = TryReadString(values, 47, settings.TolInnerProfiles);
                settings.TolSideThickness = TryReadString(values, 48, settings.TolSideThickness);
                settings.TolSideProjectionHoles = TryReadString(values, 49, settings.TolSideProjectionHoles);
                settings.TolSideProjectionDepths = TryReadString(values, 50, settings.TolSideProjectionDepths);
                settings.SideViewLayerOption = TryReadString(values, 51, settings.SideViewLayerOption);
                settings.NoteLayerOption = TryReadString(values, 52, settings.NoteLayerOption);
                settings.MarkLayerOption = TryReadString(values, 53, settings.MarkLayerOption);
                settings.DimLayerOption = TryReadString(values, 54, settings.DimLayerOption);
                settings.SideHoleLayerOption = TryReadString(values, 55, settings.SideHoleLayerOption);
                settings.SideOutlineLayerOption = TryReadString(values, 56, settings.SideOutlineLayerOption);
                if (TryReadInt(values, 57, out iv)) settings.NoteWireHole = iv != 0;
                if (TryReadInt(values, 58, out iv)) settings.NoteThreadPitch = iv != 0;
                settings.NoteTextStyleOption = NormalizeNoteTextStyleOption(TryReadString(values, 59, settings.NoteTextStyleOption));
                settings.MarkTextStyleOption = NormalizeMarkTextStyleOption(TryReadString(values, 60, settings.MarkTextStyleOption));
            }
            catch (System.Exception ex)
            {
                if (ed != null) ed.WriteMessage(Lang.F("Msg_UserSettingsReadError", ex.Message));
            }
        }

        private static void SaveUserSettings(Editor ed, Database db, RuntimeSettings settings)
        {
            if (settings == null || db == null) return;

            string path = settings.UserSettingsPath;
            string dir;
            if (string.IsNullOrEmpty(path) || !settings.UserSettingsCanSave)
            {
                string foundPath;
                if (!TryFindUserSettingsPath(db, out foundPath, out dir))
                {
                    ShowUserSettingsMissingMessage(ed);
                    return;
                }
                path = foundPath;
                settings.UserSettingsPath = path;
                settings.UserSettingsCanSave = true;
            }

            try
            {
                string[] lines = new[]
                {
                    ModeToIndex(settings.ProcessingMode).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    AngleToIndex(settings.Angle).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.CreateNotes ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.CreateNoteMarks ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatDouble(settings.NoteWidthFactor),
                    ((int)settings.NoteTextColorIndex).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatDouble(settings.SideViewDistanceScale),
                    FormatDouble(settings.DimOffsetScale),
                    NoteAnchorToIndex(settings.NoteInsertPositionKey).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatDouble(settings.NoteOffsetXScale),
                    FormatDouble(settings.NoteOffsetYScale),
                    FormatDouble(settings.DimTextHeight),
                    (settings.BoundaryDimMode == BoundaryDimMode.Linear ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatDouble(settings.DimTextScale),
                    ((int)settings.DimLineColorIndex).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ((int)settings.DimTextColorIndex).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatDouble(settings.MarkTextScale),
                    ((int)settings.MarkColorIndex).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.SidePlacement == SidePlacementMode.PickPoint ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatDouble(settings.MarkDistanceScale),
                    FormatDouble(settings.MarkMinDistanceScale),
                    (settings.Mode.BoundaryXminYminDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.BoundaryXminYmaxDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.BoundaryXmaxYminDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.BoundaryXmaxYmaxDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.OuterProfileVertexDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.HoleCenterDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.ArcCenterDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.DepthHoleDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.InnerProfileDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.DeleteDuplicateDim ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.MarkPlacement == MarkPlacementMode.Center ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatDouble(settings.MarkStartAngleDeg),
                    FormatDouble(settings.MarkAngleStepDeg),
                    settings.MarkRotateTries.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatDouble(settings.MarkRingStepScale),
                    (settings.Mode.SideThicknessDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.SideProjectionHoleDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.Mode.SideProjectionDepthDims ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    settings.TolBoundaryXminYmin ?? string.Empty,
                    settings.TolBoundaryXminYmax ?? string.Empty,
                    settings.TolBoundaryXmaxYmin ?? string.Empty,
                    settings.TolBoundaryXmaxYmax ?? string.Empty,
                    settings.TolOuterProfileVertices ?? string.Empty,
                    settings.TolHoleCenters ?? string.Empty,
                    settings.TolArcCenters ?? string.Empty,
                    settings.TolBlindHoles ?? string.Empty,
                    settings.TolInnerProfiles ?? string.Empty,
                    settings.TolSideThickness ?? string.Empty,
                    settings.TolSideProjectionHoles ?? string.Empty,
                    settings.TolSideProjectionDepths ?? string.Empty,
                    settings.SideViewLayerOption ?? LayerCatalog.DefaultValue,
                    settings.NoteLayerOption ?? "t|TEXT",
                    settings.MarkLayerOption ?? "t|TEXT",
                    settings.DimLayerOption ?? "d|DIM",
                    settings.SideHoleLayerOption ?? settings.SideViewLayerOption ?? LayerCatalog.DefaultValue,
                    settings.SideOutlineLayerOption ?? LayerCatalog.SourceObjectValue,
                    (settings.NoteWireHole ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (settings.NoteThreadPitch ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NormalizeNoteTextStyleOption(settings.NoteTextStyleOption),
                    NormalizeMarkTextStyleOption(settings.MarkTextStyleOption)
                };
                File.WriteAllLines(path, lines);
            }
            catch (System.Exception ex)
            {
                if (ed != null) ed.WriteMessage(Lang.F("Msg_UserSettingsWriteError", ex.Message));
            }
        }

        private static bool TryFindUserSettingsPath(Database db, out string settingsPath, out string directoryPath)
        {
            settingsPath = null;
            directoryPath = null;

            try
            {
                string p = HostApplicationServices.Current.FindFile(Path.Combine(UserSettingsDirectoryName, UserSettingsFileName), db, FindFileHint.Default);
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    settingsPath = p;
                    directoryPath = Path.GetDirectoryName(p);
                    return true;
                }
            }
            catch { }

            try
            {
                string p = HostApplicationServices.Current.FindFile(UserSettingsFileName, db, FindFileHint.Default);
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    string pd = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(pd) && string.Equals(new DirectoryInfo(pd).Name, UserSettingsDirectoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        settingsPath = p;
                        directoryPath = pd;
                        return true;
                    }
                }
            }
            catch { }

            foreach (string basePath in GetSupportPaths())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(basePath)) continue;
                    string full = basePath.Trim().Trim('"');
                    if (full.Length == 0) continue;

                    if (Directory.Exists(full) && string.Equals(new DirectoryInfo(full).Name, UserSettingsDirectoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        directoryPath = full;
                        settingsPath = Path.Combine(directoryPath, UserSettingsFileName);
                        return true;
                    }

                    string child = Path.Combine(full, UserSettingsDirectoryName);
                    if (Directory.Exists(child))
                    {
                        directoryPath = child;
                        settingsPath = Path.Combine(directoryPath, UserSettingsFileName);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static IEnumerable<string> GetSupportPaths()
        {
            object raw = null;
            try { raw = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("ACADPREFIX"); }
            catch { }

            string s = raw as string;
            if (string.IsNullOrEmpty(s)) yield break;

            foreach (string part in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                yield return part;
        }

        private static void ShowUserSettingsMissingMessage(Editor ed)
        {
            string msg = Lang.T("Msg_UserSettingsMissing");
            try { Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog(msg); }
            catch { if (ed != null) ed.WriteMessage("\n" + msg.Replace("\n", " ")); }
        }

        private static bool TryReadInt(List<string> values, int index, out int value)
        {
            value = 0;
            return index >= 0 && index < values.Count && int.TryParse(values[index], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadDouble(List<string> values, int index, out double value)
        {
            value = 0.0;
            if (index < 0 || index >= values.Count) return false;
            string text = values[index].Replace(',', '.');
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static string TryReadString(List<string> values, int index, string fallback)
        {
            if (index < 0 || index >= values.Count) return fallback ?? string.Empty;
            return (values[index] ?? string.Empty).Trim();
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static double Clamp(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max) return fallback;
            return value;
        }

        private static int ClampInt(int value, int min, int max, int fallback)
        {
            if (value < min || value > max) return fallback;
            return value;
        }

        private static string IndexToNoteAnchor(int index)
        {
            if (index == 1) return "XminYmin";
            if (index == 2) return "XminYmax";
            if (index == 3) return "XmaxYmin";
            if (index == 4) return "PickPoint";
            return "XmaxYmax";
        }

        private static int NoteAnchorToIndex(string key)
        {
            if (string.Equals(key, "XminYmin", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(key, "XminYmax", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(key, "XmaxYmin", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(key, "PickPoint", StringComparison.OrdinalIgnoreCase)) return 4;
            return 0;
        }

        private static int ModeToIndex(DimnoteMeasureMode mode)
        {
            if (mode == DimnoteMeasureMode.Boundary) return 1;
            if (mode == DimnoteMeasureMode.All) return 2;
            if (mode == DimnoteMeasureMode.NoNotes) return 3;
            return 0;
        }

        private static DimnoteMeasureMode IndexToMode(int index, DimnoteMeasureMode fallback)
        {
            if (index == 1) return DimnoteMeasureMode.Boundary;
            if (index == 2) return DimnoteMeasureMode.All;
            if (index == 3) return DimnoteMeasureMode.NoNotes;
            if (index == 0) return DimnoteMeasureMode.DepthBoundary;
            return fallback;
        }

        private static int AngleToIndex(ViewDir angle)
        {
            if (angle == ViewDir.Deg0) return 0;
            if (angle == ViewDir.Deg90) return 1;
            if (angle == ViewDir.Deg180) return 2;
            return 3;
        }

        private static ViewDir IndexToAngle(int index, ViewDir fallback)
        {
            if (index == 0) return ViewDir.Deg0;
            if (index == 1) return ViewDir.Deg90;
            if (index == 2) return ViewDir.Deg180;
            if (index == 3) return ViewDir.Deg270;
            return fallback;
        }

        private static Point3d ComputeNoteInsertPoint(BBox2d mainBox, BBox2d? sideLayoutBox, string noteInsertPositionKey, double dimTextHeight, double offsetXScale, double offsetYScale)
        {
            BBox2d layout = UnionBox(mainBox, sideLayoutBox);
            double dx = offsetXScale * dimTextHeight;
            double dy = offsetYScale * dimTextHeight;

            if (string.Equals(noteInsertPositionKey, "XminYmin", StringComparison.OrdinalIgnoreCase))
                return new Point3d(layout.Xmin + dx, layout.Ymin - dy, 0.0);
            if (string.Equals(noteInsertPositionKey, "XminYmax", StringComparison.OrdinalIgnoreCase))
                return new Point3d(layout.Xmin + dx, layout.Ymax + dy, 0.0);
            if (string.Equals(noteInsertPositionKey, "XmaxYmin", StringComparison.OrdinalIgnoreCase))
                return new Point3d(layout.Xmax + dx, layout.Ymin - dy, 0.0);

            return new Point3d(layout.Xmax + dx, layout.Ymax + dy, 0.0);
        }

        private static BBox2d UnionBox(BBox2d a, BBox2d? b)
        {
            if (b == null) return a;
            return new BBox2d(
                Math.Min(a.Xmin, b.Xmin),
                Math.Min(a.Ymin, b.Ymin),
                Math.Max(a.Xmax, b.Xmax),
                Math.Max(a.Ymax, b.Ymax));
        }

        private static DimnoteMeasureMode KeywordToMode(string keyword)
        {
            if (string.Equals(keyword, "A", StringComparison.OrdinalIgnoreCase)) return DimnoteMeasureMode.All;
            if (string.Equals(keyword, "B", StringComparison.OrdinalIgnoreCase)) return DimnoteMeasureMode.Boundary;
            if (string.Equals(keyword, "N", StringComparison.OrdinalIgnoreCase)) return DimnoteMeasureMode.NoNotes;
            return DimnoteMeasureMode.DepthBoundary;
        }

        private static bool UsesDepthBoundaryProjection(DimnoteMeasureMode mode)
        {
            return mode == DimnoteMeasureMode.DepthBoundary || mode == DimnoteMeasureMode.NoNotes;
        }

        private static ViewDir AngleToViewDir(int angle)
        {
            if (angle == 0) return ViewDir.Deg0;
            if (angle == 90) return ViewDir.Deg90;
            if (angle == 180) return ViewDir.Deg180;
            return ViewDir.Deg270;
        }

        private static void GetCurrentDimStyleInfo(Database db, out string dimStyleName, out string dimTextStyleName)
        {
            dimStyleName = "-";
            dimTextStyleName = "-";

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var dstr = tr.GetObject(db.Dimstyle, OpenMode.ForRead) as DimStyleTableRecord;
                    if (dstr != null)
                    {
                        dimStyleName = dstr.Name;
                        ObjectId textStyleId = dstr.Dimtxsty;
                        if (!textStyleId.IsNull)
                        {
                            var ts = tr.GetObject(textStyleId, OpenMode.ForRead) as TextStyleTableRecord;
                            if (ts != null) dimTextStyleName = ts.Name;
                        }
                    }
                    tr.Commit();
                }
            }
            catch { }
        }

        private static bool HasMetricThreadHole(IEnumerable<HoleRec> holes)
        {
            if (holes == null) return false;
            foreach (HoleRec h in holes)
            {
                if (h == null) continue;
                if (!string.IsNullOrWhiteSpace(h.MetricThreadSizeKey)) return true;
                if (!string.IsNullOrWhiteSpace(ThreadTable.BuildMetricThreadSizeKey(h.Code, h.Dia))) return true;
            }
            return false;
        }

        private static bool HasMetricSideThread(IEnumerable<SideThreadRec> sideThreads)
        {
            if (sideThreads == null) return false;
            foreach (SideThreadRec st in sideThreads)
            {
                if (st == null) continue;
                if (!string.IsNullOrWhiteSpace(st.MetricThreadSizeKey)) return true;
                if (!string.IsNullOrWhiteSpace(ThreadTable.BuildMetricThreadSizeKey(st.Code, st.Dia))) return true;
            }
            return false;
        }

        private static bool RequireSupportFile(Editor ed, Database db, string[] names, string displayName)
        {
            string path = ResolveSupportFile(db, names);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return true;

            string msg = Lang.F("Msg_SupportFileMissing", displayName);
            try { ed.WriteMessage(msg); } catch { }
            try { Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog(msg.Trim()); } catch { }
            return false;
        }

        private static string ResolveSupportFile(Database db, string[] names)
        {
            if (names == null) return null;
            foreach (string name in names)
            {
                string path = TryFindSupportFile(db, name);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            return null;
        }

        private static string BuildSupportFileStatus(Database db, string[] names)
        {
            if (names == null || names.Length == 0) return Lang.T("Msg_Unknown");

            foreach (string name in names)
            {
                string path = TryFindSupportFile(db, name);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }

            return Lang.F("Msg_SupportFileMissing", string.Join("/", names)).Trim();
        }

        private static string TryFindSupportFile(Database db, string fileName)
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

        private static void PromptForNoteWidthFactor(Editor ed, RuntimeSettings settings)
        {
            while (true)
            {
                var opt = new PromptStringOptions(Lang.F("Cmd_PromptScaleRatio", settings.NoteWidthFactor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))
                {
                    AllowSpaces = false
                };
                PromptResult res = ed.GetString(opt);
                if (res.Status == PromptStatus.None || res.Status == PromptStatus.Cancel) return;
                if (res.Status != PromptStatus.OK) return;
                string s = (res.StringResult ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(s)) return;
                double v;
                if (double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)
                    && v > 0.0 && v <= 5.0)
                {
                    settings.NoteWidthFactor = v;
                    return;
                }
                ed.WriteMessage(Lang.T("Err_InvalidScaleRatio_Positive"));
            }
        }

        private static void PromptForNoteColor(Editor ed, RuntimeSettings settings, Database db)
        {
            while (true)
            {
                var opt = new PromptStringOptions(Lang.F("Cmd_PromptColorOrPick", settings.NoteTextColorIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    AllowSpaces = false
                };
                PromptResult res = ed.GetString(opt);
                if (res.Status == PromptStatus.None || res.Status == PromptStatus.Cancel) return;
                if (res.Status != PromptStatus.OK) return;
                string s = (res.StringResult ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(s)) return;

                if (string.Equals(s, "P", StringComparison.OrdinalIgnoreCase))
                {
                    var peo = new PromptEntityOptions(Lang.T("Cmd_PromptSelectRefEntity"));
                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status == PromptStatus.Cancel) return;
                    if (per.Status != PromptStatus.OK) continue;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        Entity ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent != null && ent.ColorIndex >= 1 && ent.ColorIndex <= 255)
                        {
                            settings.NoteTextColorIndex = (short)ent.ColorIndex;
                            tr.Commit();
                            return;
                        }
                        tr.Commit();
                    }
                    continue;
                }

                int ci;
                if (int.TryParse(s, out ci) && ci >= 1 && ci <= 255)
                {
                    settings.NoteTextColorIndex = (short)ci;
                    return;
                }
                ed.WriteMessage(Lang.T("Err_InvalidColorIndex_Range"));
            }
        }

        private static void PromptForProjectionAngle(Editor ed, RuntimeSettings settings)
        {
            while (true)
            {
                var opt = new PromptIntegerOptions(Lang.F("Cmd_PromptAngle", ((int)settings.Angle).ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    AllowNone = true,
                    DefaultValue = (int)settings.Angle,
                    UseDefaultValue = true,
                    AllowNegative = false,
                    AllowZero = true
                };

                PromptIntegerResult res = ed.GetInteger(opt);
                if (res.Status == PromptStatus.None || res.Status == PromptStatus.Cancel) return;
                if (res.Status != PromptStatus.OK) return;

                int a = res.Value;
                if (a == 0) { settings.Angle = ViewDir.Deg0; return; }
                if (a == 90) { settings.Angle = ViewDir.Deg90; return; }
                if (a == 180) { settings.Angle = ViewDir.Deg180; return; }
                if (a == 270) { settings.Angle = ViewDir.Deg270; return; }

                ed.WriteMessage(Lang.T("Err_InvalidAngle"));
            }
        }

        private static string ModeToKeyword(DimnoteMeasureMode mode)
        {
            switch (mode)
            {
                case DimnoteMeasureMode.All:
                    return "A";
                case DimnoteMeasureMode.Boundary:
                    return "B";
                case DimnoteMeasureMode.NoNotes:
                    return "N";
                default:
                    return "D";
            }
        }

        private static double ComputeNoteBaseDistance(BBox2d mainBox, BBox2d? sideBox, double fallback)
        {
            if (sideBox != null)
            {
                if (sideBox.Xmin >= mainBox.Xmax)
                    return Math.Max(1e-9, sideBox.Xmin - mainBox.Xmax);
                if (mainBox.Xmin >= sideBox.Xmax)
                    return Math.Max(1e-9, mainBox.Xmin - sideBox.Xmax);
            }
            return Math.Max(1e-9, fallback);
        }

        internal static BBox2d? FindSideMainBox(Database db, List<ObjectId> created)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in created)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    // main rect is the only one created with Continuous linetype in the DDimnotes plan
                    string lt = ent.Linetype;
                    if (!string.Equals(lt, "Continuous", StringComparison.OrdinalIgnoreCase))
                        continue;

                    BBox2d? box = GeomUtils.GetBBox2d(ent);
                    if (box != null)
                    {
                        tr.Commit();
                        return box;
                    }
                }
                tr.Commit();
            }
            return null;
        }

        internal static BBox2d? GuessSideMainBoxFromCreated(Database db, List<ObjectId> created)
        {
            // Best-effort: choose largest bbox among created.
            BBox2d? best = null;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in created)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    BBox2d? b = GeomUtils.GetBBox2d(ent);
                    if (b == null) continue;
                    if (best == null || b.Area > best.Area)
                        best = b;
                }
                tr.Commit();
            }
            return best;
        }

        internal static List<ObjectId> GetSelectionIds(Editor ed)
        {
            PromptSelectionResult implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK)
                return implied.Value.GetObjectIds().ToList();

            PromptSelectionOptions pso = new PromptSelectionOptions { MessageForAdding = Lang.T("Prompt_SelectObjects") };
            PromptSelectionResult res = ed.GetSelection(pso);
            if (res.Status != PromptStatus.OK)
                return new List<ObjectId>();
            return res.Value.GetObjectIds().ToList();
        }

        internal static double PromptThickness(Editor ed, double defaultValue)
        {
            var opt = new PromptDoubleOptions(Lang.F("Prompt_Thickness", defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = defaultValue,
                UseDefaultValue = true
            };

            PromptDoubleResult res = ed.GetDouble(opt);
            if (res.Status == PromptStatus.OK)
                return res.Value;
            return defaultValue;
        }

        internal static ViewDir? PromptPick4Dir(Editor ed, Point2d center)
        {
            var opt = new PromptPointOptions(Lang.T("Prompt_PickDirection"))
            {
                BasePoint = new Point3d(center.X, center.Y, 0.0),
                UseBasePoint = true
            };

            PromptPointResult res = ed.GetPoint(opt);
            if (res.Status != PromptStatus.OK) return null;

            Vector3d v = res.Value - new Point3d(center.X, center.Y, 0.0);
            double ang = Math.Atan2(v.Y, v.X) * 180.0 / Math.PI;
            if (ang < 0) ang += 360.0;

            if (ang >= 315.0 || ang < 45.0) return ViewDir.Deg0;
            if (ang >= 45.0 && ang < 135.0) return ViewDir.Deg90;
            if (ang >= 135.0 && ang < 225.0) return ViewDir.Deg180;
            return ViewDir.Deg270;
        }

        internal static ObjectId? PromptSelectMain(Editor ed)
        {
            var opt = new PromptEntityOptions(Lang.T("Prompt_SelectMain"))
            {
                AllowNone = false
            };
            PromptEntityResult res = ed.GetEntity(opt);
            if (res.Status == PromptStatus.OK) return res.ObjectId;
            return null;
        }

        internal static ThicknessRec? MakeUserOuterMain(Database db, ObjectId id)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) return null;

                BBox2d? box = GeomUtils.GetBBox2d(ent);
                if (box == null) return null;

                var rec = new ThicknessRec(
                    id,
                    box,
                    thickness: 10.0,
                    kind: "USER_MAIN",
                    layer: ent.Layer,
                    colorIndex: GeomUtils.GetColorIndex(ent)
                );

                tr.Commit();
                return rec;
            }
        }

        /// <summary>
        /// For an X-ordinate (UsingXAxis=true), choose whether the leader/text should go to the bottom or top
        /// baseline based on which side the point is closer to.
        /// Keeps all dims on straight baselines (no diagonal stacking).
        /// </summary>
        internal static double ChooseXLeaderY(BBox2d box, Point3d p, double bottomY, double topY)
        {
            double dB = Math.Abs(p.Y - box.Ymin);
            double dT = Math.Abs(box.Ymax - p.Y);
            return (dB <= dT) ? bottomY : topY;
        }

        /// <summary>
        /// For a Y-ordinate (UsingXAxis=false), choose whether the leader/text should go to the left or right
        /// baseline based on which side the point is closer to.
        /// Keeps all dims on straight baselines (no diagonal stacking).
        /// </summary>
        internal static double ChooseYLeaderX(BBox2d box, Point3d p, double leftX, double rightX)
        {
            double dL = Math.Abs(p.X - box.Xmin);
            double dR = Math.Abs(box.Xmax - p.X);
            return (dL <= dR) ? leftX : rightX;
        }


        private static bool DragCreatedObjectsWithOrtho(Editor ed, Database db, List<ObjectId> ids, Point3d basePoint, out Vector3d displacement)
        {
            displacement = new Vector3d(0.0, 0.0, 0.0);
            if (ed == null || db == null || ids == null || ids.Count == 0)
                return false;

            object oldOrtho = null;
            bool hasOldOrtho = false;
            try
            {
                oldOrtho = Application.GetSystemVariable("ORTHOMODE");
                hasOldOrtho = true;
                Application.SetSystemVariable("ORTHOMODE", 1);
            }
            catch
            {
                // If ORTHOMODE cannot be changed in this host/version, still allow the jig.
            }

            try
            {
                var jig = new MoveCreatedObjectsJig(db, ids, basePoint, Lang.T("Prompt_PickSideViewPosition"));
                PromptResult result = ed.Drag(jig);
                if (result.Status != PromptStatus.OK)
                    return false;

                displacement = jig.Displacement;
                if (displacement.Length <= 1e-9)
                    return true;

                TransformCreatedObjects(db, ids, displacement);
                return true;
            }
            finally
            {
                if (hasOldOrtho)
                {
                    try { Application.SetSystemVariable("ORTHOMODE", oldOrtho); }
                    catch { /* keep command safe */ }
                }
            }
        }

        private static void TransformCreatedObjects(Database db, IEnumerable<ObjectId> ids, Vector3d displacement)
        {
            if (db == null || ids == null || displacement.Length <= 1e-9)
                return;

            Matrix3d transform = Matrix3d.Displacement(displacement);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    if (id == ObjectId.Null || id.IsErased)
                        continue;

                    Entity ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                    if (ent == null || ent.IsErased)
                        continue;

                    ent.TransformBy(transform);
                }
                tr.Commit();
            }
        }

        private sealed class MoveCreatedObjectsJig : DrawJig
        {
            private readonly Database _db;
            private readonly List<ObjectId> _ids;
            private readonly Point3d _basePoint;
            private readonly string _message;
            private Point3d _currentPoint;
            private Vector3d _displacement;

            public MoveCreatedObjectsJig(Database db, IEnumerable<ObjectId> ids, Point3d basePoint, string message)
            {
                _db = db;
                _ids = ids == null ? new List<ObjectId>() : ids.Where(id => id != ObjectId.Null && !id.IsErased).ToList();
                _basePoint = basePoint;
                _currentPoint = basePoint;
                _displacement = new Vector3d(0.0, 0.0, 0.0);
                _message = string.IsNullOrWhiteSpace(message) ? "Pick position:" : message;
            }

            public Vector3d Displacement { get { return _displacement; } }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions("\n" + _message);
                options.BasePoint = _basePoint;
                options.UseBasePoint = true;
                options.UserInputControls =
                    UserInputControls.Accept3dCoordinates |
                    UserInputControls.GovernedByOrthoMode |
                    UserInputControls.GovernedByUCSDetect |
                    UserInputControls.NullResponseAccepted;

                PromptPointResult result = prompts.AcquirePoint(options);
                if (result.Status == PromptStatus.Cancel)
                    return SamplerStatus.Cancel;
                if (result.Status != PromptStatus.OK)
                    return SamplerStatus.NoChange;

                if (_currentPoint.DistanceTo(result.Value) <= 1e-9)
                    return SamplerStatus.NoChange;

                _currentPoint = result.Value;
                _displacement = _currentPoint - _basePoint;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                if (draw == null || draw.Geometry == null || _ids.Count == 0)
                    return true;

                Matrix3d transform = Matrix3d.Displacement(_displacement);
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in _ids)
                    {
                        if (id == ObjectId.Null || id.IsErased)
                            continue;

                        Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (ent == null || ent.IsErased)
                            continue;

                        Entity clone = ent.Clone() as Entity;
                        if (clone == null)
                            continue;

                        try
                        {
                            clone.TransformBy(transform);
                            draw.Geometry.Draw(clone);
                        }
                        finally
                        {
                            clone.Dispose();
                        }
                    }
                    tr.Commit();
                }

                return true;
            }
        }

        internal sealed class Point3dComparer : IEqualityComparer<Point3d>
        {
            private readonly double _eps;
            public Point3dComparer(double eps) { _eps = Math.Max(1e-12, eps); }

            public bool Equals(Point3d a, Point3d b)
            {
                return Math.Abs(a.X - b.X) <= _eps && Math.Abs(a.Y - b.Y) <= _eps && Math.Abs(a.Z - b.Z) <= _eps;
            }

            public int GetHashCode(Point3d p)
            {
                long x = (long)Math.Round(p.X / _eps);
                long y = (long)Math.Round(p.Y / _eps);
                long z = (long)Math.Round(p.Z / _eps);
                unchecked
                {
                    return ((x.GetHashCode() * 397) ^ y.GetHashCode()) * 397 ^ z.GetHashCode();
                }
            }
        }

        internal sealed class Point2dComparer : IEqualityComparer<Point2d>
        {
            private readonly double _eps;
            public Point2dComparer(double eps) { _eps = Math.Max(1e-12, eps); }

            public bool Equals(Point2d a, Point2d b)
            {
                return Math.Abs(a.X - b.X) <= _eps && Math.Abs(a.Y - b.Y) <= _eps;
            }

            public int GetHashCode(Point2d p)
            {
                long x = (long)Math.Round(p.X / _eps);
                long y = (long)Math.Round(p.Y / _eps);
                unchecked
                {
                    return (x.GetHashCode() * 397) ^ y.GetHashCode();
                }
            }
        }

        internal sealed class AxisGroup
        {
            public double Key { get; }
            public int Count { get; }
            public Point2d Representative { get; }

            public AxisGroup(double key, int count, Point2d representative)
            {
                Key = key;
                Count = count;
                Representative = representative;
            }
        }

        internal static IEnumerable<AxisGroup> GroupByAxisValue(List<Point2d> points, char axis, double tol)
        {
            // Group by rounded coordinate value on the given axis.
            // Example: X=60 shared by 3 holes => one group Count=3 and text override "3-<>".
            double eps = Math.Max(1e-12, tol);
            var dict = new Dictionary<long, List<Point2d>>();

            foreach (var p in points)
            {
                double v = (axis == 'Y') ? p.Y : p.X;
                long k = (long)Math.Round(v / eps);
                if (!dict.TryGetValue(k, out var list))
                {
                    list = new List<Point2d>();
                    dict[k] = list;
                }
                list.Add(p);
            }

            foreach (var kv in dict)
            {
                var list = kv.Value;
                if (list.Count == 0) continue;

                // Use the average as a stable sort key.
                double keyVal = (axis == 'Y') ? list.Average(pp => pp.Y) : list.Average(pp => pp.X);
                yield return new AxisGroup(keyVal, list.Count, list[0]);
            }
        }
    }
}
