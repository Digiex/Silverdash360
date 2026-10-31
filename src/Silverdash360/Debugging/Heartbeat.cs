using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services;
using Silverdash360.Services.Files;

namespace Silverdash360.Debugging
{
    /// <summary>
    /// Writes every few seconds what each long-running thread is doing
    /// (<see cref="Activity"/>) and whether the UI thread still counts, to
    /// Logs/heartbeat.log, so a hang leaves where each thread was. It takes no
    /// lock another thread could hold and doesn't wait on the UI thread: it
    /// writes its own file, and the UI thread's progress is a counter a UI
    /// timer bumps. Lines that go on through a hang show the stuck threads;
    /// lines that stop mean the whole runtime stopped.
    /// </summary>
    public static class Heartbeat
    {
        private const int IntervalMs = 5000;
        private const string Name = "heartbeat.log";

        private static KernelFile file;
        private static NativeBuffer buffer;
        private static long offset;

        // On the UI thread.
        public static void Start()
        {
            DispatcherTimer ticks = new DispatcherTimer { Interval = new TimeSpan(0, 0, 1) };
            ticks.Tick += delegate { Activity.UiTicks++; };
            ticks.Start();
            try
            {
                XboxFileSystem files = new XboxFileSystem();
                string path = AppStorage.Folder + "/Logs/" + Name;
                if (files.Stat(path) != null)
                {
                    if (files.Stat(path + ".last") != null)
                    {
                        files.Delete(path + ".last", false);
                    }
                    files.Rename(path, path + ".last");
                }
                file = files.OpenWrite(path);
                buffer = new NativeBuffer(0x1000);
            }
            catch (Exception e)
            {
                Log.Write("No heartbeat file: {0}", e.Message);
                return;
            }
            new Thread(Beat) { IsBackground = true, Name = "Heartbeat" }.Start();
        }

        private static void Beat()
        {
            int lastTicks = Activity.UiTicks;
            while (true)
            {
                Thread.Sleep(IntervalMs);
                int ticks = Activity.UiTicks;
                string line = string.Format(CultureInfo.InvariantCulture,
                    "{0:HH:mm:ss.fff} UI {1} | debug: {2} | art: {3} | images: {4} | library: {5}\r\n",
                    DateTime.Now, ticks != lastTicks ? "ticking" : "STOPPED", Activity.Debug ?? "-",
                    Activity.Art ?? "-", Activity.Images ?? "-", Activity.Library ?? "-");
                lastTicks = ticks;
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                int count = Math.Min(bytes.Length, buffer.Length);
                Buffer.BlockCopy(bytes, 0, buffer.Bytes, 0, count);
                file.Write(buffer, count, offset);
                offset += count;
            }
        }
    }
}
