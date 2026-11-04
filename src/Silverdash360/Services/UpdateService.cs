using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Resources;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Files;
using Silverdash360.Services.Media;

namespace Silverdash360.Services
{
    /// <summary>
    /// Updates from the GitHub releases tools/Release.ps1 publishes. A check
    /// asks GitHub's API for the latest release; one with a higher version
    /// than this build's, and not skipped, is offered. An update downloads
    /// the release's zip through the API (github.com itself doesn't answer
    /// the console, api.github.com and the file storage do), checks its size
    /// and SHA-256 against the release's, and puts its files in the
    /// dashboard's folder: each written beside its file as .new, then the
    /// running file renamed to .old (the system lets a file in use be
    /// renamed) and the new one into its place. A release's native images
    /// (.ni.dll) go in only when the folder has the runtime they were made
    /// against, as NativeImage.txt in the zip records it; otherwise they're
    /// left out and the dashboard runs interpreted. An installed image of an
    /// assembly the update replaces without one goes to .old too: an image
    /// left beside a new assembly, or over another runtime, stops the
    /// runtime at the next start. A failure part way puts the old files back.
    /// The dashboard then starts again from its folder, and the next start
    /// deletes the .old files.
    /// </summary>
    public sealed class UpdateService
    {
        private const string LatestUrl = "https://api.github.com/repos/Digiex/Silverdash360/releases/latest";
        private const string SkipSetting = "update.skip";
        private const string NewSuffix = ".new";
        private const string OldSuffix = ".old";
        private const int BlockSize = 0x10000;

        // Files in a release's zip that aren't the dashboard's to install.
        private static readonly string[] NotInstalled = { "LICENSE.md", "INSTALL.txt" };

        // The runtime a release's native images were made against: each of
        // its files with its size, a line each (tools/Release.ps1).
        private const string RuntimeRecord = "NativeImage.txt";

        private readonly XboxFileSystem files;

        public UpdateService(XboxFileSystem files)
        {
            this.files = files;
        }

        /// <summary>A published release.</summary>
        public sealed class Release
        {
            public Version Version;
            public string Tag;
            public string Name;
            /// <summary>Its notes, as the release has them (Markdown).</summary>
            public string Notes;
            /// <summary>The API's URL for the zip, which answers with the file.</summary>
            public string ZipUrl;
            public long ZipSize;
            /// <summary>The zip's SHA-256 in hex, or null when GitHub gives none.</summary>
            public string ZipSha256;
        }

        /// <summary>This build's version.</summary>
        public static Version Current
        {
            get { return new Version(BuildInfo.Version); }
        }

        /// <summary>The release found to offer, until the user answers it.</summary>
        public Release Offered { get; set; }

