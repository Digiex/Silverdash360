using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Xbox.Controls;
using Microsoft.Xbox.Transitions;

namespace Silverdash360.Controls
{
    /// <summary>
    /// How the dashboard moves: one place for every animation's length and
    /// what it animates. On the console a short animation that starts fast
    /// feels quickest, and what's animated has to be cheap to draw on each
    /// frame: a translation of cached content is, a change of opacity over a
    /// large area isn't. The values can be changed while the dashboard runs
    /// (the debug server's ui.motion), so they can be tried on a console.
    /// </summary>
    public static class Motion
    {
        /// <summary>A grid's scroll to keep the selection in view.</summary>
        public static int ScrollMs = 150;

        /// <summary>A hub section sliding in, and how far it slides.</summary>
        public static int SectionMs = 160;
        public static double SectionDistance = 40;

        /// <summary>Whether the section fades in as it slides; costly to draw.</summary>
        public static bool SectionFade = false;

        /// <summary>A page coming in, and the one before going: 0 swaps at once.</summary>
        public static int PageInMs = 150;
        public static int PageOutMs = 0;
        public static double PageDistance = 30;

        /// <summary>Whether pages made from now keep the platform's transitions, for comparing.</summary>
        public static bool PlatformPages = false;

        /// <summary>
        /// Whether a grid draws its tiles into one cached bitmap while it
        /// scrolls, which makes a scroll a move of one bitmap but redraws them
        /// all when anything on a tile changes. Still, a grid is drawn as it
        /// is, as a kept bitmap is softer. Taken from the next scroll.
        /// </summary>
        public static bool CacheGrids = true;

        /// <summary>The easing for things coming to rest: quick at first, then settling.</summary>
        public static IEasingFunction EaseOut()
        {
            return new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        }

        /// <summary>Gives a page the dashboard's page transitions in place of the platform's slower ones.</summary>
        public static void UsePageTransitions(XboxApplicationPage page)
        {
            if (PlatformPages)
            {
                return;
            }
            // XboxApplicationFrame starts a page's transition 300 ms after
            // the page arrives, on a timer, unless the page has item lists:
            // then it starts once they've loaded, which is the page's first
            // frame. An empty one starts the transition at once.
            // Found the way the frame finds them: Content is protected.
            Panel root = VisualTreeHelper.GetChildrenCount(page) > 0 ? VisualTreeHelper.GetChild(page, 0) as Panel : null;
            if (root != null)
            {
                root.Children.Add(new ItemsControl { Width = 0, Height = 0, IsHitTestVisible = false, IsTabStop = false });
            }
            TransitionService.SetNavigationInTransition(page, new NavigationInTransition
            {
                Forward = new PageTransition { Entering = true, Direction = 1 },
                Backward = new PageTransition { Entering = true, Direction = -1 },
            });
            TransitionService.SetNavigationOutTransition(page, new NavigationOutTransition
            {
                Forward = new PageTransition { Entering = false, Direction = 1 },
                Backward = new PageTransition { Entering = false, Direction = -1 },
            });
        }

        /// <summary>
        /// A page sliding a little way in from the side it's coming from and
        /// fading up, or the page leaving doing the same out. The platform's
        /// fade out then in takes 600 ms, with input ignored all the while.
        /// </summary>
        private sealed class PageTransition : TransitionElement
        {
            public bool Entering;
            public int Direction;

            public override ITransition GetTransition(UIElement element, TimeSpan delay)
            {
                int ms = Entering ? PageInMs : PageOutMs;
                TranslateTransform move = new TranslateTransform();
                element.RenderTransform = move;
                // The frame times the hand-over from the storyboard's declared
                // Duration; left automatic, it waits the platform's 350 ms.
                Storyboard story = new Storyboard { BeginTime = delay, Duration = TimeSpan.FromMilliseconds(ms) };
                DoubleAnimation slide = new DoubleAnimation
                {
                    From = Entering ? PageDistance * Direction : 0,
                    To = Entering ? 0 : -PageDistance * Direction,
                    Duration = TimeSpan.FromMilliseconds(ms),
                    EasingFunction = EaseOut(),
                };
                Storyboard.SetTarget(slide, move);
                Storyboard.SetTargetProperty(slide, new PropertyPath("X"));
                DoubleAnimation fade = new DoubleAnimation
                {
                    From = Entering ? 0.3 : 1,
                    To = Entering ? 1 : 0,
                    Duration = TimeSpan.FromMilliseconds(ms),
                };
                Storyboard.SetTarget(fade, element);
                Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
                story.Children.Add(slide);
                story.Children.Add(fade);
                return new Transition(element, story);
            }
        }
    }
}
