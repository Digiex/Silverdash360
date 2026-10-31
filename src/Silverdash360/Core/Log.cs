using System;
using System.Collections.Generic;
using System.Globalization;
using Silverdash360.Native;

namespace Silverdash360.Core
{
    /// <summary>
    /// Debug output through the kernel's DbgPrint, which xenia writes to its log
    /// as "(DbgPrint) ..." and a development kit shows in its debug output.
    /// Debug.WriteLine goes nowhere on the console. The latest lines are kept
    /// too, for the debug server to hand out on a console without a kit, and
    /// go to <see cref="Sink"/> when one is set (a file, so they outlast a
    /// hang).
    /// </summary>
    public static class Log
    {
        public const int Kept = 200;

        private static readonly Queue<string> recent = new Queue<string>();

        /// <summary>Also given each line, with its time; a failure in it is ignored.</summary>
        public static Action<string> Sink;

        // A line the sink writes about itself isn't handed back to it.
        [ThreadStatic]
        private static bool sinking;

        public static void Write(string text)
        {
            text = text ?? "";
            string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + text;
            lock (recent)
            {
                if (recent.Count == Kept)
                {
                    recent.Dequeue();
                }
                recent.Enqueue(line);
            }
            Action<string> sink = Sink;
            if (sink != null && !sinking)
            {
                sinking = true;
                try
                {
                    sink(line);
                }
                catch (Exception)
                {
                }
                finally
                {
                    sinking = false;
                }
            }
            try
            {
                Kernel.DbgPrint("%s\n", "[Silverdash] " + text);
            }
            catch (Exception)
            {
                // Logging must never take the dashboard down.
            }
        }

        public static void Write(string format, params object[] args)
        {
            Write(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        /// <summary>The latest lines written, oldest first.</summary>
        public static List<string> Recent(int count)
        {
            lock (recent)
            {
                List<string> lines = new List<string>(recent);
                return lines.GetRange(Math.Max(0, lines.Count - count), Math.Min(count, lines.Count));
            }
        }
    }
}
