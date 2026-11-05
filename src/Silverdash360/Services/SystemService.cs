using System;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;
using Silverdash360.Services.Files;

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

        /// <summary>
        /// Starts an executable from a drive's Apps or Games folder, with its
        /// folder as the title's D: drive. Returns only when it fails.
        /// </summary>
        public string Launch(ExecutableItem executable)
        {
            if (executable == Library.XboxDashboard)
            {
                ExitToDashboard();
                return "Couldn't start the Xbox Dashboard.";
            }
            Log.Write("Launching {0}", executable.DevicePath);
            Xam.XamLoaderLaunchTitleEx(executable.DevicePath, executable.DeviceFolder, null, 0);
            return "Couldn't start " + executable.Name + ".";
        }

        /// <summary>Starts Silverdash360 again from its folder, as a new run; returns only when it can't.</summary>
        public static void RestartDashboard()
        {
            string device = AppFolder.DevicePath();
            if (device == null)
            {
                Log.Write("Where the dashboard runs from is unknown, so it isn't started again");
                return;
            }
            // Let the log line and the screen out first.
            System.Threading.Thread.Sleep(300);
            Xam.XamLoaderLaunchTitleEx(device + @"\default.xex", device, null, 0);
        }

        // How xam starts its system apps: their folder linked as XSYSLAUNCH:
        // and that given as the D: drive (xam 17559, 0x816EFED0).
        private const string SystemLaunchLink = @"\System??\XSYSLAUNCH:";
        private const string SystemLaunchDrive = @"XSYSLAUNCH:\";

        /// <summary>
        /// Starts the console's own dashboard, from the flash, even where
        /// Silverdash360 is the default. Leaving for the default dashboard
        /// starts Silverdash360 again then, and DashLaunch (3.x) swaps any
        /// start of dash.xex for its Default item too, unless the right
        /// bumper is held or it's a system launch as xam makes one: its D:
        /// drive XSYSLAUNCH:\ (launch.xex's XamLoaderLaunchTitleEx hook,
        /// 0x91F091F0). So it's started as one.
        /// </summary>
        public void ExitToDashboard()
        {
            ExecutableItem dash = Library.XboxDashboard;
            Log.Write("Returning to the console's dashboard, {0}", dash.DevicePath);
            using (ObjectName link = new ObjectName(SystemLaunchLink))
            using (ObjectName folder = new ObjectName(dash.DeviceFolder))
            {
                // One left by an earlier start would point elsewhere.
                Kernel.ObDeleteSymbolicLink(link.String);
                int status = Kernel.ObCreateSymbolicLink(link.String, folder.String);
                if (status < 0)
                {
                    Log.Write("Couldn't link {0} to {1}: {2:X8}", SystemLaunchLink, dash.DeviceFolder, status);
                    Xam.XamLoaderLaunchTitleEx(dash.DevicePath, dash.DeviceFolder, null, 0);
                    return;
                }
            }
            Xam.XamLoaderLaunchTitleEx(dash.DevicePath, SystemLaunchDrive, null, 0);
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
                return FileTimes.Local((ulong)fileTime) ?? DateTime.Now;
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
