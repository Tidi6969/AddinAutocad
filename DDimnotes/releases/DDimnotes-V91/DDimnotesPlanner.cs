using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.Geometry;
using DDimnotes.Utils;

namespace DDimnotes
{
    internal static class DDimnotesPlanner
    {
        public static DDimnotesPlan BuildPlan(
            ViewDir dir,
            ThicknessRec mainRec,
            double mainThk,
            List<ThicknessRec> thicknessRecs,
            List<HoleRec> holes,
            List<SideThreadRec> sideThreads,
            List<CurverRec> curvers,
            double gapFromMain = 0.0,
            string sideOutlineLayer = null,
            string sideViewLayer = null,
            string sideHoleLayer = null)
        {
            // base coordinate depends on view direction
            double baseX = 0.0, baseY = 0.0;

            bool thkAxisIsY = (dir == ViewDir.Deg90 || dir == ViewDir.Deg270);
            if (thkAxisIsY)
                baseY = (dir == ViewDir.Deg90) ? mainRec.Box.Ymax : mainRec.Box.Ymin;
            else
                baseX = (dir == ViewDir.Deg0) ? mainRec.Box.Xmax : mainRec.Box.Xmin;

            // Optional gap between the original MAIN boundary and the generated side view.
            // This shifts the *start face* of the thickness axis away from the MAIN bbox edge.
            // - 0/180: thickness axis is X => shift baseX
            // - 90/270: thickness axis is Y => shift baseY
            if (gapFromMain != 0.0)
            {
                if (thkAxisIsY)
                {
                    baseY = (dir == ViewDir.Deg90) ? (baseY + gapFromMain) : (baseY - gapFromMain);
                }
                else
                {
                    baseX = (dir == ViewDir.Deg0) ? (baseX + gapFromMain) : (baseX - gapFromMain);
                }
            }

            var rects = new List<RectSpec>();
            var sideDepthDimSegments = new List<SideDepthDimSegment>();
            var warnings = new List<string>();
            string outlineLayer = string.IsNullOrWhiteSpace(sideOutlineLayer) ? mainRec.Layer : sideOutlineLayer;
            string viewLayer = string.IsNullOrWhiteSpace(sideViewLayer) ? null : sideViewLayer;
            string holeLayer = string.IsNullOrWhiteSpace(sideHoleLayer) ? null : sideHoleLayer;

            // MAIN (Continuous)
            if (thkAxisIsY)
            {
                double y2 = GeomUtils.ThkCoord(baseY, mainThk, dir);
                rects.Add(new RectSpec(
                    mainRec.Box.Xmin, baseY,
                    mainRec.Box.Xmax, y2,
                    "Continuous",
                    outlineLayer,
                    mainRec.ColorIndex
                ));
            }
            else
            {
                double x2 = GeomUtils.ThkCoord(baseX, mainThk, dir);
                rects.Add(new RectSpec(
                    baseX, mainRec.Box.Ymin,
                    x2,   mainRec.Box.Ymax,
                    "Continuous",
                    outlineLayer,
                    mainRec.ColorIndex
                ));
            }

            // Other thickness (HIDDEN)
            foreach (var r in thicknessRecs)
            {
                if (r.Id == mainRec.Id) continue;

                if (thkAxisIsY)
                {
                    double y2 = GeomUtils.ThkCoord(baseY, r.Thickness, dir);
                    rects.Add(new RectSpec(
                        r.Box.Xmin, baseY,
                        r.Box.Xmax, y2,
                        "HIDDEN",
                        EffectiveLayer(viewLayer, r.Layer),
                        r.ColorIndex
                    ));
                }
                else
                {
                    double x2 = GeomUtils.ThkCoord(baseX, r.Thickness, dir);
                    rects.Add(new RectSpec(
                        baseX, r.Box.Ymin,
                        x2,   r.Box.Ymax,
                        "HIDDEN",
                        EffectiveLayer(viewLayer, r.Layer),
                        r.ColorIndex
                    ));
                }
            }

            // Curver (HIDDEN) - expanded by tolerance on plan axis
            foreach (var c in curvers)
            {
                double tol = Math.Abs(StringUtils.ParseFirstNumberOrZero(c.TolStr));

                // map depth -> [t0..t1] within [0..T]
                double t0, t1;
                if (c.Depth > 0.0)
                {
                    t0 = 0.0;
                    t1 = Math.Min(c.Depth, mainThk);
                }
                else if (c.Depth < 0.0)
                {
                    double d = Math.Min(Math.Abs(c.Depth), mainThk);
                    t0 = mainThk - d;
                    t1 = mainThk;
                }
                else
                {
                    t0 = 0.0;
                    t1 = mainThk;
                }

                if (thkAxisIsY)
                {
                    double x1 = c.Box.Xmin - tol;
                    double x2 = c.Box.Xmax + tol;
                    double y1 = GeomUtils.ThkCoord(baseY, t0, dir);
                    double y2 = GeomUtils.ThkCoord(baseY, t1, dir);

                    rects.Add(new RectSpec(x1, y1, x2, y2, "HIDDEN", EffectiveLayer(viewLayer, c.Layer), c.ColorIndex));
                }
                else
                {
                    double y1 = c.Box.Ymin - tol;
                    double y2 = c.Box.Ymax + tol;
                    double x1 = GeomUtils.ThkCoord(baseX, t0, dir);
                    double x2 = GeomUtils.ThkCoord(baseX, t1, dir);

                    rects.Add(new RectSpec(x1, y1, x2, y2, "HIDDEN", EffectiveLayer(viewLayer, c.Layer), c.ColorIndex));
                }
            }

            // Holes (HIDDEN)
            rects.AddRange(BuildHoleRects(dir, baseX, baseY, mainThk, holes, sideDepthDimSegments, holeLayer));

            // Side-face metric threads whose source entity is a LINE centerline.
            // They are not front-view holes, so they do not use XData depth and are not
            // grouped with the normal Circle-based hole/thread logic.
            rects.AddRange(BuildSideThreadRects(dir, baseX, baseY, mainRec.Box, mainThk, sideThreads, warnings, viewLayer));

            // Anchor & move axis
            // We anchor to main lower-left corner in the non-move axis to keep the insertion point near geometry.
            Point2d anchor;
            bool moveAlongX;

            if (thkAxisIsY)
            {
                // move along Y only (like LISP 90/270)
                anchor = new Point2d(mainRec.Box.Xmin, baseY);
                moveAlongX = false;
            }
            else
            {
                // move along X only (like LISP 0/180)
                anchor = new Point2d(baseX, mainRec.Box.Ymin);
                moveAlongX = true;
            }

            return new DDimnotesPlan(dir, mainThk, mainRec.Box, rects, sideDepthDimSegments, anchor, moveAlongX, warnings);
        }

