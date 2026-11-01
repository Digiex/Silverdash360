using System;
using System.Threading;
using System.Windows;

namespace Silverdash360.Core
{
    /// <summary>
    /// Runs slow native calls (enumerating content, reading headers and tiles)
    /// off the UI thread and hands the result back on it, so the UI never
    /// waits on the disk.
    /// </summary>
    public static class Background
    {
        public static void Run<T>(Func<T> work, Action<T, Exception> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                T result = default(T);
                Exception error = null;
                try
                {
                    result = work();
                }
                catch (Exception e)
                {
                    error = e;
                    Log.Write("Background work failed: {0}\n{1}", e, e.StackTrace);
                }
                OnUi(() => done(result, error));
            });
        }

        public static void OnUi(Action action)
        {
            Deployment.Current.Dispatcher.BeginInvoke(action);
        }
    }
}
