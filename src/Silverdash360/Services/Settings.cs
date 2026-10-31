using System;
using System.Collections.Generic;
using System.Text;
using Silverdash360.Core;

namespace Silverdash360.Services
{
    /// <summary>
    /// What the dashboard remembers across launches, such as each shelf's view
    /// and filters: text values by name, kept in the app's storage (<see cref="AppStorage"/>). Read and
    /// written on the UI thread; storage is read once at start and written in
    /// the background after a change.
    /// </summary>
    public static class Settings
    {
        private const string FileName = "settings.txt";

        private static readonly Dictionary<string, string> values = new Dictionary<string, string>();
        private static bool saving;
        private static bool saveAgain;

        /// <summary>The stored values were read, on the UI thread.</summary>
        public static event EventHandler Loaded;

        public static bool IsLoaded { get; private set; }

        /// <summary>Reads the stored values in the background.</summary>
        public static void Load()
        {
            Background.Run(() => AppStorage.ReadText(FileName), delegate(string text, Exception error)
            {
                foreach (string line in (text ?? "").Split('\n'))
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0)
                    {
                        // A value set before the file was read wins.
                        string key = line.Substring(0, equals).Trim();
                        if (!values.ContainsKey(key))
                        {
                            values[key] = line.Substring(equals + 1).Trim();
                        }
                    }
                }
                IsLoaded = true;
                if (Loaded != null)
                {
                    Loaded(null, EventArgs.Empty);
                }
            });
        }

        public static string Get(string key, string fallback)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : fallback;
        }

        public static bool GetBool(string key, bool fallback)
        {
            string value = Get(key, null);
            return value == null ? fallback : value == "1";
        }

        public static void Set(string key, string value)
        {
            if (Get(key, null) == value)
            {
                return;
            }
            values[key] = value;
            Save();
        }

        public static void SetBool(string key, bool value)
        {
            Set(key, value ? "1" : "0");
        }

        private static void Save()
        {
            if (saving)
            {
                saveAgain = true;
                return;
            }
            saving = true;
            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in values)
            {
                text.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
            }
            string contents = text.ToString();
            Background.Run(() => { AppStorage.WriteText(FileName, contents); return 0; }, delegate
            {
                saving = false;
                if (saveAgain)
                {
                    saveAgain = false;
                    Save();
                }
            });
        }
    }
}
