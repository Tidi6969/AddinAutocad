using System;
using System.Collections.Generic;

namespace DDimnotes
{

internal static class NtConfig
{
    // === NT CONFIG: MARK PLACEMENT (match LISP defaults) ===
    public static int MarkPlacementMode = 0;
    public static double CircleCenterFactor = 1.4;
    public static double CircleOffsetMarginFactor = 0.6;
    public static double CircleStartAngleDeg = 45.0;
    public static double CircleAngleStepDeg = 45.0;
    public static int CircleMaxRotateTries = 8;
    public static double CircleRingStepFactor = 0.8;
    public static double MarkMinDistFactor = 1.5;

    // Text/Line setup
    public static double TextFactor = 0.8;
    public static short TextColorIndex = 2;
    public static short LineColorIndex = 3;

    // Option: include WI wire-hole
    public static int NoteWireHole = 0;

    // Base config
    public static string BaseDepthPrefix = "Dp";
    public static string BaseUnit = "mm";
    public static string BaseViewFront = "Front";
    public static string BaseViewBack = "Back";
    public static string BaseThicknessLabel = "thickness";

    // Plate config
    public static string BaseKgUnit = "kg";
    public static string BaseHrcLabel = "HRC";
    public static string BaseUnknown = "unknown";
    public static string BaseUnknownKey = "unknown1";
    public static string BaseUnknown2 = "unknown 2";
    public static string BaseUnknown3 = "unknown 3";

    // Maps (ported from LISP defaults)
    // holeprocess-map: (key, processName, processSymbol)
    public static readonly List<(string Key, string Name, string Sym)> HoleProcessMap = new()
    {
        ("gen", "khoan", "%%c"),
        ("gen0", "khoan", "%%c"),
        ("gen1", "khoan", "%%c"),
        ("gen2", "khoan", "%%c"),
        ("genr", "doa", "%%c"),
        ("pos", "wirecut", "%%c"),
        ("f_sink", "Flat_Sink", "%%c"),
        ("g_sink", "gsink", "%%c"),
        ("s_sink", "ssink", "%%c"),
        ("m_screw", "thread", "M"),
        ("set_screw", "Set_screw", "M"),
        ("fm_screw", "Fm_crew", "M"),
    };

    // layermap for Header-A (DNOTES1): layer -> prefix
    public static readonly Dictionary<string, string> HeaderLayerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["handle_p"] = "handle",
        ["u1_p"] = "u1",
        ["u2_p"] = "u2",
        ["cover_p"] = "cover",
        ["up_p"] = "up",
        ["ub_p"] = "ub",
        ["ub0_p"] = "ub0",
        ["ub2_p"] = "ub2",
        ["up_lth_p"] = "up_lth",
        ["ph_p"] = "ph",
        ["ph2_p"] = "ph2",
        ["ps_p"] = "ps",
        ["ps2_p"] = "ps2",
        ["pps_p"] = "pps",
        ["pps2_p"] = "pps2",
        ["die_p"] = "die",
        ["die2_p"] = "die2",
        ["lb_p"] = "lb",
        ["lb2_p"] = "lb2",
        ["lb3_p"] = "lb3",
        ["dn_lth_p"] = "dn_lth",
        ["lp_p"] = "lp",
        ["b1_p"] = "b1",
        ["b2_p"] = "b2",
        ["guide_p"] = "guide",
        ["side_p"] = "side",
        ["eject_p"] = "eject",
        ["eject2_p"] = "eject2",
        ["PUNCH"] = "PU",
    };


