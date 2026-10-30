using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Silverdash360.Controls
{
    /// <summary>
    /// Gives a control focus once it can take it. Focus() fails while a control
    /// isn't loaded or laid out yet, and the controller's input goes to the
    /// focused control only, so a failed call leaves the dashboard deaf to it.
    /// </summary>
    public static class FocusHelper
    {
        private const int Attempts = 20;
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

        public static void Focus(Control control)
        {
            if (control == null)
            {
                return;
            }
            int attempts = 0;
            DispatcherTimer retry = new DispatcherTimer { Interval = Interval };
            retry.Tick += delegate
            {
                if (control.Focus() || ++attempts >= Attempts)
                {
                    retry.Stop();
                }
            };
            control.Dispatcher.BeginInvoke(() =>
            {
                if (!control.Focus())
                {
                    retry.Start();
                }
            });
        }
    }
}
