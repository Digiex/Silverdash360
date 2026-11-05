using System;
using System.Collections.Generic;
using System.Linq;
using Silverdash360.Core;
using Silverdash360.Native;

namespace Silverdash360.Services
{
    /// <summary>How a console setting takes effect once it's changed.</summary>
    public enum SettingEffect
    {
        /// <summary>At once.</summary>
        Now,
        /// <summary>When Silverdash360 starts again, which it does at once.</summary>
        RestartDashboard,
        /// <summary>
        /// When Silverdash360 starts again, which it does at once, and kept
        /// only if confirmed then: the display may show nothing.
        /// </summary>
        TestDisplay,
        /// <summary>When the console restarts, which it does at once.</summary>
        RestartConsole,
        /// <summary>The next time the console starts.</summary>
        NextStart,
        /// <summary>A field of a page's own, taken when the page is done (Date and Time's).</summary>
        Draft,
    }

    /// <summary>A choice of a console setting: what it's called and the value stored.</summary>
    public sealed class SettingChoice
    {
        public string Name;
        public uint Value;
        /// <summary>Shown greyed and not taken, as a resolution the display doesn't take.</summary>
        public bool Unavailable;
        /// <summary>The text beside the setting when this is chosen, if not its name.</summary>
        public string Label;
        /// <summary>What A does in place of taking the value, as Custom... asks for a name.</summary>
        public Action<ISettingHost> Run;
        /// <summary>For a resolution: interlaced, as 1080i.</summary>
        public bool Interlaced;
    }

    /// <summary>What a setting's action asks of the page showing it.</summary>
    public interface ISettingHost
    {
        /// <summary>The console's keyboard; done is called with what was typed, unless it was cancelled.</summary>
        void Ask(string title, string description, string text, Action<string> done);
        void Status(string text);
        /// <summary>Shows the settings again, as they are now.</summary>
        void Refresh();
        void Back();
        /// <summary>The Xbox Dashboard's message box: yes is called on Yes, nothing on No.</summary>
        void Confirm(string title, string text, Action yes);
    }

    /// <summary>
    /// An item of the Xbox Dashboard's Console Settings: a setting with
    /// choices, an action (<see cref="Run"/>) or a page of its own
    /// (<see cref="Opens"/>).
    /// </summary>
    public sealed class ConsoleSetting
    {
        /// <summary>The page it's on: one of <see cref="ConsoleSettings.Categories"/>, or a page an item opens.</summary>
        public string Group;
        public string Title;
        public string Description;
        public SettingEffect Effect;
        public List<SettingChoice> Choices;
        /// <summary>The value stored now.</summary>
        public Func<uint> Read;
        /// <summary>Stores a choice's value and does what takes it into use at once, if anything.</summary>
        public Action<uint> Write;
        /// <summary>The text beside it, when that isn't a choice's name.</summary>
        public Func<string> Value;
        /// <summary>Why it can't be changed now, or null when it can.</summary>
        public Func<string> Disabled;
        /// <summary>The page it opens.</summary>
        public string Opens;
        /// <summary>Done first when its page opens.</summary>
        public Action OnOpen;
        /// <summary>What A does, for an item that isn't a choice.</summary>
        public Action<ISettingHost> Run;
        /// <summary>A question asked before a choice is taken, as the Xbox Dashboard's restart questions.</summary>
        public string Confirm;

        public SettingChoice Current()
        {
            if (Choices == null || Read == null)
            {
                return null;
            }
            uint value = Read();
            return Choices.FirstOrDefault(c => c.Value == value);
        }

        public string ValueText()
        {
            if (Value != null)
            {
                return Value();
            }
            if (Choices == null)
            {
                return "";
            }
            SettingChoice current = Current();
            return current == null ? "Other" : current.Label ?? current.Name;
        }

        /// <summary>For HDTV Settings: the AV pack and its screen size setting, which a trial tries timings for.</summary>
        public uint ResolutionPack = uint.MaxValue;
    }

    /// <summary>
    /// The Xbox Dashboard's Console Settings (2.0.17559), its pages, items
    /// and choices in its order, read and written as it does: XConfig
    /// settings in the user (3) and Xbox (7) categories, the video driver's
    /// options, the speaker setup, the clock and the system controller.
    /// The Xbox Dashboard resets its own display to take a video setting into
    /// use; Silverdash360 starts again instead. Connected Devices, System Info
    /// and Console Name, 3D aside the rest of what it has, aren't here.
    /// </summary>
    public static class ConsoleSettings
    {
        private const ushort User = XConfig.UserCategory;
        private const ushort Xbox = XConfig.XboxCategory;

        // User settings.
        private const ushort ZoneBias = 0x01;
        private const ushort ZoneStandardName = 0x02;
        private const ushort ZoneDaylightName = 0x03;
        private const ushort ZoneStandardDate = 0x04;
        private const ushort ZoneDaylightDate = 0x05;
        private const ushort ZoneStandardBias = 0x06;
        private const ushort ZoneDaylightBias = 0x07;
        private const ushort LanguageSetting = 0x09;
        private const ushort VideoFlags = 0x0A;
        private const ushort AudioFlags = 0x0B;
        private const ushort RetailFlags = 0x0C;
        private const ushort Country = 0x0E;
        private const ushort HdmiSize = 0x13;
        private const ushort ComponentSize = 0x14;
        private const ushort VgaSize = 0x15;
        private const ushort ReferenceLevels = 0x29;
        private const ushort VideoOptions = 0x2C;

        // Xbox settings.
        private const ushort ScreenSaver = 0x01;
        private const ushort AutoOff = 0x02;
        private const ushort AutoPlay = 0x06;
        private const ushort Keyboard = 0x07;
        private const ushort XboxFlags = 0x0B;
        private const ushort TvSize = 0x0D;

        // Video flags.
        private const uint Widescreen = 0x10000;
        private const uint Letterbox = 0x100000;
        private const uint Pal60 = 0x400000;

        // Retail flags.
        private const uint DaylightSavingOff = 0x2;
        private const uint Clock24 = 0x8;
        private const uint SoundEffectsMuted = 0x100;
        private const uint StartupDisc = 0x2000;
        private const uint StartupMediaCenter = 0x20000;
        private const uint BackgroundDownloads = 0x10000;