    // holetag-map: (tagKey, sym, desc)
    public static readonly List<(string Tag, string Sym, string Desc)> HoleTagMap = new()
    {
        ("AP", "A", "A-type punch"),
        ("CAP", "C", "Special-shaped A punch"),
        ("EjAP", "E", "A-type letter punch"),
        ("EjBP", "F", "B-type letter punch"),
        ("BP", "B", "B-type punch"),
        ("CPP", "G", "Stripper-plate guide punch"),
        ("IGPB", "K", "Inner guide post"),
        ("IGPB2", "K", "Inner guide post (center-post type)"),
        ("OGPB", "O", "Outer guide post (non-ball)"),
        ("OGPB2", "O", "Outer guide post (ball-bearing type)"),
        ("Nbl", "WD", "Outer guide bushing (non-ball)"),
        ("Ndo", "GG", "Outer guide bushing (ball-bearing type)"),
        ("TSCR", "M", "Upper die screw"),
        ("FlatSCR", "M", "Flat head screw"),
        ("BSCR", "M", "Lower die screw"),
        ("GSCR", "M", "Guide plate screw"),
        ("FSCR", "PM", "Hex socket flat head screw"),
        ("ORG", "U", "Datum hole"),
        ("POT", "TD", "Dimple/emboss punch point"),
        ("SALA", "SALA", "Countersink"),
        ("LIMITER", "LIM", "Inner stop/limiter post"),
        ("TAP", "J", "Tapping punch (A-type)"),
        ("PTAP", "J", "Tapping punch (guided type / pilot tap)"),
        ("TTAP", "J", "Tapping punch (T/B type)"),
        ("SAFE", "L", "Safety pin"),
        ("PPIN", "P", "Locating pin (T-type, lifter-guided)"),
        ("PPIN2SR", "P", "Locating pin (cross type, forming internal locating)"),
        ("PPIN2", "P", "Locating pin (cross type, forming internal locating)"),
        ("PPIN3", "P", "Locating pin (inner locating pin)"),
        ("PPIN4", "P", "Locating pin (pointed tip)"),
        ("PPIN5", "P", "Locating pin (round tip)"),
        ("PPIN6", "P", "Locating pin (flat tip)"),
        ("CSR", "Q", "Equal-height sleeve / spacer sleeve"),
        ("GLS", "R", "Lifter dual-purpose pin"),
        ("LPS", "S", "Stock stop pin (T-type)"),
        ("LPS2", "S", "Stock stop pin (cross type)"),
        ("LPS3", "S", "Stock stop pin (C-type)"),
        ("LPS4", "S", "Stock stop pin (D-type)"),
        ("AirSPR", "DQ", "Nitrogen gas spring"),
        ("BSPR", "M", "Set screw blind threaded hole"),
        ("ULi", "ULI", "Urethane (rubber)"),
        ("SPR_SWF", "T", "Spring"),
        ("SPR_SWL", "T", "Spring"),
        ("SPR_SWM", "T", "Spring"),
        ("SPR_SWH", "T", "Spring"),
        ("SPR_SWB", "T", "Spring"),
        ("SPR", "T", "Spring (flat-wire, coiled)"),
        ("BLS", "UD", "Stock lifter block / ejector block"),
        ("BLS2", "UD", "Stock lifter block / ejector block"),
        ("PBLK", "EN", "Outer locator block"),
        ("SIDE", "SIDE", "Pitch locating (side cutter)"),
        ("GUI", "GUI", "Guide plate / strip guide plate"),
        ("TROD", "TT", "Knock rod (dowel pin + eject rod)"),
        ("EjCSR", "J", "Equal-height sleeve (ejector pin selected inside sleeve)"),
        ("PIN", "V", "Dowel pin"),
        ("PINScr", "m", "Threaded dowel pin"),
        ("LLP", "v", "Locating pin"),
        ("MA", "MA", "Lifter guide pin"),
        ("ps_MA", "GB", "Guide bushing"),
        ("AIR", "AIR", "Air blow pin"),
        ("APP", "A", "A-type guide punch"),
        ("BPP", "A", "B-type guide punch"),
        ("P_BUSH1", "G", "DA die bushing"),
        ("P_BUSH2", "H", "DH stripper-plate bushing"),
        ("G_HBUSH", "E", "GB auxiliary guide bushing"),
        ("BALL_BUSH", "BUSH", "GB ball-bearing auxiliary guide bushing"),
        ("G_BUSH", "F", "GA auxiliary guide bushing"),
        ("MSB", "M", "Equal-height screw / spacer screw"),
        ("SetScr", "M", "Set screw (grub screw)"),
        ("UP_LTH", "EB", "Upper stop/limiter post"),
        ("DN_LTH", "EB", "Lower stop/limiter post"),
        ("DPP", "D", "D-type guide punch (cross type)"),
        ("EJE", "TT", "Knock/hammering hole"),
        ("MOLD", "C", "Die shank hole"),
        ("ULi_SPRING", "U", "Urethane (rubber) 2"),
        ("HO", "b", ""),
        ("hiHO", "a", ""),
    };