        private sealed class SideThreadProjection
        {
            public double PlanCoord;
        }

        private static IEnumerable<RectSpec> BuildSideThreadRects(
            ViewDir dir,
            double baseX,
            double baseY,
            BBox2d mainBox,
            double mainThk,
            List<SideThreadRec> sideThreads,
            List<string> warnings,
            string layerOverride)
        {
            if (sideThreads == null || sideThreads.Count == 0) yield break;

            bool thkAxisIsY = (dir == ViewDir.Deg90 || dir == ViewDir.Deg270);

            foreach (SideThreadRec st in sideThreads)
            {
                if (st == null) continue;

                SideThreadProjection proj;
                if (!TryProjectSideThreadCenterline(dir, mainBox, mainThk, st, out proj))
                    continue;

                ThreadInfo thread;
                if (!ThreadTable.TryGetMetricThread(MakeHoleProxy(st, proj.PlanCoord), out thread))
                {
                    string size = string.IsNullOrWhiteSpace(st.MetricThreadSizeKey)
                        ? ("M" + st.Dia.ToString("0.###", CultureInfo.InvariantCulture))
                        : st.MetricThreadSizeKey;

                    if (warnings != null)
                    {
                        warnings.Add(
                            "DDimnotes: Không tìm thấy pitch cho ren " + size +
                            " trong ti.dat. Chương trình tạm dùng pitch = 1 để vẽ tiếp. Vui lòng bổ sung dữ liệu vào ti.dat.");
                    }

                    thread = new ThreadInfo(size, 1.0, Math.Max(st.Dia, 0.01));
                }

                foreach (var rr in SideThreadFeatureEntities(thkAxisIsY, dir, baseX, baseY, mainThk, st, thread, proj.PlanCoord, layerOverride))
                    yield return rr;
            }
        }