        // Xbox flags.
        private const uint RatingsSound = 0x1;
        private const uint AutoDownloads = 0x8;

        // AV packs (HalGetCurrentAVPack).
        private const uint PackComponentSd = 0;
        private const uint PackOther = 2;
        private const uint PackComponent = 3;
        private const uint PackHdmiAudio = 4;
        private const uint PackVga = 6;
        private const uint PackHdmi = 8;

        public const string Display = "Display";
        public const string Audio = "Audio";
        public const string LanguageAndLocale = "Language and Locale";
        public const string Clock = "Clock";
        public const string StartupAndShutdown = "Startup and Shutdown";
        public const string AutoPlayPage = "Auto-Play";
        public const string AutoDownloadsPage = "Auto-Downloads";
        public const string RemoteControl = "Remote Control";
        public const string DateAndTime = "Date and Time";
        public const string TvSizePage = "TV Size";
        public const string ConsoleNamePage = "Console Name";
        public const string SystemInfoPage = "System Info";

        /// <summary>The top of Console Settings, in the Xbox Dashboard's order (0x9201EDB8), with what each is for.</summary>
        public static readonly KeyValuePair<string, string>[] Categories =
        {
            new KeyValuePair<string, string>(Display, "Set the display to match your TV."),
            new KeyValuePair<string, string>(Audio, "Change audio output settings and mute sound effects."),
            new KeyValuePair<string, string>(LanguageAndLocale, "Choose the console's language and locale."),
            new KeyValuePair<string, string>(Clock, "Set the date, time, time format and time zone."),
            new KeyValuePair<string, string>(StartupAndShutdown, "Choose what the console starts, when it turns itself off and background downloads."),
            new KeyValuePair<string, string>(AutoPlayPage, "Choose whether discs and media start when they're put in."),
            new KeyValuePair<string, string>(AutoDownloadsPage, "Choose whether updates download by themselves."),
            new KeyValuePair<string, string>(RemoteControl, "Choose which remotes control the console."),
            new KeyValuePair<string, string>(SystemInfoPage, "View your console's system information."),
            new KeyValuePair<string, string>(ConsoleNamePage, "Name this console, as it shows on your network."),
            new KeyValuePair<string, string>(NetworkSettings.Page, "Set up and test your network connection."),
        };

        // The Xbox Dashboard's names, in its order (dashSysCslSetConsoleName.xur).
        private static readonly string[] ConsoleNames = { "Xbox 360", "Living Room", "Family Room", "Game Room", "Bedroom" };
        private const uint CustomName = 0xFF;

        // The Xbox Dashboard's resolution tables (dash 17559, 0x92020164 and
        // 0x920201C0): width and height, a negative height for 1080p.
        private static readonly SettingChoice[] HdtvModes =
        {
            Mode("480p", 640, 480), Mode("720p", 1280, 720), Mode("1080i", 1920, 1080, true), Mode("1080p", 1920, -1080),
        };

        private static readonly SettingChoice[] VgaModes =
        {
            Mode("640 x 480", 640, 480), Mode("848 x 480", 848, 480), Mode("1024 x 768", 1024, 768),
            Mode("1280 x 720", 1280, 720), Mode("1280 x 768", 1280, 768), Mode("1280 x 1024", 1280, 1024),
            Mode("1360 x 768", 1360, 768), Mode("1440 x 900", 1440, 900), Mode("1680 x 1050", 1680, 1050),
            Mode("1920 x 1080", 1920, 1080),
        };

        private static SettingChoice Mode(string name, int width, int height, bool interlaced = false)
        {
            return new SettingChoice { Name = name, Value = ((uint)width << 16) | (ushort)(short)height, Interlaced = interlaced };
        }

        private static SettingChoice Choice(string name, uint value)
        {
            return new SettingChoice { Name = name, Value = value };
        }

        private static List<SettingChoice> Choices(params SettingChoice[] choices)
        {
            return choices.ToList();
        }

        /// <summary>Every item of every page this console has, for its cable, display and region.</summary>
        public static List<ConsoleSetting> All()
        {
            List<ConsoleSetting> all = new List<ConsoleSetting>();
            AddDisplay(all);
            AddAudio(all);
            AddLanguageAndLocale(all);
            AddClock(all);
            AddStartupAndShutdown(all);
            all.Add(new ConsoleSetting
            {
                Group = AutoPlayPage, Title = "Auto-Play",
                Description = "Start a disc, music or video when it's put in",
                Choices = Choices(Choice("Enable", 0), Choice("Disable", 1)),
                Read = () => XConfig.GetU16(Xbox, AutoPlay) == 1 ? 1u : 0u,
                Write = v => XConfig.SetU16(Xbox, AutoPlay, (ushort)v),
            });
            all.Add(new ConsoleSetting
            {
                Group = AutoDownloadsPage, Title = "Auto-Downloads",
                Description = "Download updates for your games and apps by themselves",
                Choices = Choices(Choice("On", AutoDownloads), Choice("Off", 0)),
                Read = () => XConfig.GetU32(Xbox, XboxFlags) & AutoDownloads,
                Write = v => XConfig.SetBits(Xbox, XboxFlags, AutoDownloads, v),
            });
            all.Add(new ConsoleSetting
            {
                Group = RemoteControl, Title = "Remote Control",
                Description = "Which remotes control the console",
                Choices = Choices(Choice("Both Remotes", 1), Choice("Xbox 360 Media Remote", 0xF)),
                // Kept in the system controller: asked with 0x16, set with 0x95.
                Read = () => (uint)(XConfig.Smc(0x16)[1] & 0xF),
                Write = v => XConfig.Smc(0x95, (byte)v),
            });
            // XamGet/SetConsoleFriendlyName, 23 characters at most; a name
            // that's none of the list is Custom... (dash 17559, 0x92280558).
            List<SettingChoice> names = ConsoleNames.Select((n, i) => Choice(n, (uint)i)).ToList();
            names.Add(new SettingChoice
            {
                Name = "Custom...", Value = CustomName,
                Run = host => host.Ask("Console Name", "Enter a name for this console.", XConfig.ConsoleName(), typed =>
                {
                    XConfig.SetConsoleName(typed);
                    Log.Write("Console named {0}", typed);
                    host.Status("Console Name: " + typed + ".");
                    host.Back();
                }),
            });
            all.Add(new ConsoleSetting
            {
                Group = ConsoleNamePage, Title = "Console Name",
                Description = "The console's name, as it shows on your network",
                Choices = names,
                Value = XConfig.ConsoleName,
                Read = () =>
                {
                    int preset = Array.IndexOf(ConsoleNames, XConfig.ConsoleName());
                    return preset >= 0 ? (uint)preset : CustomName;
                },
                Write = v => XConfig.SetConsoleName(ConsoleNames[v]),
            });
            foreach (KeyValuePair<string, string> line in SystemInfo.Lines())
            {
                string value = line.Value;
                all.Add(new ConsoleSetting { Group = SystemInfoPage, Title = line.Key, Value = () => value, Run = host => { } });
            }
            NetworkSettings.Add(all);
            return all;
        }