    // curver-map: (curveKey, markSym, func)
    public static readonly Dictionary<string, (string Sym, string Func)> CurverMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BUSH"] = ("o", "a"),
        ["CP4"] = ("m", "b"),
        ["CP1"] = ("i", "piercing"),
    };

    // platemap: (plateKey, fullName, extra)
    public static readonly Dictionary<string, (string FullName, string Extra)> PlateMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["handle"] = ("Handle plate", ""),
        ["u1"] = ("Upper plate 1", ""),
        ["u2"] = ("Upper plate 2", ""),
        ["cover"] = ("Cover plate", ""),
        ["up"] = ("Upper plate", ""),
        ["ub"] = ("Upper backing plate", ""),
        ["ub0"] = ("Upper backing plate 0", ""),
        ["ub2"] = ("Upper backing plate 2", ""),
        ["up_lth"] = ("Upper limiter plate", ""),
        ["ph"] = ("Punch holder plate", ""),
        ["ph2"] = ("Punch holder plate 2", ""),
        ["ps"] = ("Stripper plate", ""),
        ["ps2"] = ("Stripper plate 2", ""),
        ["pps"] = ("Backing stripper plate", ""),
        ["pps2"] = ("Backing stripper plate 2", ""),
        ["die"] = ("Die plate", ""),
        ["die2"] = ("Die plate 2", ""),
        ["lb"] = ("Lower backing plate", ""),
        ["lb2"] = ("Lower backing plate 2", ""),
        ["lb3"] = ("Lower backing plate 3", ""),
        ["dn_lth"] = ("Lower limiter plate", ""),
        ["lp"] = ("Lower plate", ""),
        ["b1"] = ("Base plate 1", ""),
        ["b2"] = ("Base plate 2", ""),
        ["guide"] = ("Guide plate", ""),
        ["side"] = ("Side plate", ""),
        ["eject"] = ("Ejector plate", ""),
        ["eject2"] = ("Ejector plate 2", ""),
    };

    public static void ApplyHoleData(HoleDataBlock data)
    {
        if (data == null) return;

        string v;
        if (data.Base.TryGetValue("DepthPrefix", out v)) BaseDepthPrefix = v;
        if (data.Base.TryGetValue("Unit", out v)) BaseUnit = v;
        if (data.Base.TryGetValue("ViewFront", out v)) BaseViewFront = v;
        if (data.Base.TryGetValue("ViewBack", out v)) BaseViewBack = v;
        if (data.Base.TryGetValue("ThicknessLabel", out v)) BaseThicknessLabel = v;
        if (data.Base.TryGetValue("KgUnit", out v)) BaseKgUnit = v;
        if (data.Base.TryGetValue("HrcLabel", out v)) BaseHrcLabel = v;
        if (data.Base.TryGetValue("Unknown", out v)) BaseUnknown = v;
        if (data.Base.TryGetValue("UnknownKey", out v)) BaseUnknownKey = v;
        if (data.Base.TryGetValue("Unknown2", out v)) BaseUnknown2 = v;
        if (data.Base.TryGetValue("Unknown3", out v)) BaseUnknown3 = v;

        if (data.HoleProcessMap.Count > 0)
        {
            HoleProcessMap.Clear();
            HoleProcessMap.AddRange(data.HoleProcessMap);
        }

        if (data.HoleTagMap.Count > 0)
        {
            HoleTagMap.Clear();
            HoleTagMap.AddRange(data.HoleTagMap);
        }

        if (data.CurverMap.Count > 0)
        {
            CurverMap.Clear();
            foreach (var kv in data.CurverMap) CurverMap[kv.Key] = kv.Value;
        }

        if (data.PlateMap.Count > 0)
        {
            PlateMap.Clear();
            foreach (var kv in data.PlateMap) PlateMap[kv.Key] = kv.Value;
        }

        if (data.HeaderLayerMap.Count > 0)
        {
            HeaderLayerMap.Clear();
            foreach (var kv in data.HeaderLayerMap) HeaderLayerMap[kv.Key] = kv.Value;
        }
    }

    public static List<string> KeysByLenDesc(IEnumerable<string> keys)
    {
        var list = new List<string>(keys);
        list.Sort((a, b) => b.Length.CompareTo(a.Length));
        return list;
    }

    public static string FmtNumClean(double x)
    {
        // LISP: rtos x 2 3 then trim zeros
        string s = x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return s;
    }

    public static string FmtDia(double d)
    {
        // LISP note:fmt-dia rtos 2 3 trim zeros
        string s = d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return s;
    }

    public static string DepthSuffix(double d)
    {
        if (Math.Abs(d) <= 1e-9) return "";
        string side = d > 0.0 ? BaseViewFront : BaseViewBack;
        string num = Math.Abs(d).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return BaseDepthPrefix + num + BaseUnit + side;
    }
}
}
