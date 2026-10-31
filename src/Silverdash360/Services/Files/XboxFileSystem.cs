using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Silverdash360.Native;

namespace Silverdash360.Services.Files
{
    /// <summary>A file or folder in a listing.</summary>
    public sealed class FileEntry
    {
        public string Name;
        public bool IsDirectory;
        public long Size;
        public DateTime Modified;
    }

    /// <summary>A storage device, shown as a folder at the root.</summary>
    public sealed class Drive
    {
        public Drive(string name, string devicePath, bool readOnly, params string[] standardFolders)
        {
            Name = name;
            DevicePath = devicePath;
            ReadOnly = readOnly;
            StandardFolders = standardFolders;
        }

        public string Name { get; private set; }
        public string DevicePath { get; private set; }
        public bool ReadOnly { get; private set; }

        /// <summary>
        /// Shown in the root of the tree (and over FTP); false for one the
        /// dashboard uses itself, such as a package it has mounted.
        /// </summary>
        public bool Listed = true;

        /// <summary>
        /// Folders the drive is known to have, listed when its root can't be.
        /// xenia, for one, exposes only the hard drive's Content folder.
        /// </summary>
        public string[] StandardFolders { get; private set; }
    }

    /// <summary>
    /// The console's storage as one tree of slash-separated paths: the root
    /// holds a folder per drive ("/Hdd1/Content/..."), and everything below
    /// goes to the kernel's file calls on the drive's device.
    /// </summary>
    public sealed class XboxFileSystem
    {
        private const int DirectoryBufferSize = 0x1000;

        public XboxFileSystem()
        {
            Drives = new List<Drive>
            {
                new Drive("Hdd1", @"\Device\Harddisk0\Partition1", false, "Content", "Games", "Apps", "Cache"),
                new Drive("Usb0", @"\Device\Mass0", false),
                new Drive("Usb1", @"\Device\Mass1", false),
                new Drive("Usb2", @"\Device\Mass2", false),
                new Drive("Mu0", @"\Device\Mu0", false),
                new Drive("Mu1", @"\Device\Mu1", false),
                new Drive("Dvd", @"\Device\Cdrom0", true),
            };
        }

        /// <summary>The drives that can be shown; edit to add or hide one.</summary>
        public List<Drive> Drives { get; private set; }

