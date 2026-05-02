using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace DDimnotes
{

internal sealed class HoleItem
{
    public string Code = "";
    public double Dia;
    public double Depth;
    public string TolStr = "";
    // Optional tail token (nth5 in grp=1 XData NAME), e.g. "..MatCat-3.0mm*1%%d"
    public string Tail1 = "";
    public string DynMethod = "";
    public ObjectId EntId;
    public string NoteData = "";
}

internal sealed class CenterBucket
{
    public string Key = "";
    public Point3d Center;
    public readonly List<HoleItem> Items = new();
}

internal sealed class SigGroup
{
    public string SigKey = "";
    public int Count;
    public List<HoleItem> SortedBundle = new();
    public readonly List<Point3d> Centers = new();
}

internal sealed class SymGroup
{
    public string Sym = "";
    public readonly List<SigGroup> Groups = new();
}

internal sealed class CurverGroup
{
    public string TailKey = "";
    public int Total;
    public readonly Dictionary<string, List<int>> SymToIdxs = new(System.StringComparer.OrdinalIgnoreCase);
}

internal sealed class PlateItem
{
    public string PlateName = "";
    public string ThkStr = "";
    public double Dx;
    public double Dy;
    public string MaterStr = "";
    public string DensityStr = "";
    public string HrcStr = "";
}
}
