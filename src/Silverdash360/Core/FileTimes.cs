using System;

namespace Silverdash360.Core
{
    /// <summary>
    /// Windows file times as the console stores them, read into DateTimes.
    /// </summary>
    public static class FileTimes
    {
        /// <summary>
        /// The last file time a DateTime holds, DateTime.MaxValue's. A constant:
        /// on the console's runtime a static readonly struct such as
        /// DateTime.MaxValue can read as zero, and DateTime.MaxValue.ToFileTimeUtc()
        /// then throws, which ended FTP listings of real files.
        /// </summary>
        public const long Max = 2650467743999999999;

        /// <summary>The time in UTC, or null for none or one out of range.</summary>
        public static DateTime? Utc(ulong fileTime)
        {
            if (fileTime == 0 || fileTime > Max)
            {
                return null;
            }
            try
            {
                return DateTime.FromFileTimeUtc((long)fileTime);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>The time in local time, or null for none or one out of range.</summary>
        public static DateTime? Local(ulong fileTime)
        {
            DateTime? utc = Utc(fileTime);
            return utc.HasValue ? utc.Value.ToLocalTime() : (DateTime?)null;
        }
    }
}
