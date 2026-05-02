using Autodesk.AutoCAD.DatabaseServices;

namespace DDimnotes
{
    // Compatibility wrapper for older DNOTES code.
    // Language selection is controlled only by Lang.cs through MIS_SET.SET line 28
    // and embedded UserData/lang.dat in the DLL.
    internal enum NtLanguage
    {
        Default = 0
    }

    internal static class NtLang
    {
        public static NtLanguage Current { get { return NtLanguage.Default; } }

        public static void Init(Database db, string misSetPath)
        {
            // Command entry already calls Lang.BeginCommand() and Lang.Reload().
            // Keep this method as a no-op so business/notes code never reloads language by itself.
        }

        public static void SetLanguage(Database db, NtLanguage lang)
        {
            // Compatibility wrapper only. Do not map language ids here.
        }

        public static string T(string key)
        {
            return Lang.T(key);
        }

        public static string F(string key, params object[] args)
        {
            return Lang.F(key, args);
        }
    }
}