        private static HoleRec MakeHoleProxy(SideThreadRec st, double planCoord)
        {
            // Proxy is used only for ThreadTable lookup. Geometry is generated from SideThreadRec.
            return new HoleRec(
                st.Id,
                new Point2d(0.0, planCoord),
                st.Code,
                st.Dia,
                1.0,
                st.TolStr,
                st.Layer,
                st.ColorIndex,
                st.MetricThreadSizeKey);
        }

        private static bool TryProjectSideThreadCenterline(
            ViewDir dir,
            BBox2d mainBox,
            double mainThk,
            SideThreadRec st,
            out SideThreadProjection projection)
        {
            projection = null;

            double dx = st.End.X - st.Start.X;
            double dy = st.End.Y - st.Start.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= 1e-9) return false;

            const double angleToleranceDeg = 5.0;
            double angleTolRatio = Math.Tan(angleToleranceDeg * Math.PI / 180.0);

            bool thkAxisIsY = (dir == ViewDir.Deg90 || dir == ViewDir.Deg270);
            double planCoord;

            if (thkAxisIsY)
            {
                // View 90/270 and source LINE 90/270 belong to the same vertical group.
                if (Math.Abs(dx) > Math.Abs(dy) * angleTolRatio) return false;

                planCoord = (st.Start.X + st.End.X) / 2.0;
                if (!IsInsidePlanRange(planCoord, mainBox.Xmin, mainBox.Xmax)) return false;
            }
            else
            {
                // View 0/180 and source LINE 0/180 belong to the same horizontal group.
                if (Math.Abs(dy) > Math.Abs(dx) * angleTolRatio) return false;

                planCoord = (st.Start.Y + st.End.Y) / 2.0;
                if (!IsInsidePlanRange(planCoord, mainBox.Ymin, mainBox.Ymax)) return false;
            }

            projection = new SideThreadProjection
            {
                PlanCoord = planCoord
            };
            return true;
        }

        private static bool IsInsidePlanRange(double value, double min, double max)
        {
            const double tol = 1e-6;
            return value >= min - tol && value <= max + tol;
        }

        private static double ClampThreadDepth(double value, double mainThk)
        {
            if (value < 0.0) return 0.0;
            if (value > mainThk) return mainThk;
            return value;
        }

        private static IEnumerable<RectSpec> SideThreadFeatureEntities(
            bool thkAxisIsY,
            ViewDir dir,
            double baseX,
            double baseY,
            double mainThk,
            SideThreadRec st,
            ThreadInfo thread,
            double planCoord,
            string layerOverride)
        {
            // V54: source LINE + metric thread XData is an end-view thread symbol.
            // It is not a front-view hole segment and does not use XData depth.
            double nominalDia = Math.Max(Math.Max(thread.NominalDia, st.Dia), 0.01);
            double pitch = thread.Pitch > 0.0 ? thread.Pitch : 1.0;
            double innerDia = Math.Max(0.01, nominalDia - pitch);

            double cx;
            double cy;
            if (thkAxisIsY)
            {
                cx = planCoord;
                cy = GeomUtils.ThkCoord(baseY, mainThk / 2.0, dir);
            }
            else
            {
                cx = GeomUtils.ThkCoord(baseX, mainThk / 2.0, dir);
                cy = planCoord;
            }

            Point2d center = new Point2d(cx, cy);
            double innerRadius = innerDia / 2.0;
            double outerRadius = nominalDia / 2.0;
            double centerLineHalfLen = nominalDia * 0.6; // total centerline length = 1.2 * D

            string sideThreadLayer = EffectiveLayer(layerOverride, st.Layer);
            yield return RectSpec.Circle(center, innerRadius, "Continuous", sideThreadLayer, st.ColorIndex);

            double gapStartDeg;
            double gapEndDeg;
            GetThreadArcGapQuadrant(dir, out gapStartDeg, out gapEndDeg);
            yield return RectSpec.Arc(center, outerRadius, gapEndDeg, gapStartDeg, "Continuous", sideThreadLayer, st.ColorIndex);

            yield return RectSpec.Line(
                new Point2d(cx - centerLineHalfLen, cy),
                new Point2d(cx + centerLineHalfLen, cy),
                "CENTER",
                sideThreadLayer,
                st.ColorIndex);

            yield return RectSpec.Line(
                new Point2d(cx, cy - centerLineHalfLen),
                new Point2d(cx, cy + centerLineHalfLen),
                "CENTER",
                sideThreadLayer,
                st.ColorIndex);
        }

