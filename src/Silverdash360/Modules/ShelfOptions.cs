using System;
using System.Collections.Generic;
using System.Linq;
using Silverdash360.Model;
using Silverdash360.Native;
using Silverdash360.Services;

namespace Silverdash360.Modules
{
    /// <summary>
    /// How a shelf of titles is shown: which kinds of title, from which
    /// storage, in what order, and as covers or a list. Each shelf keeps its
    /// own, in <see cref="Settings"/>.
    /// </summary>
    public sealed class ShelfOptions
    {
        public const string All = "all";
        /// <summary>
        /// What a shelf shows unless set otherwise: titles there's something
        /// to start, without those only saves or add-ons are left of.
        /// </summary>
        public const string Installed = "installed";

        public sealed class Choice
        {
            public string Key;
            public string Name;
        }

        private sealed class Kind
        {
            public string Key;
            public string Name;
            public Func<TitleEntry, bool> Matches;
        }

        private static readonly List<Kind> Kinds = new List<Kind>
        {
            new Kind { Key = Installed, Name = "Installed", Matches = t => t.IsInstalled },
            new Kind { Key = "360", Name = "Xbox 360 games", Matches = t => LaunchType(t) == ContentType.GameOnDemand || LaunchType(t) == ContentType.GameTitle },
            new Kind { Key = "arcade", Name = "Arcade", Matches = t => LaunchType(t) == ContentType.ArcadeTitle },
            new Kind { Key = "indie", Name = "Indie and XNA", Matches = t => LaunchType(t) == ContentType.Xna || LaunchType(t) == ContentType.CommunityGame },
            new Kind { Key = "demo", Name = "Demos", Matches = t => LaunchType(t) == ContentType.GameDemo },
            new Kind { Key = "xbox", Name = "Original Xbox", Matches = t => t.Content.Any(c => c.Type == ContentType.XboxOriginalGame) },
            new Kind { Key = "homebrew", Name = "Homebrew", Matches = t => t.LaunchItem == null && t.Executables.Count > 0 },
            new Kind { Key = "missing", Name = "Not installed", Matches = t => !t.IsInstalled },
        };

        public static readonly List<Choice> Sorts = new List<Choice>
        {
            new Choice { Key = "name", Name = "Name" },
            new Choice { Key = "size", Name = "Size" },
            new Choice { Key = "played", Name = "Last played" },
        };

        private static readonly Dictionary<uint, string> deviceNames = new Dictionary<uint, string>();
        private readonly string shelf;

        public ShelfOptions(string shelf)
        {
            this.shelf = shelf;
        }

        public string Shelf { get { return shelf; } }

        public string Show
        {
            get { return Settings.Get(shelf + ".show", Installed); }
            set { Settings.Set(shelf + ".show", value); }
        }

        public string Location
        {
            get { return Settings.Get(shelf + ".location", All); }
            set { Settings.Set(shelf + ".location", value); }
        }

        public string Sort
        {
            get { return Settings.Get(shelf + ".sort", "name"); }
            set { Settings.Set(shelf + ".sort", value); }
        }

        public bool ListView
        {
            get { return Settings.GetBool(shelf + ".list", false); }
            set { Settings.SetBool(shelf + ".list", value); }
        }

        public bool IsFiltered
        {
            get { return Show != Installed || Location != All; }
        }

        /// <summary>
        /// The kinds of title the shelf has: installed ones first, then
        /// everything, installed or not, then each kind.
        /// </summary>
        public List<Choice> ShowChoices(IEnumerable<TitleEntry> titles)
        {
            List<TitleEntry> list = titles.ToList();
            List<Choice> choices = new List<Choice>
            {
                new Choice { Key = Installed, Name = "Installed" },
                new Choice { Key = All, Name = "Everything" },
            };
            choices.AddRange(Kinds.Where(k => k.Key != Installed && list.Any(k.Matches))
                                  .Select(k => new Choice { Key = k.Key, Name = k.Name }));
            return choices;
        }