        /// <summary>
        /// Asks GitHub for the latest release, and calls back on the UI thread
        /// with it, or with null and why: it isn't newer than the version
        /// given, is skipped (unless skipped ones count, as when the user asks),
        /// or couldn't be read.
        /// </summary>
        public void Check(Version than, bool offerSkipped, Action<Release, string> done)
        {
            Get(new Uri(LatestUrl), null, delegate(byte[] body, Exception error)
            {
                Release release = null;
                string why = null;
                if (error != null)
                {
                    // The platform's message is a resource key with its
                    // arguments ("[HttpWebRequest_WebException_RemoteServer]
                    // Arguments: NotFound ..."): the log keeps it, settings
                    // says what it most likely means.
                    Log.Write("Update check failed: {0}", error.Message);
                    why = "Couldn't reach GitHub. Check that the console is connected to the internet.";
                }
                else
                {
                    try
                    {
                        release = Parse(Encoding.UTF8.GetString(body, 0, body.Length));
                        if (release == null)
                        {
                            why = "The latest release can't be installed from here.";
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Write("Update check: the answer is unreadable: {0}", e.Message);
                        why = "GitHub's answer is unreadable.";
                    }
                }
                if (release != null && release.Version <= than)
                {
                    Log.Write("Update check: {0} is the latest", release.Tag);
                    why = "Silverdash360 is up to date (" + BuildInfo.Version + ").";
                    release = null;
                }
                else if (release != null && !offerSkipped && release.Tag == Settings.Get(SkipSetting, ""))
                {
                    Log.Write("Update check: {0} is out, and skipped", release.Tag);
                    why = release.Version + " is skipped.";
                    release = null;
                }
                else if (release != null)
                {
                    Log.Write("Update check: {0} is out", release.Tag);
                }
                done(release, why);
            });
        }

        /// <summary>Doesn't offer the release again.</summary>
        public void Skip(Release release)
        {
            Settings.Set(SkipSetting, release.Tag);
        }

        private static Release Parse(string json)
        {
            IDictionary<string, object> latest = (IDictionary<string, object>)JsonReader.Parse(json);
            string tag = JsonReader.String(latest, "tag_name");
            Version version;
            try
            {
                version = new Version(tag.TrimStart('v'));
            }
            catch (Exception)
            {
                Log.Write("Update check: the latest release's tag {0} isn't a version", tag);
                return null;
            }
            Release release = new Release
            {
                Version = version,
                Tag = tag,
                Name = JsonReader.String(latest, "name") ?? "Silverdash360 " + version,
                Notes = JsonReader.String(latest, "body") ?? "",
            };
            object assets;
            if (latest.TryGetValue("assets", out assets) && assets is List<object>)
            {
                foreach (object item in (List<object>)assets)
                {
                    IDictionary<string, object> asset = item as IDictionary<string, object>;
                    string name = asset != null ? JsonReader.String(asset, "name") : null;
                    if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        release.ZipUrl = JsonReader.String(asset, "url");
                        release.ZipSize = (long)JsonReader.Number(asset, "size", -1);
                        string digest = JsonReader.String(asset, "digest");
                        if (digest != null && digest.StartsWith("sha256:"))
                        {
                            release.ZipSha256 = digest.Substring(7).ToLowerInvariant();
                        }
                        break;
                    }
                }
            }
            if (release.ZipUrl == null)
            {
                Log.Write("Update check: {0} has no zip", tag);
                return null;
            }
            return release;
        }

        /// <summary>
        /// Downloads the release and puts its files in place, telling how it
        /// goes on the UI thread, then starts the dashboard again. failed is
        /// called, on the UI thread, when it couldn't, with the files as they
        /// were.
        /// </summary>
        public void Install(Release release, Action<string> progress, Action<string> failed)
        {
            progress("Downloading " + release.Name + "...");
            Download(release, delegate(byte[] zip, string problem)
            {
                if (problem != null)
                {
                    Log.Write("Update {0} not installed: {1}", release.Tag, problem);
                    failed(problem);
                    return;
                }
                InstallZip(release, zip, progress, failed);
            });
        }

        /// <summary>Installs a release's zip that's been read, on the UI thread; see Install.</summary>
        public void InstallZip(Release release, byte[] zip, Action<string> progress, Action<string> failed)
        {
            // The zip's files, read on the UI thread, as the application's
            // resource reader is.
            Dictionary<string, byte[]> contents;
            try
            {
                contents = Unzip(zip);
            }
            catch (Exception e)
            {
                Log.Write("Update {0}: the zip is unreadable: {1}", release.Tag, e);
                failed("The download is unreadable: " + e.Message);
                return;
            }
            progress("Installing " + release.Name + "...");
            Background.Run(() => Put(contents), delegate(int count, Exception putError)
            {
                if (putError != null)
                {
                    Log.Write("Update {0} not installed: {1}", release.Tag, putError);
                    failed("Couldn't install it: " + putError.Message);
                    return;
                }
                Log.Write("Installed {0}: {1} files; starting it", release.Tag, count);
                progress("Starting " + release.Name + "...");
                Restart();
            });
        }

