using System;
using System.Windows.Controls;
using System.Windows.Media;

namespace Silverdash360.Controls
{
    /// <summary>
    /// Gives a control focus once it can take it. Focus() fails while a control
    /// isn't loaded or laid out yet, and the controller's input goes to the
    /// focused control only, so a failed call leaves the dashboard deaf to it.
    /// It tries again on each rendered frame, for up to about a second. (A
    /// DispatcherTimer per call made the runtime fail to create timers when
    /// the library was refreshed with thousands of titles.)
    /// </summary>
    public static class FocusHelper
    {
        private const int Frames = 60;

        private static Control pending;
        private static int framesLeft;
        private static bool hooked;

        public static void Focus(Control control)
        {
            if (control == null)
            {
                return;
            }
            // The latest request wins.
            pending = control;
            framesLeft = Frames;
            if (!hooked)
            {
                hooked = true;
                CompositionTarget.Rendering += OnRendering;
            }
        }

        private static void OnRendering(object sender, EventArgs e)
        {
            if (pending == null || pending.Focus() || --framesLeft <= 0)
            {
                pending = null;
                hooked = false;
                CompositionTarget.Rendering -= OnRendering;
            }
        }
    }
}