        private static void AddDisplay(List<ConsoleSetting> all)
        {
            uint pack = XConfig.AVPack();
            bool hdmi = pack == PackHdmi || pack == PackHdmiAudio;
            bool digital = hdmi || pack == PackVga;
            byte[] discovery = XConfig.DisplayDiscovery();
            // The Xbox Dashboard shows TV Size, with 3D TV, only for a TV that
            // takes 3D: bit 0 at 4 is the 3D_present bit of the TV's HDMI
            // vendor block (kernel 17559, 0x800C3D40), and the size is for
            // 3D. It's here for any TV that gives its size too.
            bool takes3D = discovery != null && (BigEndian.U32(discovery, 4) & 1) != 0;
            bool tvTells = takes3D || (discovery != null && DetectedTvSize(discovery) != 0);
            bool colorSpaces = discovery != null && BigEndian.U32(discovery, 0x1D) == 3;
            // The AV region: 1 NTSC-M, 2 NTSC-J, 3 PAL.
            uint region = (XConfig.GetU32(XConfig.ConsoleCategory, 2) >> 8) & 0xFF;

            ushort sizeSetting = hdmi ? HdmiSize : pack == PackVga ? VgaSize
                : pack == PackComponent || pack == PackComponentSd ? ComponentSize : (ushort)0;
            if (sizeSetting != 0)
            {
                bool hdtv;
                List<SettingChoice> modes = Resolutions(pack, discovery, out hdtv);
                all.Add(new ConsoleSetting
                {
                    Group = Display, Title = hdtv ? "HDTV Settings" : "Screen Resolution",
                    Description = "Select a high definition output setting. When you change it, you'll be asked if you can " +
                                  "see a picture. If you don't answer within 10 seconds, the console goes back to its original setting",
                    Effect = SettingEffect.TestDisplay,
                    Choices = modes,
                    Read = () => XConfig.GetU32(User, sizeSetting),
                    Write = v => XConfig.SetU32(User, sizeSetting, v),
                    ResolutionPack = pack,
                });
            }
            all.Add(new ConsoleSetting
            {
                Group = Display, Title = "Screen Format",
                Description = "Widescreen for a 16:9 TV",
                Effect = SettingEffect.RestartDashboard,
                Choices = Choices(Choice("Normal", 0), Choice("Widescreen", Widescreen)),
                Read = () => XConfig.GetU32(User, VideoFlags) & Widescreen,
                Write = v => XConfig.SetBits(User, VideoFlags, Widescreen | Letterbox, v),
                Disabled = () => sizeSetting != 0 && IsWideOnly(XConfig.GetU32(User, sizeSetting))
                    ? "This resolution is widescreen." : null,
            });
            if (digital || region == 1)
            {
                // Two bits a cable, HDMI's in the fifth pair.
                int shift = 2 * (int)(pack == PackHdmi ? 4 : pack);
                all.Add(new ConsoleSetting
                {
                    Group = Display, Title = digital ? "Reference Levels" : "Black Level",
                    Description = "How dark black is: Expanded for a display that shows the whole range",
                    Effect = SettingEffect.RestartDashboard,
                    Choices = Choices(Choice("Standard", 3), Choice("Intermediate", 2), Choice(digital ? "Expanded" : "Extended", 1)),
                    Read = () =>
                    {
                        uint level = (XConfig.GetU32(User, ReferenceLevels) >> shift) & 3;
                        return level == 0 ? 3 : level;
                    },
                    Write = v => XConfig.SetBits(User, ReferenceLevels, 3u << shift, v << shift),
                });
            }
            if (region != 1 && region != 2)
            {
                all.Add(new ConsoleSetting
                {
                    Group = Display, Title = "PAL Settings",
                    Description = "PAL-60 for a TV that shows 60 Hz, PAL-50 otherwise. Kept only if you say so",
                    Effect = SettingEffect.TestDisplay,
                    Choices = Choices(Choice("PAL-60", Pal60), Choice("PAL-50", 0)),
                    Read = () => XConfig.GetU32(User, VideoFlags) & Pal60,
                    Write = v => XConfig.SetBits(User, VideoFlags, Pal60, v),
                });
            }
            if (colorSpaces)
            {
                all.Add(new ConsoleSetting
                {
                    Group = Display, Title = "HDMI Color Space",
                    Description = "How colours are sent over HDMI. Auto lets the display and the console agree",
                    Effect = SettingEffect.RestartDashboard,
                    Choices = Choices(Choice("Auto", 7), Choice("Source", 6), Choice("RGB", 0), Choice("YCbCr709", 2), Choice("YCbCr601", 1)),
                    Read = () => XConfig.GetU32(User, VideoOptions) & 7,
                    Write = v => XConfig.SetVideoOption(1, v),
                });
            }
            if (hdmi)
            {
                all.Add(new ConsoleSetting
                {
                    Group = Display, Title = "Display Discovery",
                    Description = "The console asks the display what it can show",
                    Effect = SettingEffect.RestartConsole,
                    Confirm = "Your console needs to restart to use the new display discovery setting. After your console " +
                              "restarts, you can choose your new digital audio and HDTV settings.",
                    Choices = Choices(Choice("Display Discovery Enabled", 0), Choice("Display Discovery Disabled", 1)),
                    Read = () => (XConfig.GetU32(User, VideoOptions) >> 3) & 1,
                    Write = v =>
                    {
                        XConfig.SetVideoOption(3, v);
                        if (v == 1)
                        {
                            // As the Xbox Dashboard: 640 x 480, normal, digital stereo.
                            XConfig.SetU32(User, HdmiSize, HdtvModes[0].Value);
                            XConfig.SetU32(User, VideoFlags, 0);
                            WriteDigitalOutput(0);
                        }
                    },
                });
            }
            all.Add(new ConsoleSetting
            {
                Group = Display, Title = "Screen Saver",
                Description = "Dim the screen after 10 minutes without a press",
                Choices = Choices(Choice("Screen Saver Enabled", 0x000A), Choice("Screen Saver Disabled", 0x1000)),
                // Nothing stored yet is on.
                Read = () => XConfig.GetU16(Xbox, ScreenSaver) == 0x1000 ? 0x1000u : 0x000Au,
                Write = v =>
                {
                    XConfig.SetU16(Xbox, ScreenSaver, (ushort)v);
                    XConfig.ApplyInactivityTime(false);
                },
            });
            if (takes3D)
            {
                all.Add(new ConsoleSetting
                {
                    Group = Display, Title = "3D TV",
                    Description = "Let games show 3D on your 3D TV",
                    Effect = SettingEffect.RestartConsole,
                    Confirm = "Your console needs to restart to use the new 3D TV setting.\n\nRestart now?",
                    Choices = Choices(Choice("3D Enabled", 0), Choice("3D Disabled", 1)),
                    // Video option 5, bit 21 of user setting 0x2C: 1 is off.
                    Read = () => (XConfig.GetU32(User, VideoOptions) >> 21) & 1,
                    Write = v => XConfig.SetVideoOption(5, v),
                });
            }
            if (tvTells)
            {
                AddTvSize(all, discovery);
            }
        }

