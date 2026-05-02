using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DDimnotes
{

internal sealed class MarkPlacer
{
    private readonly List<Point3d> _markPts = new();
    private readonly double _markTextH;
    private readonly double _minDist;
    private readonly double _ringStep;

    public MarkPlacer(double markTextH)
    {
        _markTextH = markTextH;
        _minDist = NtConfig.MarkMinDistFactor * _markTextH;
        _ringStep = NtConfig.CircleRingStepFactor * _markTextH;
    }

    public void AddMarkPt(Point3d pt) => _markPts.Add(pt);

    private double MinDistToMarks(Point3d pt)
    {
        if (_markPts.Count == 0) return 1e99;
        double md = 1e99;
        foreach (var p in _markPts)
        {
            double d = GeoUtil.Dist(pt, p);
            if (d < md) md = d;
        }
        return md;
    }

    private bool PtOk(Point3d pt) => MinDistToMarks(pt) >= _minDist;

    private static double DegToRad(double deg) => Math.PI * deg / 180.0;

    public Point3d PickAround(Point3d basePt, double d0)
    {
        int tries = NtConfig.CircleMaxRotateTries * 6;
        Point3d last = GeoUtil.Polar(basePt, DegToRad(NtConfig.CircleStartAngleDeg), d0);

        for (int i = 0; i < tries; i++)
        {
            int ring = i / NtConfig.CircleMaxRotateTries;
            int idx = i - ring * NtConfig.CircleMaxRotateTries;
            double ang = NtConfig.CircleStartAngleDeg + idx * NtConfig.CircleAngleStepDeg;
            double d = d0 + ring * _ringStep;
            var pt = GeoUtil.Polar(basePt, DegToRad(ang), d);
            last = pt;
            if (PtOk(pt)) return pt;
        }
        return last;
    }

    public Point3d PickHoleMarkPoint(Point3d center, double rmin, double rmax, int k)
    {
        if (NtConfig.MarkPlacementMode == 1) return center;
        if (k < 1) k = 1;

        if (k >= 2)
        {
            if (rmin >= NtConfig.CircleCenterFactor * _markTextH && PtOk(center))
                return center;
        }
        else
        {
            if (rmax >= NtConfig.CircleCenterFactor * _markTextH && PtOk(center))
                return center;
        }

        double d0 = rmax + NtConfig.CircleOffsetMarginFactor * _markTextH;
        return PickAround(center, d0);
    }

    public Point3d PickCurverMarkPoint(DBObject locObjOrNull, Point3d? locPointOrNull)
    {
        if (NtConfig.MarkPlacementMode == 1)
        {
            if (locObjOrNull is Curve centerCurve) return GeoUtil.Center(centerCurve);
            if (locPointOrNull.HasValue) return locPointOrNull.Value;
            return Point3d.Origin;
        }

        if (locObjOrNull is Curve crv)
        {
            if (!crv.Closed)
            {
                var p0 = crv.StartPoint;
                var p1 = crv.EndPoint;
                double dStart = MinDistToMarks(p0);
                double dEnd = MinDistToMarks(p1);
                bool useStart = dStart >= dEnd;
                var endPt = useStart ? p0 : p1;

                double d0 = 1.6 * _markTextH;
                Vector3d dir;
                try
                {
                    dir = crv.GetFirstDerivative(endPt);
                    if (!useStart) dir = -dir;
                }
                catch
                {
                    dir = Vector3d.XAxis;
                }

                var u = dir.Length > 1e-12 ? dir.GetNormal() : Vector3d.XAxis;
                var cand = endPt + u * d0;
                if (PtOk(cand)) return cand;
                return PickAround(endPt, d0);
            }

            var c = GeoUtil.Center(crv);
            if (PtOk(c)) return c;
            return PickAround(c, 1.6 * _markTextH);
        }

        if (locPointOrNull.HasValue)
        {
            var pt = locPointOrNull.Value;
            if (PtOk(pt)) return pt;
            return PickAround(pt, 1.6 * _markTextH);
        }

        return Point3d.Origin;
    }
}
}
