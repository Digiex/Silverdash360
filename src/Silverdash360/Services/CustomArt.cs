using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;
using Silverdash360.Services.Files;
using Silverlight360;

namespace Silverdash360.Services
{
    /// <summary>
    /// Art and details the user gives a title kept as a folder, a
    /// default.xex in a folder of a drive's Apps or Games folder: a
    /// Silverdash360Art folder beside the default.xex, laid out as an x360db
    /// title's folder is, so one can be copied in as it is. info.json has the
    /// name, description and the rest in x360db's form; artwork/boxart.jpg,
    /// background.jpg and icon.png are the box front, the backdrop and the
    /// tile, and can be right in the folder too. They come before anything
    /// downloaded. The folder's name is Silverdash360's own, so it doesn't
    /// meet the title's files, and nothing in it or the title's folder is
    /// written. Call it from a background thread: it reads the drives.
    /// </summary>
    public sealed class CustomArt
    {
        /// <summary>The folder beside a title's default.xex.</summary>
        public const string FolderName = "Silverdash360Art";
        public const string BoxArt = "boxart.jpg";
        public const string Background = "background.jpg";
        public const string Icon = "icon.png";
        private const string Info = "info.json";
        private const string Artwork = "artwork";
        private const string DefaultExecutable = "/default.xex";
        // Larger files aren't read: a backdrop is 1280 by 720.
        private const long MaxSize = 8 * 1024 * 1024;
        private const int ReadBlock = 64 * 1024;

        private readonly XboxFileSystem files;

        public CustomArt(XboxFileSystem files)
        {
            this.files = files;
        }

        /// <summary>The art folder of an executable in a folder of its own, or null.</summary>
        public static string FolderOf(ExecutableItem executable)
        {
            string path = executable.Path;
            return path != null && path.EndsWith(DefaultExecutable, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - DefaultExecutable.Length) + "/" + FolderName
                : null;
        }

        /// <summary>The art folder of the title's first executable in a folder of its own, or null.</summary>
        public static string FolderOf(TitleEntry title)
        {
            foreach (ExecutableItem executable in title.Executables)
            {
                string folder = FolderOf(executable);
                if (folder != null)
                {
                    return folder;
                }
            }
            return null;
        }

        /// <summary>One of the title's images (BoxArt, Background or Icon), or null.</summary>
        public byte[] Image(TitleEntry title, string file)
        {
            string folder = FolderOf(title);
            return folder == null ? null : ReadFile(folder + "/" + Artwork + "/" + file) ?? ReadFile(folder + "/" + file);
        }

        /// <summary>The title's details from its info.json, or null.</summary>
        public TitleDetails Details(TitleEntry title)
        {
            string folder = FolderOf(title);
            return folder == null ? null : Details(folder);
        }

        /// <summary>The details in an art folder's info.json; null without one, or one that can't be read.</summary>
        public TitleDetails Details(string folder)
        {
            byte[] json = ReadFile(folder + "/" + Info);
            if (json == null)
            {
                return null;
            }
            try
            {
                TitleDetails details = TitleDetails.ParseX360db(
                    JsonReader.Parse(Encoding.UTF8.GetString(json, 0, json.Length)) as IDictionary<string, object>);
                if (details != null)
                {
                    details.IsCustom = true;
                }
                return details;
            }
            catch (Exception e)
            {
                Log.Write("{0}/{1} unreadable: {2}", folder, Info, e.Message);
                return null;
            }
        }

        // A file's bytes; null when it isn't there, is too large or can't be read.
        private byte[] ReadFile(string path)
        {
            try
            {
                FileEntry entry = files.Stat(path);
                if (entry == null || entry.IsDirectory || entry.Size == 0 || entry.Size > MaxSize)
                {
                    return null;
                }
                MemoryStream bytes = new MemoryStream((int)entry.Size);
                using (KernelFile file = files.OpenRead(path))
                using (NativeBuffer block = new NativeBuffer(ReadBlock))
                {
                    int read;
                    while (bytes.Length < MaxSize && (read = file.Read(block, block.Length, bytes.Length)) > 0)
                    {
                        bytes.Write(block.Bytes, 0, read);
                    }
                }
                return bytes.ToArray();
            }
            catch (Exception e)
            {
                Log.Write("{0} unreadable: {1}", path, e.Message);
                return null;
            }
        }
    }
}
