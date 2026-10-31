using System;
using System.Text;
using Silverdash360.Native;
using Silverdash360.Services.Files;

namespace Silverdash360.Services
{
    /// <summary>
    /// Where the dashboard runs from. A title's folder is its D: drive, so
    /// the kernel's link for D: names it, such as
    /// \Device\Harddisk0\Partition1\Apps\Silverdash360.
    /// </summary>
    public static class AppFolder
    {
        /// <summary>Where the dashboard is kept when where it runs from isn't known or can't be written.</summary>
        public const string Fallback = "/Hdd1/Apps/Silverdash360";

        /// <summary>The kernel's name for a file in the title's folder: DrivePrefix + "default.xex".</summary>
        public const string DrivePrefix = @"\??\D:\";

        /// <summary>The kernel path D: links to, or null when it can't be read.</summary>
        public static string DevicePath()
        {
            try
            {
                using (ObjectName name = new ObjectName(@"\??\D:"))
                {
                    uint handle;
                    if (Kernel.NtOpenSymbolicLinkObject(out handle, name.Attributes) != Kernel.StatusSuccess)
                    {
                        return null;
                    }
                    try
                    {
                        using (NativeBuffer target = new NativeBuffer(8 + 260))
                        {
                            // ANSI_STRING: length, maximum length, buffer.
                            target.SetU16(0, 0);
                            target.SetU16(2, 260);
                            target.SetU32(4, target[8]);
                            if (Kernel.NtQuerySymbolicLinkObject(handle, target.Address, 0) != Kernel.StatusSuccess)
                            {
                                return null;
                            }
                            int length = Math.Min((int)target.U16(0), 260);
                            return Encoding.UTF8.GetString(target.Bytes, 8, length).TrimEnd('\\');
                        }
                    }
                    finally
                    {
                        Kernel.NtClose(handle);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Where the dashboard runs from as a path in the file system's tree
        /// (/Hdd1/Apps/Silverdash360), or null when it's on no drive the tree has.
        /// </summary>
        public static string Path(XboxFileSystem files)
        {
            string device = DevicePath();
            return device != null ? files.PathOf(device) : null;
        }
    }
}