        /// <summary>
        /// The release's zip, checked against its size and SHA-256, or why
        /// there's none; on the UI thread.
        /// </summary>
        public void Download(Release release, Action<byte[], string> done)
        {
            Get(new Uri(release.ZipUrl), "application/octet-stream", delegate(byte[] zip, Exception error)
            {
                if (error != null)
                {
                    done(null, "The download failed: " + error.Message);
                    return;
                }
                Background.Run(() => Verify(release, zip), delegate(string problem, Exception verifyError)
                {
                    problem = problem ?? (verifyError != null ? verifyError.Message : null);
                    done(problem == null ? zip : null, problem);
                });
            });
        }

        // Null when the download is the release's zip.
        private static string Verify(Release release, byte[] zip)
        {
            if (release.ZipSize >= 0 && zip.Length != release.ZipSize)
            {
                return string.Format("The download is {0} bytes, not {1}.", zip.Length, release.ZipSize);
            }
            if (release.ZipSha256 != null)
            {
                byte[] digest = new SHA256Managed().ComputeHash(zip);
                StringBuilder hex = new StringBuilder(64);
                foreach (byte b in digest)
                {
                    hex.Append(b.ToString("x2"));
                }
                if (hex.ToString() != release.ZipSha256)
                {
                    return "The download isn't the release's: its SHA-256 differs.";
                }
            }
            return null;
        }

        // The files in a zip by their paths in it, without the ones not
        // installed. Stored files are copied out; compressed ones go through
        // the application's resource reader, which reads zips.
        private static Dictionary<string, byte[]> Unzip(byte[] zip)
        {
            int end = -1;
            for (int i = zip.Length - 22; i >= 0 && i >= zip.Length - 22 - 0xFFFF; i--)
            {
                if (U32(zip, i) == 0x06054b50)
                {
                    end = i;
                    break;
                }
            }
            if (end < 0)
            {
                throw new InvalidOperationException("It isn't a zip.");
            }
            int count = U16(zip, end + 10);
            int at = (int)U32(zip, end + 16);
            Dictionary<string, byte[]> contents = new Dictionary<string, byte[]>();
            for (int n = 0; n < count; n++)
            {
                if (U32(zip, at) != 0x02014b50)
                {
                    throw new InvalidOperationException("Its directory is damaged.");
                }
                int method = U16(zip, at + 10);
                int compressed = (int)U32(zip, at + 20);
                int nameLength = U16(zip, at + 28);
                int skip = nameLength + U16(zip, at + 30) + U16(zip, at + 32);
                int local = (int)U32(zip, at + 42);
                string name = Encoding.UTF8.GetString(zip, at + 46, nameLength).Replace('\\', '/');
                at += 46 + skip;
                if (name.EndsWith("/") || Array.IndexOf(NotInstalled, name) >= 0)
                {
                    continue;
                }
                if (name.StartsWith("/") || name.Contains("..") || name.Contains(":"))
                {
                    throw new InvalidOperationException(name + " would go outside the dashboard's folder.");
                }
                if (method == 0)
                {
                    int data = local + 30 + U16(zip, local + 26) + U16(zip, local + 28);
                    byte[] bytes = new byte[compressed];
                    Buffer.BlockCopy(zip, data, bytes, 0, compressed);
                    contents[name] = bytes;
                }
                else
                {
                    StreamResourceInfo file = Application.GetResourceStream(
                        new StreamResourceInfo(new MemoryStream(zip), null), new Uri(name, UriKind.Relative));
                    if (file == null)
                    {
                        throw new InvalidOperationException(name + " can't be read from it.");
                    }
                    using (Stream stream = file.Stream)
                    using (MemoryStream bytes = new MemoryStream())
                    {
                        byte[] block = new byte[BlockSize];
                        int read;
                        while ((read = stream.Read(block, 0, block.Length)) > 0)
                        {
                            bytes.Write(block, 0, read);
                        }
                        contents[name] = bytes.ToArray();
                    }
                }
            }
            if (!contents.ContainsKey("Silverdash360.dll"))
            {
                throw new InvalidOperationException("It has no Silverdash360.dll.");
            }
            return contents;
        }

