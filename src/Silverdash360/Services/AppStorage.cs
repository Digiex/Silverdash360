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
    /// background thread: the disk blocks. A file name can have folders, as
    /// in Art/415607F7/boxartlg.jpg.
    /// </summary>
    public static class AppStorage
    {
        public const string FolderName = "Data";
        private const int BlockSize = 0x10000;
        // A save is written beside the file as .new, renamed to .saved once
        // it's all there, and only then put in the old file's place. Starting
        // a game ends the dashboard wherever it is, so a save can stop at any
        // step: a .new may be half written and is dropped, and a .saved is
        // whole and newer than the file (Recover).
        private const string NewSuffix = ".new";
        private const string SavedSuffix = ".saved";

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
                    Recover(path);
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
                    string path = folder + "/" + fileName;
                    CreateFolders(path.Substring(0, path.LastIndexOf('/')));
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
                    string saved = path + SavedSuffix;
                    if (files.Stat(saved) != null)
                    {
                        files.Delete(saved, false);
                    }
                    files.Rename(written, saved);
                    if (files.Stat(path) != null)
                    {
                        files.Delete(path, false);
                    }
                    files.Rename(saved, path);
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Write("App storage: {0} not saved: {1}", fileName, e.Message);
                return false;
            }
        }

        /// <summary>A file's size and time, or null when it isn't there.</summary>
        public static FileEntry Stat(string fileName)
        {
            try
            {
                lock (Lock)
                {
                    Init();
                    return files.Stat(folder + "/" + fileName);
                }
            }
            catch (Exception)
            {
                return null;
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

        // Finishes a save that was cut short: a whole .saved goes in the
        // file's place, and a .new, which may not be whole, goes.
        private static void Recover(string path)
        {
            string saved = path + SavedSuffix;
            if (files.Stat(saved) != null)
            {
                if (files.Stat(path) != null)
                {
                    files.Delete(path, false);
                }
                files.Rename(saved, path);
                Log.Write("App storage: finished saving {0}", path);
            }
            string written = path + NewSuffix;
            if (files.Stat(written) != null)
            {
                files.Delete(written, false);
                Log.Write("App storage: dropped an unfinished save of {0}", path);
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
