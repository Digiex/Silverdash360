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
    /// too, for the debug server to hand out on a console without a kit.
    /// </summary>
    public static class Log
    {
        public const int Kept = 200;

        private static readonly Queue<string> recent = new Queue<string>();

        public static void Write(string text)
        {
            text = text ?? "";
            lock (recent)
            {
                if (recent.Count == Kept)
                {
                    recent.Dequeue();
                }
                recent.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + text);
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