        private static int U16(byte[] bytes, int at)
        {
            return bytes[at] | (bytes[at + 1] << 8);
        }

        private static uint U32(byte[] bytes, int at)
        {
            return (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));
        }

        // Writes every file as .new first, then swaps each in; a failure puts
        // back what was swapped. The number of files.
        private int Put(Dictionary<string, byte[]> contents)
        {
            string folder = Folder();
            byte[] record;
            if (contents.TryGetValue(RuntimeRecord, out record))
            {
                contents.Remove(RuntimeRecord);
            }
            if (!RuntimeMatches(folder, record))
            {
                foreach (string image in contents.Keys.Where(IsImage).ToList())
                {
                    contents.Remove(image);
                }
            }
            List<string> paths = new List<string>();
            using (NativeBuffer buffer = new NativeBuffer(BlockSize))
            {
                foreach (KeyValuePair<string, byte[]> file in contents)
                {
                    string path = folder + "/" + file.Key;
                    CreateFolders(path.Substring(0, path.LastIndexOf('/')));
                    Write(path + NewSuffix, file.Value, buffer);
                    paths.Add(path);
                }
            }
            List<string> swapped = new List<string>();
            try
            {
                foreach (string path in paths)
                {
                    DeleteIfThere(path + OldSuffix);
                    if (files.Stat(path) != null)
                    {
                        files.Rename(path, path + OldSuffix);
                    }
                    swapped.Add(path);
                    files.Rename(path + NewSuffix, path);
                }
                foreach (string path in StaleImages(folder, contents))
                {
                    DeleteIfThere(path + OldSuffix);
                    files.Rename(path, path + OldSuffix);
                    swapped.Add(path);
                }
            }
            catch (Exception)
            {
                foreach (string path in swapped)
                {
                    try
                    {
                        if (files.Stat(path + OldSuffix) != null)
                        {
                            DeleteIfThere(path);
                            files.Rename(path + OldSuffix, path);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Write("Update: couldn't put back {0}: {1}", path, e.Message);
                    }
                }
                foreach (string path in paths)
                {
                    DeleteIfThere(path + NewSuffix);
                }
                throw;
            }
            return paths.Count;
        }

        private static bool IsImage(string name)
        {
            return name.EndsWith(".ni.dll", StringComparison.OrdinalIgnoreCase);
        }

        // Whether the folder has the runtime the release's native images were
        // made against, by its record. An image over another runtime stops it
        // as it starts; without one the dashboard runs interpreted.
        private bool RuntimeMatches(string folder, byte[] record)
        {
            if (record == null)
            {
                return false;
            }
            foreach (string line in Encoding.UTF8.GetString(record, 0, record.Length).Split('\n'))
            {
                string entry = line.Trim();
                if (entry.Length == 0 || entry.StartsWith("#"))
                {
                    continue;
                }
                int space = entry.LastIndexOf(' ');
                long size;
                if (space <= 0 || !long.TryParse(entry.Substring(space + 1), out size))
                {
                    Log.Write("Update: {0} has a line it doesn't know: {1}", RuntimeRecord, entry);
                    return false;
                }
                string name = entry.Substring(0, space);
                FileEntry file = files.Stat(folder + "/" + name);
                if (file == null || file.Size != size)
                {
                    Log.Write("Update: {0} isn't the runtime's the native images were made against ({1}, not {2} bytes); " +
                              "running interpreted", name, file == null ? "missing" : file.Size + " bytes", size);
                    return false;
                }
            }
            return true;
        }

        // The native images in the folder of assemblies the update replaces
        // without one.
        private List<string> StaleImages(string folder, Dictionary<string, byte[]> contents)
        {
            List<string> stale = new List<string>();
            foreach (string name in contents.Keys)
            {
                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || IsImage(name))
                {
                    continue;
                }
                string image = name.Substring(0, name.Length - 4) + ".ni.dll";
                if (!contents.ContainsKey(image) && files.Stat(folder + "/" + image) != null)
                {
                    stale.Add(folder + "/" + image);
                }
            }
            return stale;
        }

