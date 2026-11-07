using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services;
using Silverdash360.Services.Files;
using Silverlight360;

namespace Silverdash360.Debugging
{
    /// <summary>
    /// Finishes an update that tools/deploy.py staged. A running dashboard's
    /// files can't be replaced safely, so the new build is uploaded to a
    /// folder of its own and started from there; finding the install marker
    /// in its folder, it copies the files the marker names into the app's
    /// folder and starts that. The marker, a text file:
    ///
    ///   silverdash360-install 1
    ///   target /Hdd1/Apps/Silverdash360
    ///   file Silverdash360.dll
    ///   file Config\machine.config
    ///
    /// Signed in to Live, the console checks for a title update while a
    /// title starts, and when it installs one it starts the title again from
    /// where it started: the staging folder, if the install had already
    /// started the target. So before starting the target the install leaves
    /// a note naming it, and a run that finds the note starts the target
    /// once more.
    /// </summary>
    public static class SelfInstall
    {
        public const string MarkerName = "deploy-install.txt";
        /// <summary>The note that the install has started the target.</summary>
        public const string StartedName = "deploy-started.txt";
        /// <summary>The folder in the app's folder deploy.py stages a build in.</summary>
        public const string StagingFolder = AppFolder.StagingFolder;
        private const string Header = "silverdash360-install 1";
        private const int BlockSize = 0x10000;
        private const uint FileOpenIf = 3;

        /// <summary>Why this run's install failed, or null.</summary>
        public static string LastError { get; private set; }

        public sealed class Plan
        {
            public string Target;
            public List<string> Files = new List<string>();
            /// <summary>Only starts the target again, as the note asks.</summary>
            public bool Restart;
        }

        /// <summary>The install the marker in the dashboard's folder asks for, or null.</summary>
        public static Plan Pending()
        {
            Plan plan = Read(MarkerName);
            if (plan == null)
            {
                plan = Read(StartedName);
                if (plan != null)
                {
                    plan.Files.Clear();
                    plan.Restart = true;
                }
            }
            return plan;
        }

        private static Plan Read(string fileName)
        {
            KernelFile marker;
            if (KernelFile.TryOpen(AppFolder.DrivePrefix + fileName, Kernel.GenericRead | Kernel.Synchronize,
                                   Kernel.FileShareRead, Kernel.FileOpen, Kernel.FileNonDirectoryFile, out marker)
                != Kernel.StatusSuccess)
            {
                return null;
            }
            string text;
            using (marker)
            using (NativeBuffer buffer = new NativeBuffer(BlockSize))
            {
                int read = marker.Read(buffer, buffer.Length, 0);
                text = Encoding.UTF8.GetString(buffer.Bytes, 0, read);
            }
            string[] lines = text.Replace("\r", "").Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Header)
            {
                Log.Write("Install marker not understood: {0}", lines.Length > 0 ? lines[0] : "");
                return null;
            }
            Plan plan = new Plan();
            foreach (string line in lines)
            {
                if (line.StartsWith("target "))
                {
                    plan.Target = line.Substring(7).Trim();
                }
                else if (line.StartsWith("file "))
                {
                    plan.Files.Add(line.Substring(5).Trim());
                }
            }
            return plan.Target != null ? plan : null;
        }