        // The resolutions the Xbox Dashboard offers for the cable and display
        // (dash 17559, 0x9227F858, 0x9227EF18 and the VdEnumerateVideoModes
        // callback 0x9227E398). Component: the HDTV list, all of it. HDMI,
        // DVI and VGA by what the display says (the word at 0x1D): a PC
        // monitor, the VGA list, all of it; an HDMI TV, the HDTV list, with
        // what the display takes not greyed; a DVI one, only what it takes.
        // The display's own best mode heads it as Optimal Resolution, and
        // others it takes, at 60 Hz and in the VGA list's sizes, follow by
        // width.
        private static List<SettingChoice> Resolutions(uint pack, byte[] discovery, out bool hdtv)
        {
            hdtv = true;
            if (pack == PackComponent || pack == PackComponentSd)
            {
                return HdtvModes.ToList();
            }
            uint sink = discovery != null ? BigEndian.U32(discovery, 0x1D) : 0;
            bool hdmiSink = discovery != null && (BigEndian.U32(discovery, 4) & 0x100) != 0;
            if (sink == 1 || (pack == PackVga && sink != 2 && sink != 3))
            {
                hdtv = false;
                return VgaModes.ToList();
            }
            if (sink != 2 && sink != 3)
            {
                // The display said nothing: the HDTV list as for component.
                return HdtvModes.ToList();
            }
            hdtv = hdmiSink;
            List<SettingChoice> list = hdmiSink
                ? HdtvModes.Select(m => new SettingChoice { Name = m.Name, Value = m.Value, Interlaced = m.Interlaced, Unavailable = true }).ToList()
                : new List<SettingChoice>();
            int fixedCount = list.Count;
            foreach (XConfig.VideoMode mode in XConfig.VideoModes())
            {
                if (list.Count >= 20)
                {
                    break;
                }
                if (Math.Abs((int)mode.Refresh - 60) > 1 || (mode.Interlaced && !hdmiSink) ||
                    !VgaModes.Any(m => (m.Value >> 16) == mode.Width && (short)m.Value == mode.Height))
                {
                    continue;
                }
                int height = mode.Height == 1080 && !mode.Interlaced ? -1080 : mode.Height;
                uint value = ((uint)mode.Width << 16) | (ushort)(short)height;
                if (mode.Preferred)
                {
                    list.Insert(0, new SettingChoice
                    {
                        Name = "Optimal Resolution", Value = value, Label = string.Format("{0} x {1}", mode.Width, mode.Height),
                        Interlaced = mode.Interlaced,
                    });
                    fixedCount++;
                }
                SettingChoice same = list.Skip(mode.Preferred ? 1 : 0).FirstOrDefault(c => c.Value == value);
                if (same != null)
                {
                    same.Unavailable = false;
                    continue;
                }
                int at = fixedCount;
                while (at < list.Count && (list[at].Value >> 16) <= (uint)mode.Width)
                {
                    at++;
                }
                list.Insert(at, new SettingChoice
                {
                    Name = string.Format("{0} x {1}", mode.Width, mode.Height), Value = value, Interlaced = mode.Interlaced,
                });
            }
            return list;
        }

        private static bool IsWideOnly(uint mode)
        {
            return HdtvModes.Skip(1).Any(m => m.Value == mode);
        }

        // TV Size: the screen's diagonal in cm (Xbox setting 0xD, 0 for none
        // set), entered in inches or centimetres or worked out from what the
        // display says (dash 17559, 0x9228B1C0).
        private static bool tvSizeInInches = true;

