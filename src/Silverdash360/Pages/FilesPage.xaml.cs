using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
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
    /// package the library knows as one that starts a title. Y on an
    /// executable adds it to the library's games or apps, or takes it out.
    /// Packages are listed by the name and thumbnail in their header.
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

        // A choice of where to add an executable in the library.
        private sealed class Choice
        {
            public string Text;
            public string Detail;
            // Apps or Games, or null to take it out.
            public string Collection;
        }

        private const string FolderLegend = "A open   B back";
        private const string ExecutableLegend = "A start   Y add to library   B back";
        private const string ChoiceLegend = "A choose   B cancel";

        private string path;
        // Picking a wallpaper for the theme editor: A on a picture takes it.
        private bool picking;
        // The executable being added to the library, while the choices show.
        private Item adding;
        private IList<object> folderItems;

        public FilesPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Items.Direction = ScrollDirection.Vertical;
            Items.Lines = 1;
            Items.TileWidth = 0;
            Items.TileHeight = 72;
            Items.Spacing = 8;
            Items.TileTemplate = new RowTemplate(o => o is Choice ? ChoiceRow((Choice)o) : Row((Item)o)) { IconSize = 52 };
            Items.ItemInvoked += delegate(object sender, ItemEventArgs e)
            {
                if (e.Item is Choice)
                {
                    Add((Choice)e.Item);
                }
                else
                {
                    Open((Item)e.Item);
                }
            };
            Items.ItemButton += delegate(object sender, ItemEventArgs e)
            {
                Item item = e.Item as Item;
                if (e.Button == GamePadButton.Y && item != null && item.Kind == Kind.Executable && !picking)
                {
                    ShowChoices(item);
                    e.Handled = true;
                }
            };
            Items.SelectionChanged += delegate { ShowLegend(); };
            GamePadButtonDown += delegate(object sender, GamePadButtonEventArgs e)
            {
                // B leaves the choices for the folder.
                if (e.Button == GamePadButton.B && adding != null)
                {
                    EndChoices();
                    e.Handled = true;
                }
            };
        }

        private static DashboardContext Context { get { return App.Context; } }

        private static XboxFileSystem Files { get { return Context.Ftp.Files; } }

        private string pageKey;
        private int backRow = -1;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            pageKey = e.Uri.OriginalString;
            backRow = BackSelection.Arrived(pageKey);
            string query;
            picking = NavigationContext.QueryString.TryGetValue("pick", out query) && query == "wallpaper";
            // Picked on a folder further in: back to the editor, once this
            // navigation is over.
            if (picking && ThemeDraft.Picked)
            {
                Silverdash360.Core.Background.OnUi(GoBack);
                return;
            }
            if (picking)
            {
                StatusText.Text = "Pick a JPEG or PNG picture for the wallpaper.";
            }
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
                BackSelection.Restore(Items, ref backRow);
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            BackSelection.Leaving(pageKey, Items);
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
            if (picking)
            {
                PickWallpaper(item);
                return;
            }
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

        private void PickWallpaper(Item item)
        {
            if (item.Kind == Kind.Folder)
            {
                Context.OpenFiles(item.Path, true);
                return;
            }
            if (!ThemeDraft.IsPicture(item.Name))
            {
                StatusText.Text = "A wallpaper is a JPEG or PNG picture.";
                return;
            }
            StatusText.Text = "Reading " + item.Name + "...";
            string picture = item.Path;
            long size = item.Size;
            Silverdash360.Core.Background.Run(() => ThemeDraft.ReadPicture(Files, picture, size), delegate(byte[] bytes, Exception error)
            {
                if (error != null || bytes == null)
                {
                    StatusText.Text = "Couldn't use " + item.Name + ": " + (error != null ? error.Message : "it's empty");
                    return;
                }
                ThemeDraft.Pick(bytes);
                GoBack();
            });
        }

        private void GoBack()
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
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

        private void ShowLegend()
        {
            Item item = Items.SelectedItem as Item;
            Legend.Text = adding != null ? ChoiceLegend
                : item != null && item.Kind == Kind.Executable && !picking ? ExecutableLegend : FolderLegend;
        }

        // Where to add the executable: the games or the apps, or out of the
        // library when it was added before.
        private void ShowChoices(Item item)
        {
            string executable = item.Path;
            Silverdash360.Core.Background.Run(() => ExecutableScanner.AddedTo(executable), delegate(string addedTo, Exception error)
            {
                if (adding != null)
                {
                    return;
                }
                adding = item;
                folderItems = Items.Items;
                List<object> choices = new List<object>
                {
                    new Choice { Text = "Games", Detail = "List it with the games", Collection = "Games" },
                    new Choice { Text = "Apps", Detail = "List it with the apps", Collection = "Apps" },
                };
                if (addedTo != null)
                {
                    choices.Add(new Choice { Text = "Remove from library", Detail = "It's listed with the " + addedTo.ToLowerInvariant() + " now" });
                }
                FolderText.Text = "Add " + item.Name + " to the library";
                Items.Items = choices;
                if (addedTo != null)
                {
                    Items.SelectItem(choices.Cast<Choice>().First(c => c.Collection == addedTo));
                }
                ShowLegend();
            });
        }

        private void EndChoices()
        {
            Item item = adding;
            adding = null;
            FolderText.Text = path == "/" ? "Drives" : path;
            Items.Items = folderItems;
            Items.SelectItem(item);
            ShowLegend();
        }

        // Kept in the library's list of added executables, then the library
        // is read again, which lists it.
        private void Add(Choice choice)
        {
            string executable = adding.Path;
            string name = adding.Name;
            string collection = choice.Collection;
            EndChoices();
            Silverdash360.Core.Background.Run(() => { ExecutableScanner.SetAdded(executable, collection); return true; },
                                               delegate(bool done, Exception error)
            {
                if (error != null)
                {
                    StatusText.Text = "Couldn't change the library: " + error.Message;
                    return;
                }
                StatusText.Text = collection == null ? name + " is out of the library."
                    : name + " is added to the " + collection.ToLowerInvariant() + ".";
                Context.Library.Refresh();
            });
        }

        private static RowData ChoiceRow(Choice choice)
        {
            return new RowData { Title = choice.Text, Subtitle = choice.Detail, ArtSeed = (uint)choice.Text.GetHashCode() };
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
