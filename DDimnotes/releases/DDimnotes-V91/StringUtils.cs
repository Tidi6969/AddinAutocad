using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DDimnotes.Utils
{
    internal static class StringUtils
    {
        private static readonly Regex Ws = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex Num = new Regex(@"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        public static string NormalizeWhitespace(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            return Ws.Replace(s.Trim(), " ");
        }

        public static string[] SplitWs(string s)
        {
            var norm = NormalizeWhitespace(s);
            return norm.Length == 0 ? Array.Empty<string>() : norm.Split(' ');
        }

        public static double ParseFirstNumberOrZero(string s)
        {
            if (s == null) return 0.0;
            var m = Num.Match(s);
            if (!m.Success) return 0.0;
            double v;
            if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return v;
            return 0.0;
        }
    }
}
