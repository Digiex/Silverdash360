using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Model;
using Silverdash360.Modules;
using Silverdash360.Services;
using Silverdash360.Services.Files;
using Silverdash360.Services.Media;

namespace Silverdash360.Pages
{
    /// <summary>
    /// The console's drives and every file on them, opened with ?path=/Hdd1/...
    /// (the drives at /). A opens a folder, plays music, videos and pictures
    /// with the rest of their kind in the folder queued after them, starts an
    /// executable with its folder as its D: drive, and starts a content
    /// package the library knows as one that starts a title. Packages are
    /// listed by the name and thumbnail in their header.
    /// </summary>
    public partial class FilesPage : XboxApplicationPage
    {
        private enum Kind
        {
            Folder,
            Media,
            Executable,
            Package,
            Other,
        }

        private sealed class Item
        {
            public string Name;
            public string Path;
            public Kind Kind;
            public long Size;
            public MediaKind Media;
            public PackageHeader Package;

            public override string ToString()
            {
                return Package != null && Package.DisplayName.Length > 0 ? Package.DisplayName + " (" + Name + ")" : Name;
            }
        }

        private string path;

        public FilesPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Items.Direction = ScrollDirection.Vertical;
            Items.Lines = 1;
            Items.TileWidth = 0;
            Items.TileHeight = 72;
            Items.Spacing = 8;
            Items.TileTemplate = new RowTemplate(o => Row((Item)o)) { IconSize = 52 };
            Items.ItemInvoked += delegate(object sender, ItemEventArgs e) { Open((Item)e.Item); };
        }

        private static DashboardContext Context { get { return App.Context; } }

        private static XboxFileSystem Files { get { return Context.Ftp.Files; } }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            string query;
            path = NavigationContext.QueryString.TryGetValue("path", out query) ? query : "/";
            FolderText.Text = path == "/" ? "Drives" : path;
            FocusHelper.Focus(Items);
            Items.EmptyText = "Loading...";
            Items.Items = null;
            string listing = path;
            Silverdash360.Core.Background.Run(() => List(listing), delegate(List<Item> result, Exception error)
            {
                if (error != null)
                {
                    Items.EmptyText = "Couldn't read this folder: " + error.Message;
                    return;
                }
                Items.EmptyText = "This folder is empty.";
                Items.Items = result.Cast<object>().ToList();
            });
        }

        // On a background thread: the folder's entries, folders first, with
        // what each file is.
        private static List<Item> List(string folder)
        {
            List<Item> items = new List<Item>();
            foreach (FileEntry entry in Files.List(folder))
            {
                Item item = new Item
                {
                    Name = entry.Name,
                    Path = XboxFileSystem.Combine(folder, entry.Name),
                    Size = entry.Size,
                    Kind = entry.IsDirectory ? Kind.Folder : Kind.Other,
                };
                if (!entry.IsDirectory)
                {
                    MediaKind? media = MediaFormats.OfFile(entry.Name);
                    if (media.HasValue)
                    {
                        item.Kind = Kind.Media;
                        item.Media = media.Value;
                    }
                    else if (entry.Name.EndsWith(".xex", StringComparison.OrdinalIgnoreCase))
                    {
                        item.Kind = Kind.Executable;
                    }
                    else if (entry.Name.IndexOf('.') < 0 && entry.Size >= PackageHeader.MinimumSize)
                    {
                        // Packages are named without an extension.
                        item.Package = PackageHeader.Read(Files, item.Path);
                        if (item.Package != null)
                        {
                            item.Kind = Kind.Package;
                        }
                    }
                }
                items.Add(item);
            }
            return items.OrderBy(i => i.Kind == Kind.Folder ? 0 : 1).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void Open(Item item)
        {
            switch (item.Kind)
            {
                case Kind.Folder:
                    Context.OpenFiles(item.Path);
                    break;
                case Kind.Media:
                    Play(item);
                    break;
                case Kind.Executable:
                    StartExecutable(item);
                    break;
                case Kind.Package:
                    StartPackage(item);
                    break;
                default:
                    StatusText.Text = "Silverdash can't open " + item.Name + ".";
                    break;
            }
        }

        // With the folder's other media of the same kind after it, through
        // the drive's media source, which serves files to the player.
        private void Play(Item item)
        {
            string drive = item.Path.Split('/')[1];
            IMediaSource source = Context.Media.Find("drive:" + drive);
            if (source == null)
            {
                StatusText.Text = "Media on this drive can't be played.";
                return;
            }
            List<MediaEntry> queue = new List<MediaEntry>();
            MediaEntry chosen = null;
            foreach (Item other in Items.Items.Cast<Item>())
            {
                if (other.Kind != Kind.Media || other.Media != item.Media)
                {
                    continue;
                }
                MediaEntry entry = new MediaEntry { Source = source, Id = other.Path, Name = other.Name, Kind = other.Media, Size = other.Size };
                queue.Add(entry);
                if (other == item)
                {
                    chosen = entry;
                }
            }
            Context.PlayMedia(queue, chosen);
        }

        private void StartExecutable(Item item)
        {
            string folder = item.Path.Substring(0, item.Path.LastIndexOf('/'));
            ExecutableItem executable = new ExecutableItem
            {
                Path = item.Path,
                DevicePath = Files.DevicePath(item.Path),
                DeviceFolder = Files.DevicePath(folder),
                Name = item.Name,
            };
            StatusText.Text = Context.System.Launch(executable);
        }

        // Started through the library's record of it, as a title's page
        // starts it; a package isn't started from its file alone.
        private void StartPackage(Item item)
        {
            string name = item.Name;
            uint titleId = item.Package.TitleId;
            ContentItem known = Context.Library.AllContent.FirstOrDefault(
                c => c.TitleId == titleId && string.Equals(c.FileName, name, StringComparison.OrdinalIgnoreCase));
            if (known == null || !known.IsLaunchable)
            {
                StatusText.Text = item.Package.DisplayName + " is " + ContentType.NameOf(item.Package.ContentType).ToLowerInvariant() +
                                  " content and doesn't start a title.";
                return;
            }
            StatusText.Text = Context.System.Launch(known) ?? "";
        }

        private static RowData Row(Item item)
        {
            RowData row = new RowData { Title = item.Name, ArtSeed = (uint)item.Name.GetHashCode() };
            switch (item.Kind)
            {
                case Kind.Folder:
                    row.Subtitle = item.Path.LastIndexOf('/') == 0 ? DriveMediaSource.NameOf(item.Name) : "Folder";
                    break;
                case Kind.Media:
                    row.Subtitle = MediaFormats.NameOf(item.Media);
                    break;
                case Kind.Executable:
                    row.Subtitle = "Executable";
                    break;
                case Kind.Package:
                    row.Title = item.Package.DisplayName.Length > 0 ? item.Package.DisplayName : item.Name;
                    row.Subtitle = ContentType.NameOf(item.Package.ContentType) + "  ·  title " +
                                   item.Package.TitleId.ToString("X8") + "  ·  " + item.Name;
                    string packagePath = item.Path;
                    row.Art = done => Context.Images.PackageThumbnail(Files, packagePath, done);
                    break;
                default:
                    row.Subtitle = "File";
                    break;
            }
            row.Detail = item.Kind == Kind.Folder ? "" : SystemService.FormatSize(item.Size);
            return row;
        }
    }
}
