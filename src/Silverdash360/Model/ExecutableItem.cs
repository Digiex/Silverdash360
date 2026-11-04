using System;

namespace Silverdash360.Model
{
    /// <summary>
    /// A title's executable found in a drive's Apps or Games folder, such as
    /// Hdd1\Games\Some Game\default.xex, rather than in a content package.
    /// </summary>
    public sealed class ExecutableItem
    {
        /// <summary>The path in the dashboard's file tree, such as /Hdd1/Games/Some Game/default.xex.</summary>
        public string Path { get; set; }

        /// <summary>The kernel's path for launching it.</summary>
        public string DevicePath { get; set; }

        /// <summary>The kernel's path of the folder it's in, mounted as its D: drive.</summary>
        public string DeviceFolder { get; set; }

        /// <summary>The folder's name, or the file's for one directly in Apps.</summary>
        public string Name { get; set; }

        /// <summary>"Apps" or "Games", whichever it was found in.</summary>
        public string Collection { get; set; }

        /// <summary>The drive's name, such as Hdd1.</summary>
        public string Drive { get; set; }

        /// <summary>From the XEX header's execution info; 0 when it has none.</summary>
        public uint TitleId { get; set; }

        public long Size { get; set; }
        public DateTime Modified { get; set; }

        public bool IsApp
        {
            get { return string.Equals(Collection, "Apps", StringComparison.OrdinalIgnoreCase); }
        }

        private string location;

        /// <summary>Where it is, such as "Hdd1 Apps", unless something else is said.</summary>
        public string Location
        {
            get { return location ?? Drive + " " + Collection; }
            set { location = value; }
        }

        public override string ToString()
        {
            return Path;
        }
    }
}
