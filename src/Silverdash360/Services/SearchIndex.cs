using System;
using System.Collections.Generic;
using System.Linq;
using Silverdash360.Model;

namespace Silverdash360.Services
{
    /// <summary>
    /// The titles search can find, made once per refresh. Search runs on
    /// every key typed, and app code is interpreted on the console, so
    /// rather than test each title with a lambda it searches one string of
    /// every title's search text with the framework's IndexOf, and only the
    /// titles that match cost any app code.
    /// </summary>
    public sealed class SearchIndex
    {
        public static readonly SearchIndex Empty = new SearchIndex(new TitleEntry[0]);

        // Titles in name order, their search texts joined with newlines (a
        // word typed never holds one), and where each text starts, with the
        // end of the whole as the last entry.
        private readonly TitleEntry[] titles;
        private readonly string[] texts;
        private readonly string corpus;
        private readonly int[] starts;

        /// <summary>Indexes the titles; their order is kept for equally good matches.</summary>
        public SearchIndex(IEnumerable<TitleEntry> titles)
        {
            this.titles = titles.ToArray();
            texts = this.titles.Select(t => t.SearchText).ToArray();
            corpus = string.Join("\n", texts);
            starts = new int[texts.Length + 1];
            int at = 0;
            for (int i = 0; i < texts.Length; i++)
            {
                starts[i] = at;
                at += texts[i].Length + 1;
            }
            starts[texts.Length] = at;
        }

        public int Count { get { return titles.Length; } }

        /// <summary>
        /// Titles whose search text holds every word, those starting with the
        /// first word first, each group in the index's order.
        /// </summary>
        public List<TitleEntry> Find(string[] words)
        {
            List<TitleEntry> leading = new List<TitleEntry>();
            List<TitleEntry> others = new List<TitleEntry>();
            if (words.Length == 0 || titles.Length == 0)
            {
                return leading;
            }
            // Look for the longest word, which matches the fewest titles,
            // and check the others only in those.
            string rarest = words.OrderByDescending(w => w.Length).First();
            int at = 0;
            int i = 0;
            while (at < corpus.Length && (at = corpus.IndexOf(rarest, at, StringComparison.Ordinal)) >= 0)
            {
                // The title the match is in. Matches only move forward, so
                // this steps along rather than searching (Array.BinarySearch
                // on ints is generic code with no native image: interpreted).
                while (starts[i + 1] <= at)
                {
                    i++;
                }
                if (words.Length == 1)
                {
                    // The first match in a title is at its start when the
                    // title starts with the word.
                    (at == starts[i] ? leading : others).Add(titles[i]);
                }
                else if (HasAll(texts[i], words))
                {
                    (texts[i].StartsWith(words[0], StringComparison.Ordinal) ? leading : others).Add(titles[i]);
                }
                // On to the next title: this one is in already, or isn't at all.
                at = starts[i + 1];
            }
            leading.AddRange(others);
            return leading;
        }

        private static bool HasAll(string text, string[] words)
        {
            for (int i = 0; i < words.Length; i++)
            {
                if (text.IndexOf(words[i], StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