        private static void GetThreadArcGapQuadrant(ViewDir dir, out double gapStartDeg, out double gapEndDeg)
        {
            switch (dir)
            {
                case ViewDir.Deg90:
                    gapStartDeg = 90.0;
                    gapEndDeg = 180.0;
                    break;
                case ViewDir.Deg180:
                    gapStartDeg = 180.0;
                    gapEndDeg = 270.0;
                    break;
                case ViewDir.Deg270:
                    gapStartDeg = 270.0;
                    gapEndDeg = 0.0;
                    break;
                case ViewDir.Deg0:
                default:
                    gapStartDeg = 0.0;
                    gapEndDeg = 90.0;
                    break;
            }
        }

        private static IEnumerable<RectSpec> BuildHoleRects(ViewDir dir, double baseX, double baseY, double mainThk, List<HoleRec> holes, List<SideDepthDimSegment> sideDepthDimSegments, string layerOverride)
        {
            const double centerTol = 1.0;

            foreach (List<HoleRec> group in BuildHoleStepGroups(holes, centerTol))
            {
                foreach (var r in BuildHoleGroupRects(dir, baseX, baseY, mainThk, group, sideDepthDimSegments, layerOverride))
                    yield return r;
            }
        }

        private static List<List<HoleRec>> BuildHoleStepGroups(List<HoleRec> holes, double centerTol)
        {
            var byCenter = new Dictionary<string, List<HoleRec>>(StringComparer.OrdinalIgnoreCase);
            foreach (HoleRec h in holes)
            {
                string key = GeomUtils.CenterKey(h.Center, centerTol);
                List<HoleRec> list;
                if (!byCenter.TryGetValue(key, out list))
                {
                    list = new List<HoleRec>();
                    byCenter[key] = list;
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

            double da = HoleDiaEff(a);
            double db = HoleDiaEff(b);

            // Same center alone is not enough to create one stepped-hole group.
            // Per the original data rule, the smaller same-center hole must have '+' in tok1/code.
            // Therefore M_SCREWHO + GENPIN without '+' are drawn as independent features.
            if (Math.Abs(da - db) <= 1e-9)
                return HasPlusCode(a) || HasPlusCode(b);

            HoleRec smaller = da < db ? a : b;
            return HasPlusCode(smaller);
        }

        private static bool HasPlusCode(HoleRec h)
        {
            return h != null && !string.IsNullOrEmpty(h.Code) && h.Code.IndexOf('+') >= 0;
        }

        private static IEnumerable<RectSpec> BuildHoleGroupRects(ViewDir dir, double baseX, double baseY, double mainThk, List<HoleRec> items, List<SideDepthDimSegment> sideDepthDimSegments, string layerOverride)
        {
            bool thkAxisIsY = (dir == ViewDir.Deg90 || dir == ViewDir.Deg270);

            // set_screw is not a normal blind thread: its core/tap drill is always a through hole,
            // while the nominal screw diameter only follows the supplied depth.
            foreach (var h in items.Where(x => ThreadTable.IsSetScrewCode(x.Code)))
            {
                foreach (var rr in SetScrewFeatureRects(thkAxisIsY, dir, baseX, baseY, h, mainThk, sideDepthDimSegments, layerOverride))
                    yield return rr;
            }

            items = items.Where(h => !ThreadTable.IsSetScrewCode(h.Code)).ToList();
            if (items.Count == 0) yield break;

            var front = items.Where(h => h.Depth > 0.0).ToList();
            var back  = items.Where(h => h.Depth < 0.0).ToList();
            var thru  = items.Where(h => h.Depth == 0.0).ToList();

            // sort by diaEff desc
            front.Sort((a, b) => HoleDiaEff(b).CompareTo(HoleDiaEff(a)));
            back.Sort((a, b)  => HoleDiaEff(b).CompareTo(HoleDiaEff(a)));
            thru.Sort((a, b)  => HoleDiaEff(b).CompareTo(HoleDiaEff(a)));

            // frontMax / backMax
            double frontMax = 0.0;
            foreach (var h in front)
                frontMax = Math.Max(frontMax, Math.Min(h.Depth, mainThk));

            double backMax = 0.0;
            foreach (var h in back)
                backMax = Math.Max(backMax, Math.Min(Math.Abs(h.Depth), mainThk));

            // FRONT: each depth hole starts at the side-view opening and ends at its depth bottom.
            foreach (var h in front)
            {
                double tEdge = 0.0;
                double tInner = Math.Min(h.Depth, mainThk);
                if (tInner <= tEdge) continue;

                foreach (var rr in HoleFeatureRects(thkAxisIsY, dir, baseX, baseY, h, tEdge, tInner, mainThk, isThreadDepthSegment: true, layerOverride: layerOverride))
                    yield return rr;

                AddSideDepthDimSegment(sideDepthDimSegments, thkAxisIsY, dir, baseX, baseY, h, tInner, tEdge);
            }

            // BACK: each depth hole starts at the far side-view opening and ends at its depth bottom.
            foreach (var h in back)
            {
                double depthAbs = Math.Min(Math.Abs(h.Depth), mainThk);
                if (depthAbs <= 0.0) continue;

                double tInner = mainThk - depthAbs;
                double tEdge = mainThk;
                if (tEdge <= tInner) continue;

                foreach (var rr in HoleFeatureRects(thkAxisIsY, dir, baseX, baseY, h, tInner, tEdge, mainThk, isThreadDepthSegment: true, layerOverride: layerOverride))
                    yield return rr;

                AddSideDepthDimSegment(sideDepthDimSegments, thkAxisIsY, dir, baseX, baseY, h, tInner, tEdge);
            }

            // THRU between bottom of front and bottom of back
            if (thru.Count > 0)
            {
                double tA = frontMax;
                double tB = mainThk - backMax;
                if (tB < tA) tB = tA;

                foreach (var h in thru)
                {
                    foreach (var rr in HoleFeatureRects(thkAxisIsY, dir, baseX, baseY, h, tA, tB, mainThk, isThreadDepthSegment: false, layerOverride: layerOverride))
                        yield return rr;
                }
            }
        }

        private static void AddSideDepthDimSegment(
            List<SideDepthDimSegment> sideDepthDimSegments,
            bool thkAxisIsY,
            ViewDir dir,
            double baseX,
            double baseY,
            HoleRec h,
            double tInner,
            double tEdge)
        {
            if (sideDepthDimSegments == null || h == null) return;

            Point2d inner;
            Point2d edge;
            if (thkAxisIsY)
            {
                inner = new Point2d(h.Center.X, GeomUtils.ThkCoord(baseY, tInner, dir));
                edge = new Point2d(h.Center.X, GeomUtils.ThkCoord(baseY, tEdge, dir));
            }
            else
            {
                inner = new Point2d(GeomUtils.ThkCoord(baseX, tInner, dir), h.Center.Y);
                edge = new Point2d(GeomUtils.ThkCoord(baseX, tEdge, dir), h.Center.Y);
            }

            sideDepthDimSegments.Add(new SideDepthDimSegment(h, inner, edge));
        }


        private static IEnumerable<RectSpec> SetScrewFeatureRects(
            bool thkAxisIsY, ViewDir dir, double baseX, double baseY,
            HoleRec h,
            double mainThk,
            List<SideDepthDimSegment> sideDepthDimSegments,
            string layerOverride)
        {
            ThreadInfo thread;
            if (!ThreadTable.TryGetSetScrewThread(h, out thread))
            {
                // If ti.dat/MSW cannot be read, keep a safe fallback instead of dropping geometry.
                double fallbackStart = h.Depth < 0.0 ? Math.Max(0.0, mainThk - Math.Abs(h.Depth)) : 0.0;
                double fallbackEnd = h.Depth < 0.0 ? mainThk : Math.Min(Math.Abs(h.Depth), mainThk);
                if (Math.Abs(h.Depth) > 1e-9 && Math.Abs(fallbackEnd - fallbackStart) > 1e-9)
                {
                    double fallbackInner = h.Depth < 0.0 ? fallbackStart : fallbackEnd;
                    double fallbackEdge = h.Depth < 0.0 ? fallbackEnd : fallbackStart;
                    AddSideDepthDimSegment(sideDepthDimSegments, thkAxisIsY, dir, baseX, baseY, h, fallbackInner, fallbackEdge);
                }
                foreach (var rr in HoleFeatureRects(thkAxisIsY, dir, baseX, baseY, h, fallbackStart, fallbackEnd, mainThk, isThreadDepthSegment: false, layerOverride: layerOverride))
                    yield return rr;
                yield break;
            }

            double tStart;
            double tEnd;
            if (h.Depth < 0.0)
            {
                double d = Math.Min(Math.Abs(h.Depth), mainThk);
                tStart = mainThk - d;
                tEnd = mainThk;
            }
            else if (h.Depth > 0.0)
            {
                tStart = 0.0;
                tEnd = Math.Min(h.Depth, mainThk);
            }
            else
            {
                tStart = 0.0;
                tEnd = mainThk;
            }

            if (Math.Abs(h.Depth) > 1e-9 && Math.Abs(tEnd - tStart) > 1e-9)
            {
                double tInner = h.Depth < 0.0 ? tStart : tEnd;
                double tEdge = h.Depth < 0.0 ? tEnd : tStart;
                AddSideDepthDimSegment(sideDepthDimSegments, thkAxisIsY, dir, baseX, baseY, h, tInner, tEdge);
            }

            // Correct set_screw geometry:
            // - keep the large/nominal m_screw-style contour by depth;
            // - keep the drill/core hole through the whole plate;
            // - remove only the small/root m_screw polyline that creates the sharp small-hole end.
            foreach (var rr in SetScrewOuterThreadPolylines(thkAxisIsY, dir, baseX, baseY, h, thread, tStart, tEnd, layerOverride))
                yield return rr;

            double coreHalfW = Math.Max(0.0, thread.CoreDia) / 2.0;
            if (coreHalfW > 0.0)
            {
                foreach (var rr in HoleSegRects(thkAxisIsY, dir, baseX, baseY, h.Center, coreHalfW, 0.0, mainThk, EffectiveLayer(layerOverride, h.Layer), h.ColorIndex))
                    yield return rr;
            }
        }

        private static IEnumerable<RectSpec> HoleFeatureRects(
            bool thkAxisIsY, ViewDir dir, double baseX, double baseY,
            HoleRec h,
            double tStart, double tEnd,
            double mainThk,
            bool isThreadDepthSegment,
            string layerOverride)
        {
            ThreadInfo thread;
            if (isThreadDepthSegment && ThreadTable.TryGetMetricThread(h, out thread))
            {
                // Metric internal thread convention, based on DScrew/Ti.dat and the referenced LISP:
                // - M_SCREW token[0] = size (M8, M10, ...)
                // - token[1] = pitch
                // - token[3] = nominal thread diameter
                // Side view uses two thread polylines and a small end peak/taper, instead of a plain box.
                foreach (var rr in ThreadFeaturePolylines(thkAxisIsY, dir, baseX, baseY, h, thread, tStart, tEnd, mainThk, layerOverride))
                    yield return rr;

                yield break;
            }

            double halfW = HoleDiaEff(h) / 2.0;
            foreach (var rr in HoleSegRects(thkAxisIsY, dir, baseX, baseY, h.Center, halfW, tStart, tEnd, EffectiveLayer(layerOverride, h.Layer), h.ColorIndex))
                yield return rr;
        }

        private static IEnumerable<RectSpec> SetScrewOuterThreadPolylines(
            bool thkAxisIsY, ViewDir dir, double baseX, double baseY,
            HoleRec h,
            ThreadInfo thread,
            double tStart,
            double tEnd,
            string layerOverride)
        {
            double nominalDia = Math.Max(thread.NominalDia, h.Dia);
            double bodyRadiusOffset = nominalDia / 2.0 + 0.002;
            double pitchRadius = thread.Pitch / 2.0;
            double pitchRadiusOffset = Math.Max(0.0, nominalDia / 2.0 - (pitchRadius - 0.002));

            double a = Math.Min(tStart, tEnd);
            double b = Math.Max(tStart, tEnd);
            if (b <= a) yield break;

            double axisSign = h.Depth < 0.0 ? -1.0 : 1.0;
            double t0 = h.Depth < 0.0 ? b : a;
            double t1 = h.Depth < 0.0 ? a : b;
            double tPitchEnd = t1 + axisSign * pitchRadius;

            Func<double, double, Point2d> pt = (t, off) => ThreadPoint(thkAxisIsY, dir, baseX, baseY, h.Center, t, off);

            var outer = new List<Point2d>
            {
                pt(t0, -bodyRadiusOffset),
                pt(t1, -bodyRadiusOffset),
                pt(tPitchEnd, -pitchRadiusOffset),
                pt(tPitchEnd,  pitchRadiusOffset),
                pt(t1,  bodyRadiusOffset),
                pt(t1, -bodyRadiusOffset),
                pt(t1,  bodyRadiusOffset),
                pt(t0,  bodyRadiusOffset)
            };

            yield return RectSpec.Polyline(outer, false, "HIDDEN", EffectiveLayer(layerOverride, h.Layer), h.ColorIndex);
        }

        private static IEnumerable<RectSpec> ThreadFeaturePolylines(
            bool thkAxisIsY, ViewDir dir, double baseX, double baseY,
            HoleRec h,
            ThreadInfo thread,
            double tStart,
            double tEnd,
            double mainThk,
            string layerOverride)
        {
            double nominalDia = Math.Max(thread.NominalDia, h.Dia);
            double bodyRadiusOffset = nominalDia / 2.0 + 0.002;
            double pitchRadius = thread.Pitch / 2.0;
            double pitchRadiusOffset = Math.Max(0.0, nominalDia / 2.0 - (pitchRadius - 0.002));

            double a = Math.Min(tStart, tEnd);
            double b = Math.Max(tStart, tEnd);
            if (b <= a) yield break;

            // Front thread runs from near face to larger t; back thread runs from far face to smaller t.
            double axisSign = h.Depth < 0.0 ? -1.0 : 1.0;
            double t0 = h.Depth < 0.0 ? b : a;
            double t1 = h.Depth < 0.0 ? a : b;

            double tPitchStart = t0 + axisSign * pitchRadius;
            double rawTPitchEnd = t1 + axisSign * pitchRadius;
            double tExtend1 = t1 + axisSign * (0.5 * nominalDia);
            double tExtend2 = t1 + axisSign * (0.7 * nominalDia);

            const double eps = 1e-9;
            double depthAbs = Math.Abs(h.Depth);
            bool depthAtOrBeyondPlate = mainThk > 0.0 && depthAbs >= mainThk - eps;

            // V58: if the thread depth reaches or passes the plate thickness,
            // treat it as D = T. The large/outer thread end taper must not
            // create any point outside the plate thickness.
            double tPitchEnd = depthAtOrBeyondPlate ? t1 : rawTPitchEnd;

            Func<double, double, Point2d> pt = (t, off) => ThreadPoint(thkAxisIsY, dir, baseX, baseY, h.Center, t, off);

            // This follows the key point order of screw$thr$draw:
            // outer/body polyline creates the end peak from body radius into pitch radius;
            // pitch/root polyline adds the root lines and the long small peak at the thread end.
            var outer = new List<Point2d>
            {
                pt(t0, -bodyRadiusOffset),
                pt(t1, -bodyRadiusOffset),
                pt(tPitchEnd, -pitchRadiusOffset),
                pt(tPitchEnd,  pitchRadiusOffset),
                pt(t1,  bodyRadiusOffset),
                pt(t1, -bodyRadiusOffset),
                pt(t1,  bodyRadiusOffset),
                pt(t0,  bodyRadiusOffset)
            };

            double normalTotalLength = Math.Abs(tExtend2 - t0);
            bool smallHoleThroughNoPeak = ShouldDrawMetricThreadSmallHoleThrough(h, mainThk, normalTotalLength);

            List<Point2d> root;
            if (smallHoleThroughNoPeak)
            {
                // V58 normal metric thread correction:
                // When L reaches or exceeds plate thickness, or depth passes the plate
                // and is treated as D = T, replace only the small/root end peak
                // by a through core hole. This is intentionally not the set_screw/MSW branch.
                double tThrough = axisSign > 0.0 ? mainThk : 0.0;
                root = new List<Point2d>
                {
                    pt(tPitchStart, -pitchRadiusOffset),
                    pt(tThrough, -pitchRadiusOffset),
                    pt(tThrough,  pitchRadiusOffset),
                    pt(tPitchStart,  pitchRadiusOffset),
                    pt(t0,  bodyRadiusOffset),
                    pt(t0, -bodyRadiusOffset),
                    pt(tPitchStart, -pitchRadiusOffset),
                    pt(tPitchStart,  pitchRadiusOffset)
                };
            }
            else
            {
                root = new List<Point2d>
                {
                    pt(tPitchStart, -pitchRadiusOffset),
                    pt(tExtend1, -pitchRadiusOffset),
                    pt(tExtend1,  pitchRadiusOffset),
                    pt(tExtend1, -pitchRadiusOffset),
                    pt(tExtend2, 0.0),
                    pt(tExtend1,  pitchRadiusOffset),
                    pt(tPitchStart,  pitchRadiusOffset),
                    pt(t0,  bodyRadiusOffset),
                    pt(t0, -bodyRadiusOffset),
                    pt(tPitchStart, -pitchRadiusOffset),
                    pt(tPitchStart,  pitchRadiusOffset)
                };
            }

            yield return RectSpec.Polyline(outer, false, "HIDDEN", EffectiveLayer(layerOverride, h.Layer), h.ColorIndex);
            yield return RectSpec.Polyline(root, false, "HIDDEN", EffectiveLayer(layerOverride, h.Layer), h.ColorIndex);
        }

        private static bool ShouldDrawMetricThreadSmallHoleThrough(HoleRec h, double mainThk, double normalTotalLength)
        {
            if (h == null) return false;
            if (mainThk <= 0.0) return false;

            // User requirement V58:
            // T = plate thickness; D = hole/thread depth from XData;
            // L = normal total drawn length from thread start to farthest drawn end.
            // L < T draws normally. L >= T enters the through small-hole branch.
            // D > T is treated as D = T, so it also uses this boundary branch.
            const double eps = 1e-9;
            return normalTotalLength >= mainThk - eps;
        }

        private static Point2d ThreadPoint(
            bool thkAxisIsY, ViewDir dir, double baseX, double baseY,
            Point2d center, double t, double offset)
        {
            if (thkAxisIsY)
            {
                double y = GeomUtils.ThkCoord(baseY, t, dir);
                return new Point2d(center.X + offset, y);
            }
            else
            {
                double x = GeomUtils.ThkCoord(baseX, t, dir);
                return new Point2d(x, center.Y + offset);
            }
        }

        private static string EffectiveLayer(string layerOverride, string sourceLayer)
        {
            return string.IsNullOrWhiteSpace(layerOverride) ? sourceLayer : layerOverride;
        }

        private static IEnumerable<RectSpec> HoleSegRects(
            bool thkAxisIsY, ViewDir dir, double baseX, double baseY,
            Point2d center, double halfW,
            double tStart, double tEnd,
            string layer, short? colorIndex)
        {
            double a = Math.Min(tStart, tEnd);
            double b = Math.Max(tStart, tEnd);
            if (b <= a) yield break;

            if (thkAxisIsY)
            {
                // thickness axis = Y ; plan axis = X
                double x1 = center.X - halfW;
                double x2 = center.X + halfW;
                double y1 = GeomUtils.ThkCoord(baseY, a, dir);
                double y2 = GeomUtils.ThkCoord(baseY, b, dir);

                yield return new RectSpec(x1, y1, x2, y2, "HIDDEN", layer, colorIndex);
            }
            else
            {
                // thickness axis = X ; plan axis = Y
                double y1 = center.Y - halfW;
                double y2 = center.Y + halfW;
                double x1 = GeomUtils.ThkCoord(baseX, a, dir);
                double x2 = GeomUtils.ThkCoord(baseX, b, dir);

                yield return new RectSpec(x1, y1, x2, y2, "HIDDEN", layer, colorIndex);
            }
        }

        private static bool IsPosOrGenr(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            return code.StartsWith("POS", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("GENR", StringComparison.OrdinalIgnoreCase);
        }

        private static double HoleDiaEff(HoleRec h)
        {
            double tol = Math.Abs(StringUtils.ParseFirstNumberOrZero(h.TolStr));
            if (IsPosOrGenr(h.Code))
                return h.Dia + 2.0 * tol;
            return h.Dia;
        }
    }
}