        /// <summary>The storage the shelf's titles are on, after "all".</summary>
        public List<Choice> LocationChoices(IEnumerable<TitleEntry> titles)
        {
            List<Choice> choices = new List<Choice> { new Choice { Key = All, Name = "All storage" } };
            choices.AddRange(titles.Select(t => LocationOf(t)).Where(l => l != null).Distinct()
                                   .Select(l => new Choice { Key = l, Name = LocationName(l) })
                                   .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase));
            return choices;
        }

        /// <summary>The shelf's titles, filtered and in order.</summary>
        public List<TitleEntry> Apply(IEnumerable<TitleEntry> titles)
        {
            IEnumerable<TitleEntry> shown = titles;
            Kind kind = Kinds.FirstOrDefault(k => k.Key == Show);
            if (kind != null)
            {
                shown = shown.Where(kind.Matches);
            }
            string location = Location;
            if (location != All)
            {
                shown = shown.Where(t => LocationOf(t) == location);
            }
            switch (Sort)
            {
                case "size":
                    return shown.OrderByDescending(t => t.TotalSize)
                                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
                case "played":
                    // Never played last, by name.
                    return shown.OrderBy(t => t.LastPlayed.HasValue ? 0 : 1)
                                .ThenByDescending(t => t.LastPlayed.HasValue ? t.LastPlayed.Value.Ticks : 0)
                                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
                default:
                    return shown.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        /// <summary>The choices, for the legend: such as "Arcade · Hard Drive · Size".</summary>
        public string Describe(IEnumerable<TitleEntry> titles)
        {
            List<string> parts = new List<string>();
            if (Show != All)
            {
                Choice show = ShowChoices(titles).FirstOrDefault(c => c.Key == Show);
                parts.Add(show != null ? show.Name : Show);
            }
            if (Location != All)
            {
                parts.Add(LocationName(Location));
            }
            parts.Add((Sorts.FirstOrDefault(s => s.Key == Sort) ?? Sorts[0]).Name);
            return string.Join("  ·  ", parts.ToArray());
        }

        private static uint LaunchType(TitleEntry title)
        {
            ContentItem launch = title.LaunchItem;
            return launch != null ? launch.Type : 0;
        }

        // A package's storage device, or an executable's drive; null when it
        // isn't installed.
        private static string LocationOf(TitleEntry title)
        {
            ContentItem launch = title.LaunchItem;
            if (launch != null)
            {
                return "device:" + launch.DeviceId.ToString("X8");
            }
            ExecutableItem executable = title.Executable;
            return executable != null ? "drive:" + executable.Drive : null;
        }

        private static string LocationName(string location)
        {
            if (location.StartsWith("drive:"))
            {
                string drive = location.Substring(6);
                int number;
                if (drive == "Hdd1")
                {
                    return "Hard drive";
                }
                if (drive.StartsWith("Usb") && int.TryParse(drive.Substring(3), out number))
                {
                    return "USB storage " + (number + 1);
                }
                return drive;
            }
            uint device;
            if (location.StartsWith("device:") &&
                uint.TryParse(location.Substring(7), System.Globalization.NumberStyles.HexNumber, null, out device))
            {
                return DeviceName(device);
            }
            return location;
        }

        // What the console calls a storage device, asked once each.
        private static string DeviceName(uint device)
        {
            string name;
            if (deviceNames.TryGetValue(device, out name))
            {
                return name;
            }
            name = "Storage " + device;
            try
            {
                using (NativeBuffer buffer = new NativeBuffer(2 * 64))
                {
                    if (Xam.XamContentGetDeviceName(device, buffer.Address, 64) == Xam.ErrorSuccess)
                    {
                        string read = buffer.Utf16(0, 64).Trim();
                        if (read.Length > 0)
                        {
                            name = read;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            deviceNames[device] = name;
            return name;
        }
    }
}
