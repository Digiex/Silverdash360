using System;
using System.Text;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Modules;
using Silverdash360.Native;
using Silverdash360.Services;
using Silverdash360.Services.Files;
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
                { "user", FtpServer.UserName },
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
