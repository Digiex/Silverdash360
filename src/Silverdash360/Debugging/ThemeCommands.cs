using System;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using Silverdash360.Controls;
using Silverdash360.Model;
using Silverdash360.Modules;
using Silverdash360.Services;

namespace Silverdash360.Debugging
{
    /// <summary>The dashboard's theme: what's installed, what's picked and the colours it gave.</summary>
    public sealed class ThemeCommands : IDebugCommands
    {
        private readonly DashboardContext context;

        public ThemeCommands(DashboardContext context)
        {
            this.context = context;
        }

        private ThemeService Themes { get { return context.Themes; } }

        public void Register(DebugCommandSet commands)
        {
            commands.Add("theme.state", "", "The theme picked, the one loaded and its colours.", r => UiThread.Invoke(() => State()));
            commands.Add("theme.list", "", "The built-in themes and installed theme packages, with the setting that picks each.", r => UiThread.Invoke(() => List()));
            commands.Add("theme.set", "<dashboard|silverdash|builtin:N|key> [timeoutMs]",
                         "Picks a theme, as settings does, and waits for it to load. A key is a package's from theme.list.", Set);
        }

        private JsonObject State()
        {
            DashTheme theme = Themes.Current;
            return new JsonObject
            {
                { "setting", Themes.Setting },
                { "description", Themes.Describe() },
                { "loading", Themes.IsLoading },
                { "dashboardThemeIsBuiltIn", Themes.DashboardThemeIsBuiltIn },
                { "theme", theme == null ? null : new JsonObject
                    {
                        { "name", theme.Name },
                        { "package", theme.Package != null ? theme.Package.ToString() : null },
                        { "builtIn", theme.BuiltIn },
                        { "wallpapers", theme.Wallpapers.Select(w => w.Length).ToList() },
                        { "sceneTheme", theme.IsSceneTheme },
                        { "sphereColor", theme.SphereColor },
                        { "bladeStyle", theme.BladeStyle },
                    }
                },
                { "accent", Hex(Theme.Accent.Color) },
                { "panel", Hex(Theme.Panel.Color) },
                { "subText", Hex(Theme.SubText.Color) },
                { "default", Theme.IsDefault },
            };
        }

        private object List()
        {
            return Themes.BuiltInThemes.Select(n => new JsonObject
            {
                { "setting", ThemeService.SettingFor(n) },
                { "name", ThemeService.BuiltInName(n) },
            }).Concat(Themes.Installed().Select(t => new JsonObject
            {
                { "setting", ThemeService.SettingFor(t) },
                { "name", t.DisplayName },
                { "title", t.TitleName },
                { "package", t.ToString() },
            })).ToList();
        }

        private object Set(DebugRequest request)
        {
            string choice = request.Arg(0, "theme", true);
            if (choice != ThemeService.FollowDashboard && choice != ThemeService.NoTheme && !choice.StartsWith("package:") &&
                !choice.StartsWith("builtin:"))
            {
                ContentItem package = UiThread.Invoke(() => Themes.Installed())
                    .FirstOrDefault(t => ThemeService.SettingFor(t).EndsWith(choice, StringComparison.OrdinalIgnoreCase) ||
                                         t.DisplayName.Equals(choice, StringComparison.OrdinalIgnoreCase));
                if (package == null)
                {
                    throw new DebugException("No theme " + choice + ". \"theme.list\" lists them.");
                }
                choice = ThemeService.SettingFor(package);
            }
            int timeout = request.Int(1, 15000);
            string picked = choice;
            UiThread.Invoke(() => Themes.Choose(picked, null));
            int start = Environment.TickCount;
            Thread.Sleep(50);
            while (UiThread.Invoke(() => Themes.IsLoading) && Environment.TickCount - start < timeout)
            {
                Thread.Sleep(50);
            }
            JsonObject state = UiThread.Invoke(() => State());
            state.Add("loadMs", Environment.TickCount - start);
            return state;
        }

        private static string Hex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B);
        }
    }
}