        private static void AddTvSize(List<ConsoleSetting> all, byte[] discovery)
        {
            Func<string> size = () =>
            {
                uint cm = XConfig.GetU16(Xbox, TvSize);
                return cm == 0 ? "Not set" : string.Format("{0}\" ({1} cm)", (uint)(cm * 0.3937008 + 0.5), cm);
            };
            all.Add(new ConsoleSetting
            {
                Group = Display, Title = "TV Size",
                Description = "Your TV's size, for games that use it",
                Opens = TvSizePage,
                Value = size,
                // A size not set yet is worked out when the page opens.
                OnOpen = () =>
                {
                    ushort detected = DetectedTvSize(discovery);
                    if (XConfig.GetU16(Xbox, TvSize) == 0 && detected != 0)
                    {
                        XConfig.SetU16(Xbox, TvSize, detected);
                    }
                },
            });
            all.Add(new ConsoleSetting
            {
                Group = TvSizePage, Title = "Enter TV Size",
                Description = "The size from one corner of the screen to the opposite one",
                Value = size,
                Run = host =>
                {
                    uint cm = XConfig.GetU16(Xbox, TvSize);
                    uint shown = Math.Min(9999, tvSizeInInches ? (uint)(cm * 0.3937008 + 0.5) : cm);
                    host.Ask("TV Size", tvSizeInInches ? "Enter your screen size in inches." : "Enter your screen size in centimeters.",
                             shown == 0 ? "" : shown.ToString(), typed =>
                    {
                        uint entered;
                        if (!uint.TryParse(typed, out entered) || entered == 0 || entered > 9999)
                        {
                            host.Status("Enter the size as a number.");
                            return;
                        }
                        XConfig.SetU16(Xbox, TvSize, (ushort)Math.Min(9999, tvSizeInInches ? (uint)(entered * 2.54 + 0.5) : entered));
                        host.Refresh();
                    });
                },
            });
            all.Add(new ConsoleSetting
            {
                Group = TvSizePage, Title = "Units",
                Description = "What Enter TV Size takes",
                Effect = SettingEffect.Draft,
                Choices = Choices(Choice("Inches (in)", 1), Choice("Centimeters (cm)", 0)),
                Read = () => tvSizeInInches ? 1u : 0u,
                Write = v => tvSizeInInches = v == 1,
            });
            all.Add(new ConsoleSetting
            {
                Group = TvSizePage, Title = "Detect Size",
                Description = "Ask the TV its size",
                Run = host =>
                {
                    ushort detected = DetectedTvSize(XConfig.DisplayDiscovery());
                    if (detected == 0)
                    {
                        host.Status("Your TV doesn't say its size.");
                        return;
                    }
                    XConfig.SetU16(Xbox, TvSize, detected);
                    host.Refresh();
                },
            });
        }

        // The diagonal from the width and height the display gives in cm
        // (bytes 0x15 and 0x16), 0 when it doesn't give them.
        private static ushort DetectedTvSize(byte[] discovery)
        {
            if (discovery == null || discovery[0x15] == 0 || discovery[0x16] == 0)
            {
                return 0;
            }
            double width = discovery[0x15];
            double height = discovery[0x16];
            return (ushort)Math.Sqrt(width * width + height * height);
        }

        private static void AddAudio(List<ConsoleSetting> all)
        {
            if (XConfig.AVPack() != PackOther)
            {
                all.Add(new ConsoleSetting
                {
                    Group = Audio, Title = "Digital Output",
                    Description = "What goes out over HDMI and the optical audio port",
                    Choices = Choices(Choice("Digital Stereo", 0), Choice("Dolby Digital 5.1", 0x10000), Choice("Dolby Digital with WMA Pro", 0x30000)),
                    Read = () => AudioFlagsInUse() & 0x30000,
                    Write = WriteDigitalOutput,
                });
            }
            all.Add(new ConsoleSetting
            {
                Group = Audio, Title = "Sound Effects",
                Description = "The console's own sounds",
                Choices = Choices(Choice("Sound Effects Enabled", 0), Choice("Sound Effects Disabled", SoundEffectsMuted)),
                Read = () => XConfig.GetU32(User, RetailFlags) & SoundEffectsMuted,
                Write = v =>
                {
                    XConfig.SetBits(User, RetailFlags, SoundEffectsMuted, v);
                    XConfig.MuteSound(v != 0);
                },
            });
            all.Add(new ConsoleSetting
            {
                Group = Audio, Title = "Ratings Collection Sound",
                Description = "The background sound for TV ratings collection. Sign in to Xbox Live for this to take effect",
                Choices = Choices(Choice("Background Sound On", RatingsSound), Choice("Background Sound Off", 0)),
                Read = () => XConfig.GetU32(Xbox, XboxFlags) & RatingsSound,
                Write = v => XConfig.SetBits(Xbox, XboxFlags, RatingsSound, v),
            });
        }

        // The audio flags as the system uses them: nothing stored is Dolby
        // Digital with Dolby Pro Logic II (XGetAudioFlags).
        private static uint AudioFlagsInUse()
        {
            uint flags = XConfig.GetU32(User, AudioFlags);
            return flags == 0 ? 0x00010001 : flags;
        }

        // Digital output in bits 16 and 17, the analog setting kept, and the
        // speakers set up at once (dash 17559, 0x92283970).
        private static void WriteDigitalOutput(uint output)
        {
            uint flags = (AudioFlagsInUse() & ~0x30000u) | output;
            XConfig.SetU32(User, AudioFlags, flags);
            XConfig.SetSpeakerConfig(flags);
        }

        private static void AddLanguageAndLocale(List<ConsoleSetting> all)
        {
            // The list depends on the game region (0x92285E40), not the locale.
            uint gameRegion = XConfig.GameRegion();
            int set = (gameRegion & 0xFF00) != 0x100 ? 0 : gameRegion == 0x101 ? 1 : gameRegion == 0x102 ? 3 : 2;
            ConsoleSettingsTables.Language[] languages = ConsoleSettingsTables.Languages[set];
            all.Add(new ConsoleSetting
            {
                Group = LanguageAndLocale, Title = "Language",
                Description = "The console's language: the Guide's and the games'. Silverdash360 stays in English",
                // An input method variant: its place in the list with a bit
                // to tell it from the plain language.
                Choices = languages.Select((l, i) => Choice(l.Name, l.Keyboard != 0 ? 0x80000000u | (uint)i : l.Value)).ToList(),
                Read = () =>
                {
                    uint language = XConfig.GetU32(User, LanguageSetting);
                    ushort keyboard = XConfig.GetU16(Xbox, Keyboard);
                    for (int i = 0; i < languages.Length; i++)
                    {
                        if (languages[i].Keyboard != 0 && languages[i].Value == language && languages[i].Keyboard == keyboard)
                        {
                            return 0x80000000u | (uint)i;
                        }
                    }
                    return language;
                },
                Write = v =>
                {
                    ConsoleSettingsTables.Language chosen = (v & 0x80000000) != 0
                        ? languages[v & 0xFF] : languages.First(l => l.Value == v && l.Keyboard == 0);
                    XConfig.SetU32(User, LanguageSetting, chosen.Value);
                    if (chosen.Keyboard != 0)
                    {
                        XConfig.SetU16(Xbox, Keyboard, chosen.Keyboard);
                    }
                    XConfig.NotifyLanguageChanged();
                },
            });
            all.Add(new ConsoleSetting
            {
                Group = LanguageAndLocale, Title = "Locale",
                Description = "Locale affects Family Settings and wireless networking. Changing it isn't recommended after " +
                              "your wireless network has been set up, and it doesn't change the console's DVD region",
                Choices = ConsoleSettingsTables.Countries.Select(c => Choice(c.Key, (uint)c.Value)).ToList(),
                Read = () =>
                {
                    byte[] country = XConfig.Get(User, Country, 1);
                    return country != null ? country[0] : 0u;
                },
                Write = v =>
                {
                    XConfig.SetCountry(v);
                    XConfig.NotifyLanguageChanged();
                },
            });
        }