        /// <summary>
        /// Deletes the files an update left (the ones it replaced, and any it
        /// didn't finish), on a background thread.
        /// </summary>
        public void CleanUp()
        {
            Background.Run(() =>
            {
                string folder = Folder();
                CleanUp(folder, 0);
                return 0;
            }, (r, e) =>
            {
                if (e != null)
                {
                    Log.Write("Update: couldn't tidy the dashboard's folder: {0}", e.Message);
                }
            });
        }

        private void CleanUp(string folder, int depth)
        {
            foreach (FileEntry entry in files.List(folder))
            {
                string path = folder + "/" + entry.Name;
                if (entry.IsDirectory)
                {
                    // Not the dashboard's data or a staged build.
                    if (depth < 2 && entry.Name != AppStorage.FolderName && entry.Name != AppFolder.StagingFolder)
                    {
                        CleanUp(path, depth + 1);
                    }
                }
                else if (entry.Name.EndsWith(OldSuffix) || entry.Name.EndsWith(NewSuffix))
                {
                    files.Delete(path, false);
                    Log.Write("Update: deleted {0}", path);
                }
            }
        }

        private string Folder()
        {
            string folder = AppFolder.Path(files);
            if (folder == null || files.IsReadOnly(folder))
            {
                throw new IOException("The dashboard's folder can't be written.");
            }
            return folder;
        }

        private void Write(string path, byte[] bytes, NativeBuffer buffer)
        {
            DeleteIfThere(path);
            using (KernelFile file = files.OpenWrite(path))
            {
                for (int at = 0; at < bytes.Length; at += BlockSize)
                {
                    int count = Math.Min(BlockSize, bytes.Length - at);
                    Buffer.BlockCopy(bytes, at, buffer.Bytes, 0, count);
                    file.Write(buffer, count, at);
                }
            }
        }

        private void DeleteIfThere(string path)
        {
            if (files.Stat(path) != null)
            {
                files.Delete(path, false);
            }
        }

        private void CreateFolders(string path)
        {
            if (files.Stat(path) != null)
            {
                return;
            }
            int slash = path.LastIndexOf('/');
            if (slash > 0)
            {
                CreateFolders(path.Substring(0, slash));
            }
            files.CreateDirectory(path);
        }

        // Starts the dashboard again from its folder, so the new files load.
        private static void Restart()
        {
            string device = AppFolder.DevicePath();
            if (device == null)
            {
                Log.Write("Update: where the dashboard runs from is unknown, so it isn't started again");
                return;
            }
            // Let the log line and the screen out first.
            Thread.Sleep(300);
            Xam.XamLoaderLaunchTitleEx(device + @"\default.xex", device, null, 0);
        }

        // A GET through the platform's HttpWebRequest, which answers HTTPS
        // (this client of the dashboard's own doesn't). It's made on the UI
        // thread, and done is called there.
        private static void Get(Uri url, string accept, Action<byte[], Exception> done)
        {
            url = Http.Offline(url);
            Background.OnUi(delegate
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    if (accept != null)
                    {
                        request.Accept = accept;
                    }
                    request.BeginGetResponse(answer =>
                    {
                        byte[] body = null;
                        Exception error = null;
                        try
                        {
                            using (WebResponse response = request.EndGetResponse(answer))
                            using (Stream stream = response.GetResponseStream())
                            using (MemoryStream bytes = new MemoryStream())
                            {
                                byte[] block = new byte[BlockSize];
                                int read;
                                while ((read = stream.Read(block, 0, block.Length)) > 0)
                                {
                                    bytes.Write(block, 0, read);
                                }
                                body = bytes.ToArray();
                            }
                        }
                        catch (Exception e)
                        {
                            error = e;
                        }
                        Background.OnUi(() => done(body, error));
                    }, null);
                }
                catch (Exception e)
                {
                    done(null, e);
                }
            });
        }
    }
}
