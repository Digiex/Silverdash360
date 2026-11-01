using System;
using System.Text;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Modules;
using Silverdash360.Native;
using Silverdash360.Services;
using Silverdash360.Services.Files;
using Silverdash360.Services.Media;
using Silverdash360.Services.Ftp;

namespace Silverdash360.Debugging
{
    /// <summary>
    /// What tools/deploy.py needs to update the dashboard on a console:
    /// where it runs from, the FTP server to upload with, and starting a build.
    /// </summary>
    public sealed class AppCommands : IDebugCommands
    {
        /// <summary>The file deploy.py writes with each build it uploads, naming it.</summary>
        public const string DeployIdName = ".deploy-id";

        private readonly DashboardContext context;

        public AppCommands(DashboardContext context)
        {
            this.context = context;
        }

        private XboxFileSystem Files { get { return context.Ftp.Files; } }

        public void Register(DebugCommandSet commands)
        {
            commands.Add("app.location", "", "Where the dashboard runs from, where it's kept otherwise, and the build's deploy ID.", r => Location());
            commands.Add("app.launch", "[path]", "Starts the default.xex in a folder (this one by default), ending this run.", Launch);
            commands.Add("net.offline", "[off|dns|noroute]",
                         "Makes requests to the internet fail as on a console without it (see Http.OfflineMode); with nothing, says which.", r =>
            {
                if (r.Args.Count > 0)
                {
                    string mode = r.Args[0];
                    if (mode != "off" && mode != "dns" && mode != "noroute")
                    {
                        throw new DebugException("off, dns or noroute.");
                    }
                    Http.OfflineMode = mode == "off" ? null : mode;
                    Log.Write("Debug: requests to the internet {0}", Http.OfflineMode == null ? "go out" : "fail: " + mode);
                }
                return Http.OfflineMode ?? "off";
            });
            commands.Add("update.check", "[asIfVersion]", "Looks for an update as if this build were the version given, and offers it.", UpdateCheck);
            commands.Add("update.verify", "", "Downloads the latest release and checks it against its size and SHA-256, without installing it.", UpdateVerify);
            commands.Add("update.install", "<zipPath>", "Installs a release zip on the console's drives, as an update would, and starts it.", UpdateInstall);
            commands.Add("ftp.start", "[port]", "Starts the FTP server and gives its port, user and password.", FtpStart);
            commands.Add("ftp.stop", "", "Stops the FTP server.", r =>
            {
                context.Ftp.Stop();
                return null;
            });
        }

        private JsonObject Location()
        {
            string device = AppFolder.DevicePath();
            string path = device != null ? Files.PathOf(device) : null;
            return new JsonObject
            {
                { "devicePath", device },
                { "path", path },
                { "writable", path != null && !Files.IsReadOnly(path) },
                { "fallback", AppFolder.Fallback },
                { "deployId", ReadDeployId() },
                { "installError", SelfInstall.LastError },
                { "uptimeMs", (long)(DateTime.UtcNow - DebugServer.Current.Started).TotalMilliseconds },
            };
        }

        // The check as the hub's, against the version given, waiting for its
        // answer; a release found is offered.
        private object UpdateCheck(DebugRequest request)
        {
            Version than = request.Args.Count > 0 ? new Version(request.Args[0]) : UpdateService.Current;
            JsonObject result = new JsonObject { { "asIf", than.ToString() } };
            bool done = false;
            UiThread.Invoke(() =>
            {
                context.Updates.Check(than, false, delegate(UpdateService.Release release, string why)
                {
                    if (release != null)
                    {
                        result.Add("found", release.Tag);
                        result.Add("zip", release.ZipUrl);
                        result.Add("size", release.ZipSize);
                        result.Add("sha256", release.ZipSha256);
                        context.Updates.Offered = release;
                        context.OpenUpdate();
                    }
                    else
                    {
                        result.Add("none", why);
                    }
                    done = true;
                });
                return 0;
            });
            int start = Environment.TickCount;
            while (!done && Environment.TickCount - start < 30000)
            {
                Thread.Sleep(50);
            }
            return result;
        }

