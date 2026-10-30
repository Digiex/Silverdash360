using System;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;

namespace Silverdash360.Services
{
    /// <summary>Starting titles, and what the dashboard shows about the console.</summary>
    public sealed class SystemService
    {
        private const string DefaultExecutable = "default.xex";

        /// <summary>
        /// Starts the title in a package, the way the console's dashboard does.
        /// Returns only when it fails; the title replaces the dashboard.
        /// </summary>
        public string Launch(ContentItem item)
        {
            if (item.IsPartial)
            {
                return "This content isn't completely downloaded.";
            }
            if (item.IsCorrupt)
            {
                return "This content is damaged.";
            }
            Log.Write("Launching {0}", item);
            using (NativeBuffer record = new NativeBuffer(item.Record))
            {
                uint result = Xam.XamContentLaunchImageInternalEx(record.Address, DefaultExecutable, 0);
                Log.Write("Launch returned {0}", result);
                return result == Xam.ErrorSuccess ? null : "Couldn't start it (error " + result + ").";
            }
        }

        /// <summary>Returns to the console's own dashboard.</summary>
        public void ExitToDashboard()
        {
            Log.Write("Returning to the console's dashboard");
            Xam.XamLoaderLaunchTitle(null, 0);
        }

        public string SystemVersion
        {
            get
            {
                uint v = Xam.XamGetSystemVersion();
                return string.Format("{0}.{1}.{2}.{3}", v >> 28, (v >> 24) & 0xF, (v >> 8) & 0xFFFF, v & 0xFF);
            }
        }

        public string CurrentTitleId
        {
            get { return Xam.XamGetCurrentTitleId().ToString("X8"); }
        }

        public string Language
        {
            get
            {
                string[] names = { "", "English", "Japanese", "German", "French", "Spanish", "Italian", "Korean",
                                   "Traditional Chinese", "Portuguese", "Simplified Chinese", "Polish", "Russian" };
                uint language = Xam.XGetLanguage();
                return language < names.Length ? names[language] : "Language " + language;
            }
        }

        public DateTime ConsoleTime
        {
            get
            {
                long fileTime;
                Kernel.KeQuerySystemTime(out fileTime);
                return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "";
            }
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(unit == 0 || value >= 100 ? "0" : "0.0") + " " + units[unit];
        }
    }
}
