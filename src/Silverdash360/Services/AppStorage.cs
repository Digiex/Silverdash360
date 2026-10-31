using System;
using System.IO;
using System.Text;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Files;

namespace Silverdash360.Services
{
    /// <summary>
    /// The dashboard's own files (settings, the library cache), kept in a Data
    /// folder in the folder it's installed in. Its executable declares no
    /// title (the SDK's runtime profile takes the donor's out), so it has no
    /// title storage to keep them in. A build started from the staging folder
    /// deploy.py uses keeps them in the folder above it. One caller at a time:
    /// hold <see cref="Lock"/> around a read or write. Call it from a
    /// background thread: the disk blocks.
    /// </summary>
    public static class AppStorage
    {
        public const string FolderName = "Data";
        private const int BlockSize = 0x10000;
        // Written beside the file, then put in its place, so an interrupted
        // save leaves the old file.
        private const string NewSuffix = ".new";

        public static readonly object Lock = new object();

        private static XboxFileSystem files;
        private static string folder;

        /// <summary>Where the files are kept, such as /Hdd1/Apps/Silverdash/Data.</summary>
        public static string Folder
        {
            get
            {
                lock (Lock)
                {
                    Init();
                    return folder;
                }
            }
        }

        private static void Init()
        {
            if (folder != null)
            {
                return;
            }
            files = new XboxFileSystem();
            string install = null;
            try
            {
                install = AppFolder.Path(files);
                if (install != null && install.EndsWith("/" + AppFolder.StagingFolder, StringComparison.OrdinalIgnoreCase))
                {
                    install = install.Substring(0, install.Length - AppFolder.StagingFolder.Length - 1);
                }
                if (install != null && files.IsReadOnly(install))
                {
                    install = null;
                }
            }
            catch (Exception e)
            {
                Log.Write("App storage: where the dashboard runs from is unknown: {0}", e.Message);
                install = null;
            }
            folder = (install ?? AppFolder.Fallback) + "/" + FolderName;
            Log.Write("App storage in {0}", folder);
        }

        /// <summary>A file's bytes, or null when it isn't there or can't be read.</summary>
        public static byte[] ReadBytes(string fileName)
        {
            try
            {
                lock (Lock)
                {
                    Init();
                    string path = folder + "/" + fileName;
                    FileEntry entry = files.Stat(path);
                    if (entry == null || entry.IsDirectory)
                    {
                        return null;
                    }
                    MemoryStream bytes = new MemoryStream((int)entry.Size);
                    using (KernelFile file = files.OpenRead(path))
                    using (NativeBuffer buffer = new NativeBuffer(BlockSize))
                    {
                        long offset = 0;
                        int read;
                        while ((read = file.Read(buffer, buffer.Length, offset)) > 0)
                        {
                            bytes.Write(buffer.Bytes, 0, read);
                            offset += read;
                        }
                    }
                    return bytes.ToArray();
                }
            }
            catch (Exception e)
            {
                Log.Write("App storage: {0} unreadable: {1}", fileName, e.Message);
                return null;
            }
        }

        /// <summary>
        /// Saves a file; false when it couldn't be. pace, when given, is
        /// called between blocks so a background save leaves the console to
        /// the UI.
        /// </summary>
        public static bool WriteBytes(string fileName, byte[] bytes, Action pace)
        {
            try
            {
                lock (Lock)
                {
                    Init();
                    CreateFolders(folder);
                    string path = folder + "/" + fileName;
                    string written = path + NewSuffix;
                    using (KernelFile file = files.OpenWrite(written))
                    using (NativeBuffer buffer = new NativeBuffer(BlockSize))
                    {
                        for (int at = 0; at < bytes.Length; at += BlockSize)
                        {
                            int count = Math.Min(BlockSize, bytes.Length - at);
                            Buffer.BlockCopy(bytes, at, buffer.Bytes, 0, count);
                            file.Write(buffer, count, at);
                            if (pace != null)
                            {
                                pace();
                            }
                        }
                    }
                    if (files.Stat(path) != null)
                    {
                        files.Delete(path, false);
                    }
                    files.Rename(written, path);
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Write("App storage: {0} not saved: {1}", fileName, e.Message);
                return false;
            }
        }

        /// <summary>A small file's text, or null when it isn't there or can't be read.</summary>
        public static string ReadText(string fileName)
        {
            byte[] bytes = ReadBytes(fileName);
            return bytes != null ? Encoding.UTF8.GetString(bytes, 0, bytes.Length) : null;
        }

        public static void WriteText(string fileName, string text)
        {
            WriteBytes(fileName, Encoding.UTF8.GetBytes(text), null);
        }

        public static void Delete(string fileName)
        {
            try
            {
                lock (Lock)
                {
                    Init();
                    string path = folder + "/" + fileName;
                    if (files.Stat(path) != null)
                    {
                        files.Delete(path, false);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("App storage: {0} not deleted: {1}", fileName, e.Message);
            }
        }

        // The folders of a path that are missing, from the top down.
        private static void CreateFolders(string path)
        {
            if (files.Stat(path) != null)
            {
                return;
            }
            int slash = path.LastIndexOf('/');
            if (slash > 0)
            {
                CreateFolders(path.Substring(0, slash));
            }
            files.CreateDirectory(path);
        }
    }
}
