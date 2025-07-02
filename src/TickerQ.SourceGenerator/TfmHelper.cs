using System;
using System.Text.RegularExpressions;

namespace TickerQ.SourceGenerator
{
    public class TfmHelper
    {
        private static readonly Regex _tfmVersionRegex = new Regex(@"^net(\d+)\.(\d+)", RegexOptions.Compiled);

        public static bool IsNet6OrGreaterFromTfm(string tfm)
        {
            var match = _tfmVersionRegex.Match(tfm);
            if (match.Success)
            {
                int major = int.Parse(match.Groups[1].Value);
                int minor = string.IsNullOrEmpty(match.Groups[2].Value) ? 0 : int.Parse(match.Groups[2].Value);

                var version = new Version(major, minor);
                return version >= new Version(6, 0);
            }

            return false;
        }
    }
}