        // Downloads the latest release's zip and checks it, as an update
        // would, without installing it.
        private object UpdateVerify(DebugRequest request)
        {
            JsonObject result = new JsonObject();
            bool done = false;
            int start = Environment.TickCount;
            UiThread.Invoke(() =>
            {
                context.Updates.Check(new Version(0, 0), true, delegate(UpdateService.Release release, string why)
                {
                    if (release == null)
                    {
                        result.Add("none", why);
                        done = true;
                        return;
                    }
                    result.Add("release", release.Tag);
                    context.Updates.Download(release, delegate(byte[] zip, string problem)
                    {
                        result.Add("bytes", zip != null ? zip.Length : -1);
                        result.Add("problem", problem);
                        result.Add("ms", Environment.TickCount - start);
                        done = true;
                    });
                });
                return 0;
            });
            while (!done && Environment.TickCount - start < 60000)
            {
                Thread.Sleep(50);
            }
            return result;
        }

        // Installs a zip from the console's drives as an update would; the
        // dashboard starts the result, ending this run. What happens goes to
        // the log.
        private object UpdateInstall(DebugRequest request)
        {
            string path = XboxFileSystem.Combine("/", request.Arg(0, "zipPath", true));
            byte[] zip;
            using (KernelFile file = Files.OpenRead(path))
            using (NativeBuffer buffer = new NativeBuffer((int)Files.Stat(path).Size))
            {
                int read = file.Read(buffer, buffer.Length, 0);
                zip = buffer.Slice(0, read);
            }
            UpdateService.Release release = new UpdateService.Release
            {
                Version = UpdateService.Current, Tag = "test", Name = "the test build", ZipSize = zip.Length,
            };
            UiThread.Invoke(() =>
            {
                context.Updates.InstallZip(release, zip,
                                           text => Log.Write("Update: {0}", text),
                                           problem => Log.Write("Update failed: {0}", problem));
                return 0;
            });
            return new JsonObject { { "installing", path }, { "bytes", zip.Length } };
        }

        private object Launch(DebugRequest request)
        {
            string device;
            if (request.Args.Count > 0)
            {
                string path = XboxFileSystem.Combine("/", request.Args[0]);
                device = Files.DevicePath(path);
                if (Files.Stat(path + "/default.xex") == null)
                {
                    throw new DebugException("No default.xex in " + path + ".");
                }
            }
            else
            {
                device = AppFolder.DevicePath();
                if (device == null)
                {
                    throw new DebugException("Where the dashboard runs from isn't known.");
                }
            }
            // After the reply has gone: the launch ends this run.
            new Thread(delegate()
            {
                Thread.Sleep(500);
                Log.Write("Debug server starting {0}", device + @"\default.xex");
                Xam.XamLoaderLaunchTitleEx(device + @"\default.xex", device, null, 0);
            }) { IsBackground = true }.Start();
            return new JsonObject { { "launching", device + @"\default.xex" } };
        }

        private object FtpStart(DebugRequest request)
        {
            FtpServer ftp = context.Ftp;
            if (!ftp.IsRunning)
            {
                ftp.Port = request.Int(0, FtpServer.DefaultPort);
                ftp.Start();
            }
            return new JsonObject
            {
                { "address", ftp.Address },
                { "port", ftp.ListeningPort },
                { "user", ftp.UserName },
                { "password", ftp.Password },
            };
        }

        private static string ReadDeployId()
        {
            KernelFile file;
            if (KernelFile.TryOpen(AppFolder.DrivePrefix + DeployIdName, Kernel.GenericRead | Kernel.Synchronize,
                                   Kernel.FileShareRead, Kernel.FileOpen, Kernel.FileNonDirectoryFile, out file)
                != Kernel.StatusSuccess)
            {
                return null;
            }
            using (file)
            using (NativeBuffer buffer = new NativeBuffer(256))
            {
                return Encoding.UTF8.GetString(buffer.Bytes, 0, file.Read(buffer, buffer.Length, 0)).Trim();
            }
        }
    }
}
