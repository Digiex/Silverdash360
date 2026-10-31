using System;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework.Storage;
using Silverdash360.Core;

namespace Silverdash360.Services
{
    /// <summary>
    /// The title's storage container, where the dashboard keeps its files.
    /// The console mounts a container once, so it's opened by one caller at a
    /// time: hold <see cref="Lock"/> from the open to the dispose. Call it
    /// from a background thread: storage blocks.
    /// </summary>
    public static class TitleStorage
    {
        private const string ContainerName = "Silverdash360";
        // A constant: a static readonly TimeSpan read as zero on the
        // console's runtime, which gave up on any open that didn't finish at
        // once.
        private const int OpenTimeoutMs = 60000;

        public static readonly object Lock = new object();

        /// <summary>The container, or null when there's no storage.</summary>
        public static StorageContainer Open()
        {
            StorageDevice device = new StorageDevice();
            IAsyncResult opening = device.BeginOpenContainer(ContainerName, ContainerName, null, null);
            // Polled rather than waited on: the wait handle could report a
            // timeout at once while the open went on.
            int started = Environment.TickCount;
            while (!opening.IsCompleted)
            {
                if (Environment.TickCount - started > OpenTimeoutMs)
                {
                    Log.Write("Title storage: the container didn't open");
                    return null;
                }
                Thread.Sleep(20);
            }
            return device.EndOpenContainer(opening);
        }

        /// <summary>A small file's text, or null when it isn't there or can't be read.</summary>
        public static string ReadText(string fileName)
        {
            try
            {
                lock (Lock)
                {
                    using (StorageContainer container = Open())
                    {
                        if (container == null || !container.FileExists(fileName))
                        {
                            return null;
                        }
                        using (StreamReader reader = new StreamReader(container.OpenFile(fileName, FileMode.Open)))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Title storage: {0} unreadable: {1}", fileName, e.Message);
                return null;
            }
        }

        public static void WriteText(string fileName, string text)
        {
            try
            {
                lock (Lock)
                {
                    using (StorageContainer container = Open())
                    {
                        if (container == null)
                        {
                            return;
                        }
                        using (StreamWriter writer = new StreamWriter(container.CreateFile(fileName)))
                        {
                            writer.Write(text);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Title storage: {0} not saved: {1}", fileName, e.Message);
            }
        }
    }
}
