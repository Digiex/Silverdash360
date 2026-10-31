using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;

namespace Silverdash360.Core
{
    /// <summary>
    /// Work for the UI thread that gives way to the user: decoding art,
    /// building a section before it's shown. It runs between frames, a few
    /// milliseconds a frame, and only once the controller has been left alone
    /// for a moment, so a press is never queued behind it. UI thread only.
    /// </summary>
    public static class UiIdle
    {
        /// <summary>How long after the last move work waits, by default.</summary>
        public const int QuietMs = 250;

        // Work done a frame, past the first step. A frame that changes
        // anything on screen costs about 100 ms to draw on the console, so
        // several images shown in one frame are cheaper than one a frame;
        // past this a press would wait noticeably longer.
        private const int FrameBudgetMs = 30;

        private sealed class Job
        {
            public Func<bool> Step;
            public int QuietMs;
        }

        private static readonly List<Job> jobs = new List<Job>();
        // Written on the UI thread, read by background work too.
        private static volatile int lastInput;
        private static int frameStart;
        private static bool hooked;

        /// <summary>The user moved: work waits a while longer.</summary>
        public static void Input()
        {
            lastInput = Environment.TickCount;
        }

        /// <summary>How long background work waits after the last move, by default.</summary>
        public const int BackgroundQuietMs = 500;

        /// <summary>
        /// On a background thread, between steps of slow work such as reading
        /// the drives: waits while the user is moving, until they've left the
        /// controller alone for the time, as the work slows the UI down.
        /// </summary>
        public static void WaitWhileMoving(int quietMs)
        {
            int since;
            while ((since = SinceInput) < quietMs)
            {
                Thread.Sleep(Math.Max(20, quietMs - since));
            }
        }

        /// <summary>Milliseconds since the user last moved.</summary>
        public static int SinceInput
        {
            get { return Environment.TickCount - lastInput; }
        }

        /// <summary>Whether a step may do another piece of work this frame.</summary>
        public static bool HasTime
        {
            get { return Environment.TickCount - frameStart < FrameBudgetMs; }
        }

        /// <summary>Runs the action once the user is quiet.</summary>
        public static void Post(Action action)
        {
            Run(() =>
            {
                action();
                return false;
            }, QuietMs);
        }

        /// <summary>
        /// Calls the step once the user has been quiet for the time, and again
        /// a frame later for as long as it returns true. A step with many
        /// small pieces of work does them while <see cref="HasTime"/>.
        /// </summary>
        public static void Run(Func<bool> step, int quietMs)
        {
            jobs.Add(new Job { Step = step, QuietMs = quietMs });
            if (!hooked)
            {
                hooked = true;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        // Each job's step once, in turn, while the frame has time.
        private static void OnRendering(object sender, EventArgs e)
        {
            frameStart = Environment.TickCount;
            foreach (Job job in jobs.ToArray())
            {
                if (!HasTime)
                {
                    break;
                }
                // Something the user did in a step waits its turn too.
                if (SinceInput < job.QuietMs)
                {
                    continue;
                }
                bool more;
                try
                {
                    more = job.Step();
                }
                catch (Exception error)
                {
                    Log.Write("Idle work failed: {0}", error);
                    more = false;
                }
                if (!more)
                {
                    jobs.Remove(job);
                }
            }
            if (jobs.Count == 0)
            {
                hooked = false;
                CompositionTarget.Rendering -= OnRendering;
            }
        }
    }
}