        private static void AddClock(List<ConsoleSetting> all)
        {
            Func<string> liveSet = () => XConfig.ClockSetByLive()
                ? "When connected to Xbox Live, the clock is set automatically and can't be changed." : null;
            all.Add(new ConsoleSetting
            {
                Group = Clock, Title = "Date and Time",
                Description = "Set the console's date and time",
                Opens = DateAndTime,
                Value = () => LocalNow().ToString(Clock24Hour() ? "d MMM yyyy  HH:mm" : "d MMM yyyy  h:mm tt"),
                Disabled = liveSet,
                OnOpen = () => draft = LocalNow(),
            });
            all.Add(new ConsoleSetting
            {
                Group = Clock, Title = "Time Format",
                Description = "How the console and games show the time",
                Choices = Choices(Choice("12-Hour Clock", 0), Choice("24-Hour Clock", Clock24)),
                Read = () => XConfig.GetU32(User, RetailFlags) & Clock24,
                Write = v => XConfig.SetBits(User, RetailFlags, Clock24, v),
            });
            all.Add(new ConsoleSetting
            {
                Group = Clock, Title = "Time Zone",
                Description = "Where the console is",
                Choices = ConsoleSettingsTables.Zones.Select((z, i) => Choice(z.Name, (uint)i)).ToList(),
                Read = CurrentZone,
                Write = v => SetZone(ConsoleSettingsTables.Zones[v]),
            });
            all.Add(new ConsoleSetting
            {
                Group = Clock, Title = "Daylight Saving",
                Description = "Whether the clock moves with daylight saving time in your time zone",
                Choices = Choices(Choice("Daylight Saving On", 0), Choice("Daylight Saving Off", DaylightSavingOff)),
                Read = () => XConfig.GetU32(User, RetailFlags) & DaylightSavingOff,
                Write = v => KeepingTheTime(() => XConfig.SetBits(User, RetailFlags, DaylightSavingOff, v)),
                Disabled = () =>
                {
                    uint zone = CurrentZone();
                    return zone < ConsoleSettingsTables.Zones.Length && !ConsoleSettingsTables.Zones[zone].UsesDaylightSaving
                        ? "This time zone doesn't use daylight saving time." : null;
                },
            });
            AddDateAndTime(all);
        }

        // Date and Time's fields, a date being set, taken by Set.
        private static DateTime draft;

        private static readonly string[] MonthNames =
        {
            "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December",
        };

        private static void AddDateAndTime(List<ConsoleSetting> all)
        {
            all.Add(Field("Day", Enumerable.Range(1, 31).Select(d => Choice(d.ToString(), (uint)d)), () => draft.Day,
                          v => draft = new DateTime(draft.Year, draft.Month, Math.Min((int)v, DateTime.DaysInMonth(draft.Year, draft.Month)),
                                                    draft.Hour, draft.Minute, draft.Second)));
            all.Add(Field("Month", MonthNames.Select((m, i) => Choice(m, (uint)i + 1)), () => draft.Month,
                          v => draft = new DateTime(draft.Year, (int)v, Math.Min(draft.Day, DateTime.DaysInMonth(draft.Year, (int)v)),
                                                    draft.Hour, draft.Minute, draft.Second)));
            // The Xbox Dashboard stops at 2025 (0x9294B9F0), which has gone.
            all.Add(Field("Year", Enumerable.Range(2005, 95).Select(y => Choice(y.ToString(), (uint)y)), () => draft.Year,
                          v => draft = new DateTime((int)v, draft.Month, Math.Min(draft.Day, DateTime.DaysInMonth((int)v, draft.Month)),
                                                    draft.Hour, draft.Minute, draft.Second)));
            bool clock24 = Clock24Hour();
            all.Add(Field("Hour", Enumerable.Range(0, 24).Select(h => Choice(HourName(h, clock24), (uint)h)), () => draft.Hour,
                          v => draft = draft.AddHours((int)v - draft.Hour)));
            all.Add(Field("Minute", Enumerable.Range(0, 60).Select(m => Choice(m.ToString("00"), (uint)m)), () => draft.Minute,
                          v => draft = draft.AddMinutes((int)v - draft.Minute)));
            all.Add(new ConsoleSetting
            {
                Group = DateAndTime, Title = "Set Date and Time",
                Description = "Set the clock to the date and time above",
                Value = () => draft.ToString(clock24 ? "d MMM yyyy  HH:mm" : "d MMM yyyy  h:mm tt"),
                Run = host =>
                {
                    try
                    {
                        SetLocalTime(draft);
                        SetLocalTime(draft);
                        XConfig.NoteSystemTime();
                        Log.Write("Console clock set to {0}", draft);
                        host.Status("The clock is set.");
                        host.Back();
                    }
                    catch (Exception e)
                    {
                        Log.Write("Console clock wasn't set: {0}", e);
                        host.Status("The clock wasn't set: " + e.Message);
                    }
                },
            });
        }

        private static string HourName(int hour, bool clock24)
        {
            return clock24 ? hour.ToString("00") : ((hour + 11) % 12 + 1) + (hour < 12 ? " AM" : " PM");
        }

