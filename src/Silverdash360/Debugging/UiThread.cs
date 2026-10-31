using System;
using System.Threading;
using System.Windows;

namespace Silverdash360.Debugging
{
    /// <summary>Runs a command's work on the UI thread and waits for it.</summary>
    public static class UiThread
    {
        public const int Timeout = 15000;

        private sealed class Call<T>
        {
            public volatile bool Done;
            public T Result;
            public Exception Error;
        }

        public static T Invoke<T>(Func<T> work)
        {
            if (Deployment.Current.Dispatcher.CheckAccess())
            {
                return work();
            }
            Call<T> call = new Call<T>();
            Deployment.Current.Dispatcher.BeginInvoke(delegate
            {
                try
                {
                    call.Result = work();
                }
                catch (Exception e)
                {
                    call.Error = e;
                }
                call.Done = true;
            });
            // Polled: WaitOne with a timeout isn't dependable on this runtime.
            int start = Environment.TickCount;
            while (!call.Done)
            {
                if (Environment.TickCount - start > Timeout)
                {
                    throw new DebugException("The UI thread didn't answer within " + Timeout / 1000 + " seconds.");
                }
                Thread.Sleep(2);
            }
            if (call.Error != null)
            {
                throw call.Error;
            }
            return call.Result;
        }

        public static void Invoke(Action work)
        {
            Invoke<object>(() =>
            {
                work();
                return null;
            });
        }
    }
}
