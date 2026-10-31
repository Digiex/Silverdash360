namespace Silverdash360.Core
{
    /// <summary>
    /// What the long-running threads are doing, one line each, for the
    /// heartbeat to write down: after a hang it shows where each was. Plain
    /// field writes, so any thread can set its own without a lock.
    /// </summary>
    public static class Activity
    {
        public static volatile string Debug;
        public static volatile string Art;
        public static volatile string Images;
        public static volatile string Library;
        /// <summary>Counted by the UI thread each second.</summary>
        public static volatile int UiTicks;
    }
}