        private static ConsoleSetting Field(string title, IEnumerable<SettingChoice> choices, Func<int> read, Action<uint> write)
        {
            return new ConsoleSetting
            {
                Group = DateAndTime, Title = title, Effect = SettingEffect.Draft,
                Choices = choices.ToList(), Read = () => (uint)read(), Write = write,
            };
        }

        private static bool Clock24Hour()
        {
            return (XConfig.GetU32(User, RetailFlags) & Clock24) != 0;
        }

        private static void AddStartupAndShutdown(List<ConsoleSetting> all)
        {
            all.Add(new ConsoleSetting
            {
                Group = StartupAndShutdown, Title = "Startup",
                Description = "What the console starts. A dashboard set up with DashLaunch starts in place of the Xbox Dashboard",
                Effect = SettingEffect.NextStart,
                Choices = Choices(Choice("Start Disc", StartupDisc), Choice("Start Xbox Dashboard", 0), Choice("Start Windows Media Center", StartupMediaCenter)),
                Read = () =>
                {
                    uint flags = XConfig.GetU32(User, RetailFlags);
                    return (flags & StartupDisc) != 0 ? StartupDisc : (flags & StartupMediaCenter) != 0 ? StartupMediaCenter : 0;
                },
                // The Xbox Dashboard's masks for each (dash 17559).
                Write = v => XConfig.ReadModifyWrite(User, RetailFlags,
                                                     v == StartupDisc ? 0xFFFDF77F : v == StartupMediaCenter ? 0xFFFFD77F : 0xFFFDD7FF, v),
            });
            all.Add(new ConsoleSetting
            {
                Group = StartupAndShutdown, Title = "Auto-Off",
                Description = "Turn the console off when nothing has been pressed or played for a while",
                Choices = Choices(Choice("Auto-Off After 1 Hour", 60), Choice("Auto-Off After 6 Hours", 360), Choice("Auto-Off Disabled", 0)),
                Read = () => (uint)(XConfig.GetU16(Xbox, AutoOff) & 0x7FFF),
                Write = v =>
                {
                    XConfig.SetU16(Xbox, AutoOff, (ushort)(0x8000 | v));
                    XConfig.ApplyInactivityTime(true);
                },
            });
            all.Add(new ConsoleSetting
            {
                Group = StartupAndShutdown, Title = "Background Downloads",
                Description = "Downloads go on while a game is played and while the console is off",
                Choices = Choices(Choice("Background Downloads On", BackgroundDownloads), Choice("Background Downloads Off", 0)),
                Read = () => XConfig.GetU32(User, RetailFlags) & BackgroundDownloads,
                Write = v => XConfig.SetBits(User, RetailFlags, BackgroundDownloads, v),
            });
        }

        // The time zone stored, as a place in the Xbox Dashboard's list, or
        // past its end when it's none of them.
        private static uint CurrentZone()
        {
            int bias = (int)XConfig.GetU32(User, ZoneBias);
            uint standardName = XConfig.GetU32(User, ZoneStandardName);
            uint daylightName = XConfig.GetU32(User, ZoneDaylightName);
            uint standardDate = XConfig.GetU32(User, ZoneStandardDate);
            uint daylightDate = XConfig.GetU32(User, ZoneDaylightDate);
            for (int i = 0; i < ConsoleSettingsTables.Zones.Length; i++)
            {
                ConsoleSettingsTables.Zone zone = ConsoleSettingsTables.Zones[i];
                if (zone.Bias == bias && zone.StandardName == standardName && zone.DaylightName == daylightName &&
                    zone.StandardDate == standardDate && zone.DaylightDate == daylightDate)
                {
                    return (uint)i;
                }
            }
            return uint.MaxValue;
        }

        // As the Xbox Dashboard (0x92282A78): the zone into user settings 1
        // to 7, daylight saving off for a zone without it, and the clock kept
        // at the same local time unless Xbox Live sets it.
        private static void SetZone(ConsoleSettingsTables.Zone zone)
        {
            KeepingTheTime(delegate
            {
                XConfig.SetU32(User, ZoneBias, (uint)zone.Bias);
                XConfig.SetU32(User, ZoneStandardName, zone.StandardName);
                XConfig.SetU32(User, ZoneDaylightName, zone.DaylightName);
                XConfig.SetU32(User, ZoneStandardDate, zone.StandardDate);
                XConfig.SetU32(User, ZoneDaylightDate, zone.DaylightDate);
                XConfig.SetU32(User, ZoneStandardBias, (uint)zone.StandardBias);
                XConfig.SetU32(User, ZoneDaylightBias, (uint)zone.DaylightBias);
                XConfig.SetBits(User, RetailFlags, DaylightSavingOff, zone.UsesDaylightSaving ? 0 : DaylightSavingOff);
            });
        }

        // A change to how local time is worked out, after which the clock
        // shows the local time it showed before, unless Xbox Live sets it.
        private static void KeepingTheTime(Action change)
        {
            DateTime local = LocalNow();
            change();
            if (!XConfig.ClockSetByLive())
            {
                SetLocalTime(local);
                SetLocalTime(local);
                XConfig.NoteSystemTime();
            }
        }

        /// <summary>The console's local time, by its time zone settings as the Xbox Dashboard works it out.</summary>
        public static DateTime LocalNow()
        {
            DateTime utc = DateTime.UtcNow;
            return new DateTime(utc.Ticks, DateTimeKind.Unspecified).AddMinutes(-BiasAt(utc));
        }

        // Sets the clock to a local time, with the bias in force now, as the
        // Xbox Dashboard's SetLocalTime (0x92811898).
        private static void SetLocalTime(DateTime local)
        {
            DateTime utc = new DateTime(local.Ticks, DateTimeKind.Utc).AddMinutes(BiasAt(DateTime.UtcNow));
            XConfig.SetSystemTime(utc);
        }

