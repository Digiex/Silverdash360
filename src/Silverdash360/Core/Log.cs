using System;
using System.Globalization;
using Silverdash360.Native;

namespace Silverdash360.Core
{
    /// <summary>
    /// Debug output through the kernel's DbgPrint, which xenia writes to its log
    /// as "(DbgPrint) ..." and a development kit shows in its debug output.
    /// Debug.WriteLine goes nowhere on the console.
    /// </summary>
    public static class Log
    {
        public static void Write(string text)
        {
            try
            {
                Kernel.DbgPrint("%s\n", "[Silverdash] " + (text ?? ""));
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
    }
}
