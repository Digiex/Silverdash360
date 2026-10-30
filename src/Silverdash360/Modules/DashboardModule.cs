using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Silverdash360.Controls;

namespace Silverdash360.Modules
{
    /// <summary>
    /// A section of the hub, such as games or settings. The hub shows one at a
    /// time and switches with LB/RB, or with the D-pad past a section's left or
    /// right edge. To add a section, implement this (or derive from
    /// <see cref="DashboardModule"/>) and register it in
    /// <see cref="ModuleRegistry"/>.
    /// </summary>
    public interface IDashboardModule
    {
        /// <summary>The section's name in the hub's header.</summary>
        string Header { get; }

        /// <summary>Where the section goes in the header, lowest first.</summary>
        int Order { get; }

        /// <summary>The section's content, made once.</summary>
        FrameworkElement View { get; }

        /// <summary>What the controller buttons do, for the hub's footer.</summary>
        string Legend { get; }

        /// <summary>The section was shown: focus its first control.</summary>
        void Activate();

        /// <summary>The library or profile changed: update what's shown.</summary>
        void Refresh();

        /// <summary>The D-pad left the section's content; the hub may switch sections.</summary>
        event EventHandler<EdgeEventArgs> EdgeReached;
    }

    /// <summary>Base for sections, with the view made on first use.</summary>
    public abstract class DashboardModule : IDashboardModule
    {
        private FrameworkElement view;

        protected DashboardModule(DashboardContext context)
        {
            Context = context;
        }

        protected DashboardContext Context { get; private set; }

        public abstract string Header { get; }
        public abstract int Order { get; }
        public virtual string Legend { get { return "A select"; } }

        public FrameworkElement View
        {
            get
            {
                if (view == null)
                {
                    view = CreateView();
                    Refresh();
                }
                return view;
            }
        }

        public event EventHandler<EdgeEventArgs> EdgeReached;

        protected abstract FrameworkElement CreateView();
        public abstract void Activate();
        public abstract void Refresh();

        /// <summary>Hands a grid's edge to the hub.</summary>
        protected void ForwardEdges(TileGrid grid, params Edge[] edges)
        {
            grid.EdgeReached += delegate(object sender, EdgeEventArgs e)
            {
                if (edges.Contains(e.Edge) && EdgeReached != null)
                {
                    EdgeReached(this, e);
                }
            };
        }

        protected void Focus(UIElement element)
        {
            System.Windows.Controls.Control control = element as System.Windows.Controls.Control;
            if (control != null)
            {
                FocusHelper.Focus(control);
            }
        }
    }

    /// <summary>
    /// The hub's sections. Edit <see cref="CreateAll"/> to add, remove or
    /// reorder them.
    /// </summary>
    public static class ModuleRegistry
    {
        private static readonly List<Func<DashboardContext, IDashboardModule>> Factories =
            new List<Func<DashboardContext, IDashboardModule>>
            {
                c => new HomeModule(c),
                c => new LibraryModule(c, "games", 20, l => l.Games, "No games found on this console."),
                c => new LibraryModule(c, "apps", 30, l => l.Apps, "No apps found on this console."),
                c => new ProfileModule(c),
                c => new SettingsModule(c),
            };

        /// <summary>Adds a section; call before the hub is shown.</summary>
        public static void Register(Func<DashboardContext, IDashboardModule> factory)
        {
            Factories.Add(factory);
        }

        public static List<IDashboardModule> CreateAll(DashboardContext context)
        {
            return Factories.Select(f => f(context)).OrderBy(m => m.Order).ToList();
        }
    }
}
