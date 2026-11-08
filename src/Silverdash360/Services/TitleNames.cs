using System.Collections.Generic;
using System.Linq;

namespace Silverdash360.Services
{
    /// <summary>
    /// Names the user gave titles in the library, by the title's key, kept
    /// in the app's storage. A name here comes before any other.
    /// </summary>
    public static class TitleNames
    {
        // A line each: the title's key, a tab and the name.
        private const string FileName = "names.txt";

        private static readonly object Lock = new object();
        private static Dictionary<string, string> names;

        /// <summary>The name the user gave the title, or null; reads the file the first time.</summary>
        public static string Of(string key)
        {
            lock (Lock)
            {
                Load();
                string name;
                return names.TryGetValue(key, out name) ? name : null;
            }
        }

        /// <summary>Gives the title a name, or with null takes it away, and saves them; on a background thread.</summary>
        public static void Set(string key, string name)
        {
            lock (Lock)
            {
                Load();
                if (name == null)
                {
                    names.Remove(key);
                }
                else
                {
                    names[key] = name;
                }
                AppStorage.WriteText(FileName, string.Join("\n", names.Select(n => n.Key + "\t" + n.Value).ToArray()));
            }
        }

        private static void Load()
        {
            if (names != null)
            {
                return;
            }
            names = new Dictionary<string, string>();
            foreach (string line in (AppStorage.ReadText(FileName) ?? "").Split('\n'))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && line.Substring(tab + 1).Trim().Length > 0)
                {
                    names[line.Substring(0, tab)] = line.Substring(tab + 1).Trim();
                }
            }
        }
    }
}
