using System;
using System.Collections.Generic;
using Silverdash360.Model;
using Silverdash360.Pages;
using Silverdash360.Services;
using Silverdash360.Services.Ftp;
using Silverdash360.Services.Media;

namespace Silverdash360.Modules
{
    /// <summary>The services and navigation every section and page shares.</summary>
    public sealed class DashboardContext
    {
        public DashboardContext(Library library, ProfileService profiles, ImageService images, SystemService system,
                                FtpServer ftp, MediaService media, ThemeService themes)
        {
            Library = library;
            Profiles = profiles;
            Images = images;
            System = system;
            Ftp = ftp;
            Media = media;
            Themes = themes;
        }

        public Library Library { get; private set; }
        public ProfileService Profiles { get; private set; }
        public ImageService Images { get; private set; }
        public SystemService System { get; private set; }
        public FtpServer Ftp { get; private set; }
        public MediaService Media { get; private set; }
        public ThemeService Themes { get; private set; }

        /// <summary>A section's legend changed; the hub shows it again.</summary>
        public event EventHandler LegendChanged;

        public void RefreshLegend()
        {
            if (LegendChanged != null)
            {
                LegendChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>The disc drive; set by the app once the media service exists.</summary>
        public DiscService Discs { get; set; }

        /// <summary>Updates from GitHub; set by the app.</summary>
        public UpdateService Updates { get; set; }

        /// <summary>Goes to a page by its URI; set by the app.</summary>
        public Action<Uri> Navigate { get; set; }

        /// <summary>Shows a short message in the hub's footer.</summary>
        public event EventHandler<StatusEventArgs> Status;

        /// <summary>The controller of the profile the library shows.</summary>
        public uint UserIndex
        {
            get { return Library.Gamer != null ? Library.Gamer.UserIndex : 0; }
        }

        public void ShowStatus(string text)
        {
            if (Status != null)
            {
                Status(this, new StatusEventArgs(text));
            }
        }

        public void OpenTitle(TitleEntry title)
        {
            Navigate(new Uri("/Pages/TitlePage.xaml?key=" + Uri.EscapeDataString(title.Key), UriKind.Relative));
        }

        public void OpenSearch()
        {
            Navigate(new Uri("/Pages/SearchPage.xaml", UriKind.Relative));
        }

        /// <summary>A media source's folder; null for its top level.</summary>
        public void OpenMedia(IMediaSource source, MediaEntry folder)
        {
            string uri = "/Pages/MediaPage.xaml?source=" + Uri.EscapeDataString(source.Key);
            if (folder != null)
            {
                uri += "&id=" + Uri.EscapeDataString(folder.Id) + "&name=" + Uri.EscapeDataString(folder.Name);
            }
            Navigate(new Uri(uri, UriKind.Relative));
        }

        /// <summary>Plays or shows the entry, with the rest of its kind in the folder queued after it.</summary>
        public void PlayMedia(IList<MediaEntry> folder, MediaEntry entry)
        {
            Media.Start(folder, entry);
            Navigate(new Uri(entry.Kind == MediaKind.Picture ? "/Pages/PicturePage.xaml" : "/Pages/PlayerPage.xaml",
                             UriKind.Relative));
        }

        /// <summary>The file browser at a path of the file tree, the drives at /.</summary>
        public void OpenFiles(string path)
        {
            OpenFiles(path, false);
        }

        /// <summary>The files page; picking, A on a picture takes it as the theme editor's wallpaper.</summary>
        public void OpenFiles(string path, bool pickWallpaper)
        {
            Navigate(new Uri("/Pages/FilesPage.xaml?path=" + Uri.EscapeDataString(path) + (pickWallpaper ? "&pick=wallpaper" : ""),
                             UriKind.Relative));
        }

        /// <summary>Edits the theme shown, starting from it.</summary>
        public void OpenThemeEditor()
        {
            ThemeDraft.Start(Themes.Current);
            Navigate(new Uri("/Pages/ThemeEditorPage.xaml", UriKind.Relative));
        }

        /// <summary>Picks a wallpaper for the theme editor: where from, or from a theme's (its setting).</summary>
        public void OpenWallpaperPicker(string theme)
        {
            OpenWallpaperPicker(theme, null);
        }

        public void OpenWallpaperPicker(string theme, string name)
        {
            string query = theme != null
                ? "?theme=" + Uri.EscapeDataString(theme) + "&name=" + Uri.EscapeDataString(name ?? "")
                : "";
            Navigate(new Uri("/Pages/WallpaperPickerPage.xaml" + query, UriKind.Relative));
        }

        public void OpenStorage()
        {
            Navigate(new Uri("/Pages/StoragePage.xaml", UriKind.Relative));
        }

        public void OpenThemes()
        {
            Navigate(new Uri("/Pages/ThemePage.xaml", UriKind.Relative));
        }

        /// <summary>The welcome screen with the basic settings.</summary>
        public void OpenWelcome()
        {
            Navigate(new Uri("/Pages/WelcomePage.xaml", UriKind.Relative));
        }

        /// <summary>The console's own settings, as the Xbox Dashboard has them.</summary>
        public void OpenSystemSettings()
        {
            Navigate(new Uri("/Pages/SystemSettingsPage.xaml", UriKind.Relative));
        }

        /// <summary>Offers the update in <see cref="UpdateService.Offered"/>.</summary>
        public void OpenUpdate()
        {
            Navigate(new Uri("/Pages/UpdatePage.xaml", UriKind.Relative));
        }

        /// <summary>
        /// Looks for an update, offering a skipped one too, as the user asked;
        /// the status line says when there's none.
        /// </summary>
        public void CheckForUpdate()
        {
            ShowStatus("Checking for updates...");
            Updates.Check(UpdateService.Current, true, delegate(UpdateService.Release release, string why)
            {
                ShowStatus(release != null ? "" : why);
                if (release != null)
                {
                    Updates.Offered = release;
                    OpenUpdate();
                }
            });
        }

        /// <summary>A shelf's filters, order and view, by its header.</summary>
        public void OpenShelfOptions(string shelf)
        {
            Navigate(new Uri("/Pages/ShelfOptionsPage.xaml?shelf=" + Uri.EscapeDataString(shelf), UriKind.Relative));
        }

        /// <summary>
        /// Opens the disc in the drive: a game disc starts, an original Xbox
        /// game, a DVD movie or an audio CD plays in the console's own player,
        /// and a disc of files opens in the media browser.
        /// </summary>
        public void OpenDisc(TitleEntry disc)
        {
            // Started while the tray moves, a disc can't be read, and the
            // console then ends Silverdash with "disc unreadable".
            if (!Discs.IsReady)
            {
                ShowStatus("Wait for the disc tray to close and the disc to be read.");
                return;
            }
            switch (Discs.Kind)
            {
                case DiscKind.Game:
                    Launch(disc);
                    break;
                case DiscKind.Data:
                    IMediaSource source = Media.Find("drive:Dvd");
                    if (source != null)
                    {
                        OpenMedia(source, null);
                    }
                    else
                    {
                        ShowStatus("The disc's files can't be read.");
                    }
                    break;
                case DiscKind.None:
                    ShowStatus("There's no disc in the drive.");
                    break;
                default:
                    ShowStatus("Starting the disc...");
                    ShowStatus(Media.PlayDisc());
                    break;
            }
        }

        /// <summary>
        /// What A does where it plays: starts the title, or the disc; a title
        /// that isn't installed, with nothing to start, shows its page.
        /// </summary>
        public void Play(TitleEntry title)
        {
            if (title.IsDisc)
            {
                OpenDisc(title);
            }
            else if (title.IsInstalled)
            {
                Launch(title);
            }
            else
            {
                OpenTitle(title);
            }
        }

        /// <summary>
        /// A title's page; for the disc, the page of the title the library
        /// has for it, else the disc opens.
        /// </summary>
        public void ShowDetails(TitleEntry title)
        {
            TitleEntry known = title.IsDisc && title.TitleId != 0 ? Library.Find(title.TitleId) : null;
            if (!title.IsDisc)
            {
                OpenTitle(title);
            }
            else if (known != null)
            {
                OpenTitle(known);
            }
            else
            {
                OpenDisc(title);
            }
        }

        /// <summary>Starts an installed title, or says why it can't.</summary>
        public void Launch(TitleEntry title)
        {
            ContentItem item = title.LaunchItem;
            ExecutableItem executable = title.Executable;
            if (item == null && executable == null)
            {
                ShowStatus(title.Name + " isn't installed on this console.");
                return;
            }
            ShowStatus("Starting " + title.Name + "...");
            string error = item != null ? System.Launch(item) : System.Launch(executable);
            if (error != null)
            {
                ShowStatus(error);
            }
        }
    }

    public class StatusEventArgs : EventArgs
    {
        public StatusEventArgs(string text) { Text = text; }
        public string Text { get; private set; }
    }
}