        // Minutes to add to local time for UTC at a time: the zone's bias,
        // with its daylight or standard bias by its transitions (0x9281A238),
        // unless daylight saving is off or the zone has none.
        private static int BiasAt(DateTime utc)
        {
            int bias = (int)XConfig.GetU32(User, ZoneBias);
            uint standardDate = XConfig.GetU32(User, ZoneStandardDate);
            uint daylightDate = XConfig.GetU32(User, ZoneDaylightDate);
            if ((XConfig.GetU32(User, RetailFlags) & DaylightSavingOff) != 0 || (standardDate >> 24) == 0 || (daylightDate >> 24) == 0)
            {
                return bias;
            }
            int standardBias = (int)XConfig.GetU32(User, ZoneStandardBias);
            int daylightBias = (int)XConfig.GetU32(User, ZoneDaylightBias);
            int year = utc.AddMinutes(-bias).Year;
            // Daylight time starts by standard time and ends by daylight time.
            DateTime starts = Transition(year, daylightDate).AddMinutes(bias + standardBias);
            DateTime ends = Transition(year, standardDate).AddMinutes(bias + daylightBias);
            DateTime now = new DateTime(utc.Ticks, DateTimeKind.Unspecified);
            bool daylight = starts < ends ? now >= starts && now < ends : now >= starts || now < ends;
            return bias + (daylight ? daylightBias : standardBias);
        }

        // A transition in a year: month, week (5 the last), day of the week
        // and hour, packed a byte each.
        private static DateTime Transition(int year, uint packed)
        {
            int month = (int)(packed >> 24);
            int week = (int)((packed >> 16) & 0xFF);
            int dayOfWeek = (int)((packed >> 8) & 0xFF);
            int hour = (int)(packed & 0xFF);
            DateTime first = new DateTime(year, month, 1);
            int day = 1 + ((dayOfWeek - (int)first.DayOfWeek + 7) % 7) + (week - 1) * 7;
            while (day > DateTime.DaysInMonth(year, month))
            {
                day -= 7;
            }
            return new DateTime(year, month, day, hour, 0, 0);
        }

        // A display setting on trial: kept in a file of the dashboard's,
        // written before it starts again, so the start in the new setting can
        // put the old one back if nobody confirms it. A resolution is tried as
        // the Xbox Dashboard tries it (dash 17559, 0x92280238): recorded with
        // VdSetDisplayModeOverride at timing 0, which the display takes up at
        // its next start (the Xbox Dashboard resets its display; Silverdash360
        // starts again); kept, the timing goes into the cable's two bits of
        // user setting 0x2B; turned down on HDMI, DVI or VGA, the next timing
        // is tried, until the kernel has none.
        private const string TrialFile = "display-trial.txt";
        private const ushort Timings = 0x2B;

        /// <summary>How long the start in a new display setting waits to be told to keep it.</summary>
        public const int TrialSeconds = 10;

        /// <summary>Changes a display setting on trial: the old value is kept to put back.</summary>
        public static void StartTrial(ConsoleSetting setting, SettingChoice choice)
        {
            uint old = setting.Read();
            if (setting.ResolutionPack != uint.MaxValue && !Override(choice.Value, choice.Interlaced, 0))
            {
                XConfig.ClearDisplayModeOverride();
                throw new InvalidOperationException("The console has no way to show " + choice.Name + ".");
            }
            setting.Write(choice.Value);
            SaveTrial(new Trial { Setting = setting, Old = old, New = choice.Value, Interlaced = choice.Interlaced, Timing = 0 });
            Log.Write("Console setting {0} on trial: {1:X8}, was {2:X8}", setting.Title, choice.Value, old);
        }

        private static bool Override(uint mode, bool interlaced, int timing)
        {
            int height = Math.Abs((short)mode);
            return XConfig.OverrideDisplayMode((int)(mode >> 16), height, interlaced, timing);
        }

        private static void SaveTrial(Trial trial)
        {
            AppStorage.WriteText(TrialFile, trial.Setting.Title + "\n" + trial.Old.ToString("X8") + "\n" + trial.New.ToString("X8") +
                                            "\n" + trial.Timing + "\n" + (trial.Interlaced ? 1 : 0));
        }

        /// <summary>The setting on trial and its old value, or null.</summary>
        public static Trial PendingTrial()
        {
            string text = AppStorage.ReadText(TrialFile);
            if (text == null)
            {
                return null;
            }
            string[] lines = text.Split('\n');
            ConsoleSetting setting = lines.Length >= 5 ? All().FirstOrDefault(s => s.Title == lines[0]) : null;
            if (setting == null)
            {
                AppStorage.Delete(TrialFile);
                return null;
            }
            return new Trial
            {
                Setting = setting, Old = Convert.ToUInt32(lines[1], 16), New = Convert.ToUInt32(lines[2], 16),
                Timing = int.Parse(lines[3]), Interlaced = lines[4].Trim() == "1",
            };
        }

        /// <summary>Keeps the setting on trial, and for a resolution the timing it was shown at.</summary>
        public static void KeepTrial(Trial trial)
        {
            uint pack = trial.Setting.ResolutionPack;
            if (pack != uint.MaxValue)
            {
                // Two bits a cable, HDMI's in the fifth pair (0x9227E858).
                int shift = 2 * (int)(pack == PackHdmi ? 4 : pack);
                XConfig.SetBits(User, Timings, 3u << shift, (uint)trial.Timing << shift);
            }
            AppStorage.Delete(TrialFile);
        }

        /// <summary>
        /// Turns the setting on trial down: the old value back, or for a
        /// resolution on HDMI, DVI or VGA the next timing to try, which is
        /// then on trial (true). Silverdash360 is then started again.
        /// </summary>
        public static bool RevertTrial(Trial trial)
        {
            trial.Setting.Write(trial.Old);
            uint pack = trial.Setting.ResolutionPack;
            if (pack != uint.MaxValue)
            {
                XConfig.ClearDisplayModeOverride();
                bool timings = pack == PackHdmi || pack == PackHdmiAudio || pack == PackVga;
                if (timings && Override(trial.New, trial.Interlaced, trial.Timing + 1))
                {
                    trial.Timing++;
                    trial.Setting.Write(trial.New);
                    SaveTrial(trial);
                    Log.Write("Console setting {0}: trying timing {1}", trial.Setting.Title, trial.Timing);
                    return true;
                }
                XConfig.ClearDisplayModeOverride();
            }
            AppStorage.Delete(TrialFile);
            Log.Write("Console setting {0} put back: {1:X8}", trial.Setting.Title, trial.Old);
            return false;
        }

        public sealed class Trial
        {
            public ConsoleSetting Setting;
            public uint Old;
            public uint New;
            public bool Interlaced;
            public int Timing;
        }
    }
}
