using System;
using System.Text;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Files;

namespace Silverdash360.Services
{
    /// <summary>
    /// Writes the log to Logs/silverdash.log in the app's storage as it goes,
    /// so what led up to a hang can be read after a restart. The previous
    /// run's log is kept as silverdash.log.last.
    /// </summary>
    public static class LogFile
    {
        private const string Folder = "Logs";
        private const string Name = "silverdash.log";
        private const string LastSuffix = ".last";

        private static readonly object writeLock = new object();
        private static KernelFile file;
        private static NativeBuffer buffer;
        private static long offset;

        /// <summary>Starts writing; call once, early. Without storage it does nothing.</summary>
        public static void Start()
        {
            try
            {
                XboxFileSystem files = new XboxFileSystem();
                string folder = AppStorage.Folder + "/" + Folder;
                if (files.Stat(folder) == null)
                {
                    // The Data folder first, when this is its first file.
                    if (files.Stat(AppStorage.Folder) == null)
                    {
                        files.CreateDirectory(AppStorage.Folder);
                    }
                    files.CreateDirectory(folder);
                }
                string path = folder + "/" + Name;
                if (files.Stat(path) != null)
                {
                    if (files.Stat(path + LastSuffix) != null)
                    {
                        files.Delete(path + LastSuffix, false);
                    }
                    files.Rename(path, path + LastSuffix);
                }
                file = files.OpenWrite(path);
                buffer = new NativeBuffer(0x1000);
                Log.Sink = Write;
                Log.Write("Log file {0}", path);
            }
            catch (Exception e)
            {
                Log.Write("No log file: {0}", e.Message);
            }
        }

        private static void Write(string line)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
            lock (writeLock)
            {
                for (int at = 0; at < bytes.Length; at += buffer.Length)
                {
                    int count = Math.Min(buffer.Length, bytes.Length - at);
                    Buffer.BlockCopy(bytes, at, buffer.Bytes, 0, count);
                    file.Write(buffer, count, offset);
                    offset += count;
                }
            }
        }
    }
}
