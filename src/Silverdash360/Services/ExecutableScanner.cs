using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;
using Silverdash360.Services.Files;
using Silverlight360;

namespace Silverdash360.Services
{
    /// <summary>
    /// Finds titles kept as plain executables rather than packages: a
    /// default.xex in each folder of a drive's Apps and Games folders, and
    /// .xex files directly in Apps. The title ID comes from the XEX header.
    /// </summary>
    public sealed class ExecutableScanner
    {
        private const string DefaultExecutable = "default.xex";
        private const uint XexMagic = 0x58455832; // "XEX2"
        private const uint ExecutionInfoKey = 0x00040006;
        private const int HeaderRead = 0x1000;

        private readonly XboxFileSystem files;
        private readonly CustomArt custom;
        private Action pace;

        public ExecutableScanner(XboxFileSystem files)
        {
            this.files = files;
            custom = new CustomArt(files);
            Collections = new List<string> { "Apps", "Games" };
            ExtraFolders = new List<KeyValuePair<string, string>>();
        }

        /// <summary>The folders looked in at the root of every drive.</summary>
        public List<string> Collections { get; private set; }

        /// <summary>
        /// More folders to look in, as a path in the file tree and the
        /// collection (Apps or Games) its titles belong to.
        /// </summary>
        public List<KeyValuePair<string, string>> ExtraFolders { get; private set; }

        /// <summary>
        /// The executables on the drives. Ones in known with the same size and
        /// time keep their title ID without their header being read again.
        /// </summary>
        public List<ExecutableItem> Scan(IDictionary<string, ExecutableItem> known, Action pace)
        {
            this.pace = pace;
            List<ExecutableItem> found = new List<ExecutableItem>();
            foreach (Drive drive in files.AvailableDrives.Where(d => !d.System))
            {
                foreach (string collection in Collections)
                {
                    ScanFolder("/" + drive.Name + "/" + collection, collection, drive.Name, known, found);
                }
            }
            foreach (KeyValuePair<string, string> extra in ExtraFolders)
            {
                ScanFolder(extra.Key, extra.Value, extra.Key.TrimStart('/').Split('/')[0], known, found);
            }
            return found;
        }

        private void ScanFolder(string folder, string collection, string drive,
                                IDictionary<string, ExecutableItem> known, List<ExecutableItem> found)
        {
            List<FileEntry> entries;
            try
            {
                entries = files.List(folder);
            }
            catch (Exception)
            {
                // The drive has no such folder.
                return;
            }
            foreach (FileEntry entry in entries)
            {
                // Each folder is looked in, found or not: wait while the user moves.
                UiIdle.WaitWhileMoving(UiIdle.BackgroundQuietMs);
                string path = null;
                string name = entry.Name;
                if (entry.IsDirectory)
                {
                    path = folder + "/" + entry.Name + "/" + DefaultExecutable;
                }
                else if (entry.Name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(collection, "Apps", StringComparison.OrdinalIgnoreCase))
                {
                    path = folder + "/" + entry.Name;
                    name = entry.Name.Substring(0, entry.Name.Length - 4);
                }
                if (path == null)
                {
                    continue;
                }
                FileEntry file = entry.IsDirectory ? files.Stat(path) : entry;
                if (file == null || file.IsDirectory)
                {
                    continue;
                }
                ExecutableItem item = new ExecutableItem
                {
                    Path = path,
                    DevicePath = files.DevicePath(path),
                    DeviceFolder = files.DevicePath(path.Substring(0, path.LastIndexOf('/'))),
                    Name = name,
                    Collection = collection,
                    Drive = drive,
                    Size = file.Size,
                    Modified = file.Modified,
                };
                ExecutableItem before;
                if (known != null && known.TryGetValue(path, out before) && before.Size == item.Size &&
                    before.Modified == item.Modified)
                {
                    item.TitleId = before.TitleId;
                }
                else
                {
                    item.TitleId = ReadTitleId(path);
                }
                // A name the user gave it, read each time: it can change
                // while the executable doesn't.
                string art = CustomArt.FolderOf(item);
                if (art != null && files.Stat(art) != null)
                {
                    TitleDetails details = custom.Details(art);
                    item.CustomName = details != null && details.Name.Length > 0 ? details.Name : null;
                }
                found.Add(item);
                if (pace != null)
                {
                    pace();
                }
            }
        }

        // The title ID in the XEX header's execution info, or 0.
        private uint ReadTitleId(string path)
        {
            try
            {
                using (KernelFile file = files.OpenRead(path))
                {
                    return TitleIdOf(file);
                }
            }
            catch (IOException e)
            {
                Log.Write("Couldn't read {0}: {1}", path, e.Message);
            }
            return 0;
        }

        /// <summary>The title ID in an open XEX's execution info, or 0.</summary>
        public static uint TitleIdOf(KernelFile file)
        {
            using (NativeBuffer header = new NativeBuffer(HeaderRead))
            {
                int read = file.Read(header, header.Length, 0);
                if (read < 0x18 || header.U32(0) != XexMagic)
                {
                    return 0;
                }
                // Optional headers: a count, then key and value pairs. The
                // execution info's value is the offset of its 0x18 bytes.
                uint count = header.U32(0x14);
                for (int i = 0; i < count && 0x18 + i * 8 + 8 <= read; i++)
                {
                    if (header.U32(0x18 + i * 8) != ExecutionInfoKey)
                    {
                        continue;
                    }
                    uint at = header.U32(0x1C + i * 8);
                    if (at + 0x10 <= read)
                    {
                        return header.U32((int)at + 0xC);
                    }
                    read = file.Read(header, 0x10, at);
                    return read >= 0x10 ? header.U32(0xC) : 0;
                }
            }
            return 0;
        }
    }
}
