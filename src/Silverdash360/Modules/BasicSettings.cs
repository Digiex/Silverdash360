using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Silverdash360.Core;
using Silverdash360.Services;
using Silverdash360.Services.Ftp;

namespace Silverdash360.Modules
{
    /// <summary>
    /// A setting the welcome screen offers; settings has it too. Since is the
    /// welcome version it came in: one newer than the user has seen opens the
    /// welcome screen after an update, with the setting marked new.
    /// </summary>
    public sealed class BasicSetting
    {
        public string Title;
        public string Description;
        public int Since;
        public Func<string> Value;
        /// <summary>Whether the welcome screen shows it now; null for always.</summary>
        public Func<bool> Shown;
        /// <summary>Changes it, then calls back on the UI thread.</summary>
        public Action<Action> Change;
    }

    /// <summary>The settings the welcome screen offers, and whether it's due.</summary>
    public static class BasicSettings
    {
        private const string SeenSetting = "welcome.seen";

        public static List<BasicSetting> All(DashboardContext context)
        {
            FtpServer ftp = context.Ftp;
            return new List<BasicSetting>
            {
                new BasicSetting
                {
                    Title = "Start the FTP server with Silverdash",
                    Description = "Shares the console's drives over FTP whenever Silverdash runs",
                    Since = 1,
                    Value = () => ftp.AutoStart ? "On" : "Off",
                    Change = done =>
                    {
                        ftp.AutoStart = !ftp.AutoStart;
                        if (ftp.AutoStart)
                        {
                            StartFtp(context);
                        }
                        done();
                    },
                },
                new BasicSetting
                {
                    Title = "FTP user name",
                    Description = "The user name FTP clients log in with",
                    Since = 1,
                    Value = () => ftp.UserName,
                    Shown = () => ftp.AutoStart,
                    Change = done => Ask(context, "FTP user name", "The user name FTP clients log in with.", ftp.UserName,
                                         value => { ftp.SetUserName(value); done(); }),
                },
                new BasicSetting
                {
                    Title = "FTP password",
                    Description = "The password FTP clients log in with",
                    Since = 1,
                    Value = () => ftp.Password,
                    Shown = () => ftp.AutoStart,
                    Change = done => Ask(context, "FTP password", "The password FTP clients log in with.", ftp.Password,
                                         value => { ftp.SetPassword(value); done(); }),
                },
            };
        }

        /// <summary>The welcome version: the newest setting's.</summary>
        public static int Version(DashboardContext context)
        {
            return All(context).Max(s => s.Since);
        }

        /// <summary>
        /// The welcome version the user has seen: 0 for settings kept from
        /// before there was a welcome screen, -1 on a new install.
        /// </summary>
        public static int Seen
        {
            get { return Settings.GetInt(SeenSetting, Settings.WasEmpty ? -1 : 0); }
        }

        /// <summary>Whether the welcome screen is due: on a new install, or with settings the user hasn't seen.</summary>
        public static bool WelcomeDue(DashboardContext context)
        {
            return Settings.IsLoaded && Seen < Version(context);
        }

        public static void MarkSeen(DashboardContext context)
        {
            Settings.Set(SeenSetting, Version(context).ToString());
        }

        /// <summary>A setting newer than the welcome version seen; nothing is new on a new install.</summary>
        public static bool IsNew(BasicSetting setting, int seen)
        {
            return seen >= 0 && setting.Since > seen;
        }

        /// <summary>Starts the FTP server, saying so when it can't.</summary>
        public static void StartFtp(DashboardContext context)
        {
            try
            {
                context.Ftp.Start();
            }
            catch (Exception e)
            {
                context.ShowStatus("The FTP server didn't start: " + e.Message);
            }
        }

        // The console's keyboard; changed is called with what was typed,
        // unless it was cancelled or left empty.
        private static void Ask(DashboardContext context, string title, string description, string text,
                                Action<string> changed)
        {
            SoftwareKeyboardInput keyboard = new SoftwareKeyboardInput
            {
                Title = title,
                Description = description,
                DefaultText = text,
            };
            keyboard.Completed += delegate(object sender, SoftwareKeyboardInputCompletedEventArgs e)
            {
                string typed = e.Error == null && !e.Cancelled && e.Result != null ? e.Result.Trim() : "";
                if (typed.Length > 0)
                {
                    Background.OnUi(() => changed(typed));
                }
            };
            try
            {
                keyboard.OpenAsync(PlayerIndex.One);
            }
            catch (Exception e)
            {
                Log.Write("The keyboard didn't open: {0}", e.Message);
                context.ShowStatus("The keyboard didn't open: " + e.Message);
            }
        }
    }
}