        /// <summary>The drives present now.</summary>
        public IEnumerable<Drive> AvailableDrives
        {
            get
            {
                return Drives.Where(d => d.Listed && (Exists(d.DevicePath) ||
                                                      d.StandardFolders.Any(f => Exists(d.DevicePath + @"\" + f))));
            }
        }

        /// <summary>Joins and normalises a path, resolving "." and "..".</summary>
        public static string Combine(string directory, string path)
        {
            string full = path.StartsWith("/") ? path : directory.TrimEnd('/') + "/" + path;
            List<string> parts = new List<string>();
            foreach (string part in full.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }
                if (part == "..")
                {
                    if (parts.Count > 0)
                    {
                        parts.RemoveAt(parts.Count - 1);
                    }
                    continue;
                }
                parts.Add(part);
            }
            return "/" + string.Join("/", parts.ToArray());
        }

        public List<FileEntry> List(string path)
        {
            if (path == "/")
            {
                return AvailableDrives.Select(d => new FileEntry { Name = d.Name, IsDirectory = true, Modified = DateTime.Now }).ToList();
            }
            Drive drive;
            string device = Resolve(path, out drive);
            List<FileEntry> entries;
            int status = TryList(device, out entries);
            if (status != Kernel.StatusSuccess)
            {
                if (IsDriveRoot(path))
                {
                    // Only some folders of the drive can be reached.
                    return drive.StandardFolders.Where(f => Exists(device + @"\" + f))
                                .Select(f => new FileEntry { Name = f, IsDirectory = true, Modified = DateTime.Now })
                                .ToList();
                }
                throw new IOException(Describe(status, path));
            }
            return entries;
        }

        /// <summary>The entry for a path, or null when there's nothing there.</summary>
        public FileEntry Stat(string path)
        {
            if (path == "/" || IsDriveRoot(path))
            {
                bool exists = path == "/" ||
                              AvailableDrives.Any(d => string.Equals("/" + d.Name, path, StringComparison.OrdinalIgnoreCase));
                return exists ? new FileEntry { Name = path, IsDirectory = true, Modified = DateTime.Now } : null;
            }
            Drive drive;
            string device = Resolve(path, out drive);
            using (ObjectName name = new ObjectName(device))
            using (NativeBuffer info = new NativeBuffer(0x38))
            {
                if (Kernel.NtQueryFullAttributesFile(name.Attributes, info.Address) != Kernel.StatusSuccess)
                {
                    return null;
                }
                return new FileEntry
                {
                    Name = path.Substring(path.LastIndexOf('/') + 1),
                    IsDirectory = (info.U32(0x30) & Kernel.FileAttributeDirectory) != 0,
                    Size = (long)info.U64(0x28),
                    Modified = Time(info.U64(0x10)),
                };
            }
        }

        /// <summary>
        /// The tree's path for a kernel path, such as /Hdd1/Apps for
        /// \Device\Harddisk0\Partition1\Apps, or null when no drive has it.
        /// </summary>
        public string PathOf(string devicePath)
        {
            foreach (Drive drive in Drives)
            {
                string prefix = drive.DevicePath;
                if (devicePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    (devicePath.Length == prefix.Length || devicePath[prefix.Length] == '\\'))
                {
                    return Combine("/" + drive.Name, devicePath.Substring(prefix.Length).Replace('\\', '/').TrimStart('/'));
                }
            }
            return null;
        }

        /// <summary>Whether a path is on a drive that can't be written, such as the disc.</summary>
        public bool IsReadOnly(string path)
        {
            Drive drive;
            Resolve(path, out drive);
            return drive.ReadOnly;
        }

        /// <summary>The kernel's path for a path in the tree, such as \Device\Harddisk0\Partition1\Games.</summary>
        public string DevicePath(string path)
        {
            Drive drive;
            return Resolve(path, out drive);
        }

        public KernelFile OpenRead(string path)
        {
            Drive drive;
            return KernelFile.Open(Resolve(path, out drive), Kernel.GenericRead | Kernel.Synchronize,
                                   Kernel.FileShareRead, Kernel.FileOpen, Kernel.FileNonDirectoryFile);
        }

        public KernelFile OpenWrite(string path)
        {
            Drive drive;
            string device = Resolve(path, out drive);
            Writable(drive, path);
            return KernelFile.Open(device, Kernel.GenericWrite | Kernel.Synchronize, 0,
                                   Kernel.FileOverwriteIf, Kernel.FileNonDirectoryFile);
        }

        public void CreateDirectory(string path)
        {
            Drive drive;
            string device = Resolve(path, out drive);
            Writable(drive, path);
            using (KernelFile.Open(device, Kernel.FileListDirectory | Kernel.Synchronize, Kernel.FileShareRead,
                                   Kernel.FileCreate, Kernel.FileDirectoryFile))
            {
            }
        }

        /// <summary>Deletes a file, or a folder that's empty.</summary>
        public void Delete(string path, bool directory)
        {
            Drive drive;
            string device = Resolve(path, out drive);
            Writable(drive, path);
            // Folders go only when empty, whatever the file system would do.
            if (directory && List(path).Count > 0)
            {
                throw new IOException(path + " isn't empty.");
            }
            using (KernelFile file = KernelFile.Open(device, Kernel.Delete | Kernel.Synchronize,
                                                     Kernel.FileShareRead | Kernel.FileShareWrite | Kernel.FileShareDelete,
                                                     Kernel.FileOpen, directory ? Kernel.FileDirectoryFile : Kernel.FileNonDirectoryFile))
            {
                // FILE_DISPOSITION_INFORMATION: the file goes when it's closed.
                file.SetInformation(new byte[] { 1 }, Kernel.FileDispositionInformation, path);
            }
        }

        public void Rename(string from, string to)
        {
            Drive fromDrive, toDrive;
            string source = Resolve(from, out fromDrive);
            string target = Resolve(to, out toDrive);
            if (fromDrive != toDrive)
            {
                throw new IOException("Can't move between drives.");
            }
            Writable(fromDrive, from);
            using (KernelFile file = KernelFile.Open(source, Kernel.Delete | Kernel.Synchronize,
                                                     Kernel.FileShareRead | Kernel.FileShareWrite | Kernel.FileShareDelete,
                                                     Kernel.FileOpen, 0))
            {
                // FILE_RENAME_INFORMATION: replace flag, root handle, then an
                // ANSI_STRING whose text follows the structure.
                byte[] name = Encoding.UTF8.GetBytes(target);
                using (NativeBuffer info = new NativeBuffer(16 + name.Length + 1))
                {
                    Buffer.BlockCopy(name, 0, info.Bytes, 16, name.Length);
                    info.SetU16(8, (ushort)name.Length);
                    info.SetU16(10, (ushort)(name.Length + 1));
                    info.SetU32(12, info[16]);
                    file.SetInformation(info, Kernel.FileRenameInformation, to);
                }
            }
        }

        private bool IsDriveRoot(string path)
        {
            return path.LastIndexOf('/') == 0 && path.Length > 1;
        }

        // The device path for a path below a drive.
        private string Resolve(string path, out Drive drive)
        {
            string[] parts = path.TrimStart('/').Split('/');
            string driveName = parts[0];
            drive = Drives.FirstOrDefault(d => string.Equals(d.Name, driveName, StringComparison.OrdinalIgnoreCase));
            if (drive == null)
            {
                throw new FileNotFoundException("No drive " + driveName + ".");
            }
            StringBuilder device = new StringBuilder(drive.DevicePath);
            for (int i = 1; i < parts.Length; i++)
            {
                device.Append('\\').Append(parts[i]);
            }
            // The root of a mount such as \??\name: opens as name:\ only.
            if (device[device.Length - 1] == ':')
            {
                device.Append('\\');
            }
            return device.ToString();
        }

        private static void Writable(Drive drive, string path)
        {
            if (drive.ReadOnly)
            {
                throw new UnauthorizedAccessException(drive.Name + " is read-only.");
            }
        }

        private static bool Exists(string device)
        {
            List<FileEntry> ignored;
            using (ObjectName name = new ObjectName(device))
            using (NativeBuffer info = new NativeBuffer(0x38))
            {
                if (Kernel.NtQueryFullAttributesFile(name.Attributes, info.Address) == Kernel.StatusSuccess)
                {
                    return true;
                }
            }
            // A device's root has no attributes to query on some systems.
            return TryList(device, out ignored) == Kernel.StatusSuccess;
        }

        private static int TryList(string device, out List<FileEntry> entries)
        {
            entries = new List<FileEntry>();
            KernelFile directory;
            int status = KernelFile.TryOpen(device, Kernel.FileListDirectory | Kernel.Synchronize,
                                            Kernel.FileShareRead | Kernel.FileShareWrite, Kernel.FileOpen,
                                            Kernel.FileDirectoryFile, out directory);
            if (status != Kernel.StatusSuccess)
            {
                return status;
            }
            using (directory)
            using (NativeBuffer io = new NativeBuffer(8))
            using (NativeBuffer buffer = new NativeBuffer(DirectoryBufferSize))
            {
                uint restart = 1;
                while (true)
                {
                    status = Kernel.NtQueryDirectoryFile(directory.Handle, 0, 0, 0, io.Address, buffer.Address,
                                                         (uint)buffer.Length, 0, restart);
                    // An empty folder answers the first query with no such
                    // file rather than no more files.
                    if (restart != 0 && status == Kernel.StatusNoSuchFile)
                    {
                        status = Kernel.StatusNoMoreFiles;
                    }
                    restart = 0;
                    if (status != Kernel.StatusSuccess)
                    {
                        break;
                    }
                    // FILE_DIRECTORY_INFORMATION records, chained by their
                    // next-entry offsets.
                    int at = 0;
                    while (true)
                    {
                        int length = (int)buffer.U32(at + 0x3C);
                        if (at + 0x40 + length > buffer.Length)
                        {
                            break;
                        }
                        entries.Add(new FileEntry
                        {
                            Name = Encoding.UTF8.GetString(buffer.Bytes, at + 0x40, length),
                            IsDirectory = (buffer.U32(at + 0x38) & Kernel.FileAttributeDirectory) != 0,
                            Size = (long)buffer.U64(at + 0x28),
                            Modified = Time(buffer.U64(at + 0x18)),
                        });
                        uint next = buffer.U32(at);
                        if (next == 0)
                        {
                            break;
                        }
                        at += (int)next;
                    }
                }
            }
            return status == Kernel.StatusNoMoreFiles ? Kernel.StatusSuccess : status;
        }

        private static DateTime Time(ulong fileTime)
        {
            return Core.FileTimes.Local(fileTime) ?? DateTime.Now;
        }

        internal static string Describe(int status, string path)
        {
            switch ((uint)status)
            {
                case 0xC0000034: return path + " doesn't exist.";
                case 0xC000003A: return "The folder of " + path + " doesn't exist.";
                case 0xC0000035: return path + " already exists.";
                case 0xC0000022: return "Access to " + path + " is denied.";
                case 0xC0000101: return path + " isn't empty.";
                case 0xC0000103: return path + " isn't a folder.";
                case 0xC00000BA: return path + " is a folder.";
                case 0xC000007F: return "The drive is full.";
                default: return string.Format("{0}: error {1:X8}.", path, status);
            }
        }
    }

    /// <summary>
    /// An OBJECT_ATTRIBUTES naming a device path, with an IO_STATUS_BLOCK,
    /// pinned for one call.
    /// </summary>
    internal sealed class ObjectName : IDisposable
    {
        private readonly NativeBuffer buffer;

        public ObjectName(string path)
        {
            byte[] name = Encoding.UTF8.GetBytes(path);
            buffer = new NativeBuffer(32 + name.Length + 1);
            Buffer.BlockCopy(name, 0, buffer.Bytes, 32, name.Length);
            // ANSI_STRING at 0, OBJECT_ATTRIBUTES at 8, IO_STATUS_BLOCK at 24.
            buffer.SetU16(0, (ushort)name.Length);
            buffer.SetU16(2, (ushort)(name.Length + 1));
            buffer.SetU32(4, buffer[32]);
            buffer.SetU32(8, 0);
            buffer.SetU32(12, buffer[0]);
            buffer.SetU32(16, Kernel.ObjCaseInsensitive);
        }

        public uint Attributes { get { return buffer[8]; } }
        public uint IoStatus { get { return buffer[24]; } }

        public void Dispose()
        {
            buffer.Dispose();
        }
    }

    /// <summary>A kernel file handle, read and written at offsets.</summary>
    public sealed class KernelFile : IDisposable
    {
        private readonly NativeBuffer io = new NativeBuffer(16);

        private KernelFile(uint handle)
        {
            Handle = handle;
        }

        public uint Handle { get; private set; }

        internal static int TryOpen(string device, uint access, uint share, uint disposition, uint options, out KernelFile file)
        {
            file = null;
            using (ObjectName name = new ObjectName(device))
            {
                uint handle;
                int status = Kernel.NtCreateFile(out handle, access, name.Attributes, name.IoStatus, 0,
                                                 Kernel.FileAttributeNormal, share, disposition,
                                                 options | Kernel.FileSynchronousIoNonAlert);
                if (status == Kernel.StatusSuccess)
                {
                    file = new KernelFile(handle);
                }
                return status;
            }
        }

        internal static KernelFile Open(string device, uint access, uint share, uint disposition, uint options)
        {
            KernelFile file;
            int status = TryOpen(device, access, share, disposition, options, out file);
            if (status != Kernel.StatusSuccess)
            {
                throw new IOException(XboxFileSystem.Describe(status, device));
            }
            return file;
        }

        /// <summary>Reads into a pinned buffer at a file offset; 0 at the end.</summary>
        public int Read(NativeBuffer buffer, int count, long offset)
        {
            io.SetU64(8, (ulong)offset);
            int status = Kernel.NtReadFile(Handle, 0, 0, 0, io[0], buffer.Address, (uint)count, io[8]);
            if (status == Kernel.StatusEndOfFile)
            {
                return 0;
            }
            if (status != Kernel.StatusSuccess)
            {
                throw new IOException(string.Format("Read failed: {0:X8}.", status));
            }
            return (int)io.U32(4);
        }

        public void Write(NativeBuffer buffer, int count, long offset)
        {
            io.SetU64(8, (ulong)offset);
            int status = Kernel.NtWriteFile(Handle, 0, 0, 0, io[0], buffer.Address, (uint)count, io[8]);
            if (status != Kernel.StatusSuccess)
            {
                throw new IOException(string.Format("Write failed: {0:X8}.", status));
            }
        }

        internal void SetInformation(byte[] information, uint informationClass, string path)
        {
            using (NativeBuffer buffer = new NativeBuffer(information))
            {
                SetInformation(buffer, informationClass, path);
            }
        }

        internal void SetInformation(NativeBuffer information, uint informationClass, string path)
        {
            int status = Kernel.NtSetInformationFile(Handle, io[0], information.Address, (uint)information.Length,
                                                     informationClass);
            if (status != Kernel.StatusSuccess)
            {
                throw new IOException(XboxFileSystem.Describe(status, path));
            }
        }

        public void Dispose()
        {
            if (Handle != 0)
            {
                Kernel.NtClose(Handle);
                Handle = 0;
            }
            io.Dispose();
        }
    }
}