        /// <summary>
        /// Copies the files and starts the dashboard from the target, which
        /// ends this run; progress goes to report. Returns only on a failure,
        /// with what went wrong.
        /// </summary>
        public static string Run(Plan plan, Action<string> report)
        {
            XboxFileSystem files = new XboxFileSystem();
            try
            {
                string target = files.DevicePath(plan.Target);
                // Gone first: a failure part way mustn't start it all again.
                // Also by its path in the target, where deploy.py stages,
                // for when D: can't be written (as in xenia).
                if (plan.Restart)
                {
                    DeleteMarker(AppFolder.DrivePrefix + StartedName);
                    Log.Write("Started again from the staging folder; starting {0}", target + @"\default.xex");
                    Thread.Sleep(300);
                    Xam.XamLoaderLaunchTitleEx(target + @"\default.xex", target, null, 0);
                    LastError = "The console didn't start " + plan.Target + ".";
                    return LastError;
                }
                DeleteMarker(AppFolder.DrivePrefix + MarkerName);
                if (Exists(target + @"\" + StagingFolder + @"\" + MarkerName))
                {
                    DeleteMarker(target + @"\" + StagingFolder + @"\" + MarkerName);
                }
                if (files.IsReadOnly(plan.Target))
                {
                    throw new IOException(plan.Target + " is read-only.");
                }
                Log.Write("Installing {0} files into {1} ({2})", plan.Files.Count, plan.Target, target);
                CreateDirectories(target);
                using (NativeBuffer buffer = new NativeBuffer(BlockSize))
                {
                    for (int i = 0; i < plan.Files.Count; i++)
                    {
                        string file = plan.Files[i].Replace('/', '\\');
                        report(string.Format("Installing {0} of {1}: {2}", i + 1, plan.Files.Count, file));
                        int slash = file.LastIndexOf('\\');
                        if (slash > 0)
                        {
                            CreateDirectories(target + "\\" + file.Substring(0, slash));
                        }
                        Copy(AppFolder.DrivePrefix + file, target + "\\" + file, buffer);
                    }
                }
                report("Starting the new build...");
                Log.Write("Installed; starting {0}", target + @"\default.xex");
                WriteStarted(plan.Target);
                // Let the log line and the screen out first.
                Thread.Sleep(300);
                Xam.XamLoaderLaunchTitleEx(target + @"\default.xex", target, null, 0);
                LastError = "The console didn't start " + plan.Target + ".";
            }
            catch (Exception e)
            {
                Log.Write("Install failed: {0}", e);
                LastError = e.Message;
            }
            return LastError;
        }

        private static void Copy(string source, string destination, NativeBuffer buffer)
        {
            using (KernelFile from = KernelFile.Open(source, Kernel.GenericRead | Kernel.Synchronize, Kernel.FileShareRead,
                                                     Kernel.FileOpen, Kernel.FileNonDirectoryFile))
            using (KernelFile to = KernelFile.Open(destination, Kernel.GenericWrite | Kernel.Synchronize, 0,
                                                   Kernel.FileOverwriteIf, Kernel.FileNonDirectoryFile))
            {
                long offset = 0;
                int read;
                while ((read = from.Read(buffer, buffer.Length, offset)) > 0)
                {
                    to.Write(buffer, read, offset);
                    offset += read;
                }
            }
        }

        // The folders of a kernel path that are missing, made from the
        // deepest that exists down. Only those are opened: a drive's own
        // folders can refuse it (xenia's hard drive Content folder does).
        private static void CreateDirectories(string device)
        {
            List<string> missing = new List<string>();
            string path = device;
            while (!Exists(path))
            {
                missing.Add(path);
                int slash = path.LastIndexOf('\\');
                if (slash <= 0)
                {
                    throw new IOException(device + " is on no drive.");
                }
                path = path.Substring(0, slash);
            }
            for (int i = missing.Count - 1; i >= 0; i--)
            {
                KernelFile folder;
                int status = KernelFile.TryOpen(missing[i], Kernel.FileListDirectory | Kernel.Synchronize,
                                                Kernel.FileShareRead, FileOpenIf, Kernel.FileDirectoryFile, out folder);
                if (status != Kernel.StatusSuccess)
                {
                    throw new IOException(XboxFileSystem.Describe(status, missing[i]));
                }
                folder.Dispose();
            }
        }

        private static bool Exists(string device)
        {
            using (ObjectName name = new ObjectName(device))
            using (NativeBuffer information = new NativeBuffer(0x38))
            {
                return Kernel.NtQueryFullAttributesFile(name.Attributes, information.Address) == Kernel.StatusSuccess;
            }
        }

        /// <summary>
        /// Deletes a note left in the staging folder when the target started
        /// without the console starting the staged build again.
        /// </summary>
        public static void ClearStarted()
        {
            try
            {
                string note = AppFolder.DrivePrefix + StagingFolder + @"\" + StartedName;
                if (Exists(note))
                {
                    DeleteMarker(note);
                }
            }
            catch (Exception e)
            {
                Log.Write("Install note not deleted: {0}", e.Message);
            }
        }

        // The note a run started again from here finds; only in the
        // dashboard's own folder, so a run elsewhere can't take it up.
        private static void WriteStarted(string target)
        {
            byte[] text = Encoding.UTF8.GetBytes(Header + "\ntarget " + target + "\n");
            using (NativeBuffer buffer = new NativeBuffer(text))
            using (KernelFile note = KernelFile.Open(AppFolder.DrivePrefix + StartedName,
                                                     Kernel.GenericWrite | Kernel.Synchronize, 0,
                                                     Kernel.FileOverwriteIf, Kernel.FileNonDirectoryFile))
            {
                note.Write(buffer, text.Length, 0);
            }
        }

        private static void DeleteMarker(string device)
        {
            using (KernelFile marker = KernelFile.Open(device, Kernel.Delete | Kernel.Synchronize,
                                                       Kernel.FileShareRead | Kernel.FileShareWrite | Kernel.FileShareDelete,
                                                       Kernel.FileOpen, Kernel.FileNonDirectoryFile))
            {
                marker.SetInformation(new byte[] { 1 }, Kernel.FileDispositionInformation, MarkerName);
            }
        }
    }
}
