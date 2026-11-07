using System;
using System.Collections.Generic;
using System.Linq;
using Silverdash360.Modules;
using Silverdash360.Native;
using Silverdash360.Services.Files;
using Silverlight360;

namespace Silverdash360.Debugging
{
    /// <summary>
    /// Finding and copying files the system keeps outside the content folders,
    /// such as the dashboard parts the console downloads on demand
    /// (Dash.MP.ContentExplorer.lex and the other Dash.MP lexes). xam keeps
    /// those in the hard drive's system cache, which it mounts from
    /// \Device\Harddisk0\Cache0 and Cache1 as \System??\SysCache0: and 1:.
    /// </summary>
    public sealed class SysCacheCommands : IDebugCommands
    {
        private const int MaxDepth = 4;
        private const int MaxFound = 500;
        private const int CopyChunk = 0x10000;

        // Where the system cache may be reached from a title: xam's mounts
        // first, then the raw cache partitions.
        private static readonly string[] Roots =
        {
            @"\System??\SysCache0:\",
            @"\System??\SysCache1:\",
            @"\??\SysCache0:\",
            @"\??\SysCache1:\",
            @"\Device\Harddisk0\Cache0\",
            @"\Device\Harddisk0\Cache1\",
            @"\Device\Harddisk0\SystemAuxPartition\",
        };

        private readonly DashboardContext context;

        public SysCacheCommands(DashboardContext context)
        {
            this.context = context;
        }

        private XboxFileSystem Files { get { return context.Ftp.Files; } }

        public void Register(DebugCommandSet commands)
        {
            commands.Add("syscache.find", "[name]",
                         "Tries each place the system cache is mounted and lists the files whose names contain name " +
                         "(ContentExplorer by default), a few folders deep.", Find);
            commands.Add("fs.copydevice", "<devicePath> <path>",
                         "Copies a file by the kernel's name for it, such as one syscache.find found, to a path such as " +
                         "/Hdd1/ContentExplorer.lex, to download over FTP.", CopyDevice);
        }

        private object Find(DebugRequest request)
        {
            string name = request.Arg(0, "ContentExplorer");
            List<JsonObject> roots = new List<JsonObject>();
            List<JsonObject> found = new List<JsonObject>();
            foreach (string root in Roots)
            {
                List<FileEntry> entries;
                int status = Files.TryListDevice(root, out entries);
                roots.Add(new JsonObject
                {
                    { "path", root },
                    { "status", status.ToString("X8") },
                    { "entries", status == Kernel.StatusSuccess ? entries.Count : 0 },
                });
                if (status == Kernel.StatusSuccess)
                {
                    Walk(root, entries, name, 0, found);
                }
            }
            return new JsonObject { { "roots", roots }, { "found", found } };
        }

        private void Walk(string folder, List<FileEntry> entries, string name, int depth, List<JsonObject> found)
        {
            foreach (FileEntry entry in entries)
            {
                if (found.Count >= MaxFound)
                {
                    return;
                }
                string path = folder.TrimEnd('\\') + @"\" + entry.Name;
                if (entry.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found.Add(new JsonObject
                    {
                        { "devicePath", path },
                        { "directory", entry.IsDirectory },
                        { "size", entry.Size },
                        { "modified", entry.Modified },
                    });
                }
                List<FileEntry> children;
                if (entry.IsDirectory && depth < MaxDepth &&
                    Files.TryListDevice(path, out children) == Kernel.StatusSuccess)
                {
                    Walk(path, children, name, depth + 1, found);
                }
            }
        }

        private object CopyDevice(DebugRequest request)
        {
            string source = request.Arg(0, "devicePath", true);
            string target = request.Arg(1, "path", true);
            int start = Environment.TickCount;
            long copied = 0;
            using (KernelFile input = KernelFile.Open(source, Kernel.GenericRead | Kernel.Synchronize,
                                                      Kernel.FileShareRead | Kernel.FileShareWrite, Kernel.FileOpen,
                                                      Kernel.FileNonDirectoryFile))
            using (KernelFile output = Files.OpenWrite(target))
            using (NativeBuffer buffer = new NativeBuffer(CopyChunk))
            {
                while (true)
                {
                    int read = input.Read(buffer, CopyChunk, copied);
                    if (read <= 0)
                    {
                        break;
                    }
                    output.Write(buffer, read, copied);
                    copied += read;
                }
            }
            return new JsonObject
            {
                { "source", source },
                { "path", target },
                { "bytes", copied },
                { "copyMs", Environment.TickCount - start },
            };
        }
    }
}
