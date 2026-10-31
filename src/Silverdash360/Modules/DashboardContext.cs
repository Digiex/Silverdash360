using System;
using System.Collections.Generic;
using Silverdash360.Model;
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

        public void OpenStorage()
        {
            Navigate(new Uri("/Pages/StoragePage.xaml", UriKind.Relative));
        }

        public void OpenThemes()
        {
            Navigate(new Uri("/Pages/ThemePage.xaml", UriKind.Relative));
        }

        /// <summary>A shelf's filters, order and view, by its header.</summary>
        public void OpenShelfOptions(string shelf)
        {
            Navigate(new Uri("/Pages/ShelfOptionsPage.xaml?shelf=" + Uri.EscapeDataString(shelf), UriKind.Relative));
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
