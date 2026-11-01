using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Silverdash360.Native;
using Silverdash360.Services.Files;

namespace Silverdash360.Services.Ftp
{
    /// <summary>
    /// One client's connection, in passive (PASV, EPSV) or active (PORT, EPRT)
    /// mode. Files are sent as they are whatever TYPE says.
    /// </summary>
    internal sealed class FtpSession
    {
        private const int TransferSize = 0x10000;
        /// <summary>How long to wait for a data connection, in milliseconds.</summary>
        private const int DataTimeout = 30000;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private readonly FtpServer server;
        private readonly NativeSocket control;
        private readonly NativeBuffer input = new NativeBuffer(1024);
        private readonly StringBuilder pending = new StringBuilder();
        private NativeSocket passive;
        private uint activeAddress;
        private int activePort;
        private string directory = "/";
        private string user;
        private bool loggedIn;
        private long restartAt;
        private string renameFrom;
        private bool closed;

        public FtpSession(FtpServer server, NativeSocket control)
        {
            this.server = server;
            this.control = control;
        }

        private XboxFileSystem Files { get { return server.Files; } }

        public void Run()
        {
            server.Report("FTP client connected");
            try
            {
                Reply(220, "Silverdash360 FTP ready.");
                string line;
                while (!closed && (line = ReadLine()) != null)
                {
                    int space = line.IndexOf(' ');
                    string command = (space < 0 ? line : line.Substring(0, space)).ToUpperInvariant();
                    string argument = space < 0 ? "" : line.Substring(space + 1);
                    try
                    {
                        Handle(command, argument);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        Reply(550, e.Message);
                    }
                    catch (IOException e)
                    {
                        Reply(550, e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                server.Report("FTP session ended: {0}", e.Message);
            }
            finally
            {
                Close();
                input.Dispose();
                server.Report("FTP client disconnected");
            }
        }

        public void Close()
        {
            closed = true;
            ClosePassive();
            control.Dispose();
        }

        private void Handle(string command, string argument)
        {
            switch (command)
            {
                case "USER":
                    user = argument;
                    loggedIn = false;
                    Reply(331, "Password, please.");
                    return;
                case "PASS":
                    loggedIn = user == server.UserName && argument == server.Password;
                    if (loggedIn)
                    {
                        Reply(230, "Logged in.");
                    }
                    else
                    {
                        Reply(530, "Wrong user or password. They're in the dashboard's settings.");
                    }
                    return;
                case "QUIT":
                    Reply(221, "Bye.");
                    closed = true;
                    return;
                case "NOOP":
                    Reply(200, "OK.");
                    return;
                case "FEAT":
                    Send("211-Features:\r\n PASV\r\n EPSV\r\n SIZE\r\n MDTM\r\n REST STREAM\r\n UTF8\r\n211 End\r\n");
                    return;
                case "SYST":
                    Reply(215, "UNIX Type: L8");
                    return;
                case "OPTS":
                    Reply(200, "OK.");
                    return;
            }
            if (!loggedIn)
            {
                Reply(530, "Log in first.");
                return;
            }
            switch (command)
            {
                case "PWD":
                case "XPWD":
                    Reply(257, "\"" + directory + "\" is the current folder.");
                    break;
                case "CWD":
                case "XCWD":
                    ChangeDirectory(argument);
                    break;
                case "CDUP":
                case "XCUP":
                    ChangeDirectory("..");
                    break;
                case "TYPE":
                case "MODE":
                case "STRU":
                case "ALLO":
                    Reply(200, "OK.");
                    break;
                case "PASV":
                    EnterPassive(false);
                    break;
                case "EPSV":
                    EnterPassive(true);
                    break;
                case "PORT":
                    SetActive(argument, false);
                    break;
                case "EPRT":
                    SetActive(argument, true);
                    break;
                case "LIST":
                case "NLST":
                    SendListing(argument, command == "NLST");
                    break;
                case "SIZE":
                    FileEntry sized = Files.Stat(PathOf(argument));
                    if (sized == null || sized.IsDirectory)
                    {
                        Reply(550, "No such file.");
                    }
                    else
                    {
                        Reply(213, sized.Size.ToString(Invariant));
                    }
                    break;
                case "MDTM":
                    FileEntry dated = Files.Stat(PathOf(argument));
                    if (dated == null)
                    {
                        Reply(550, "No such file.");
                    }
                    else
                    {
                        Reply(213, dated.Modified.ToUniversalTime().ToString("yyyyMMddHHmmss", Invariant));
                    }
                    break;
                case "REST":
                    restartAt = long.Parse(argument, Invariant);
                    Reply(350, "Restarting at " + restartAt + ".");
                    break;
                case "RETR":
                    SendFile(PathOf(argument));
                    break;
                case "STOR":
                    ReceiveFile(PathOf(argument));
                    break;
                case "DELE":
                    Files.Delete(PathOf(argument), false);
                    Reply(250, "Deleted.");
                    break;
                case "MKD":
                case "XMKD":
                    string made = PathOf(argument);
                    Files.CreateDirectory(made);
                    Reply(257, "\"" + made + "\" created.");
                    break;
                case "RMD":
                case "XRMD":
                    Files.Delete(PathOf(argument), true);
                    Reply(250, "Removed.");
                    break;
                case "RNFR":
                    renameFrom = PathOf(argument);
                    if (Files.Stat(renameFrom) == null)
                    {
                        renameFrom = null;
                        Reply(550, "No such file.");
                    }
                    else
                    {
                        Reply(350, "Name it what?");
                    }
                    break;
                case "RNTO":
                    if (renameFrom == null)
                    {
                        Reply(503, "RNFR first.");
                        break;
                    }
                    string from = renameFrom;
                    renameFrom = null;
                    Files.Rename(from, PathOf(argument));
                    Reply(250, "Renamed.");
                    break;
                case "ABOR":
                    ClosePassive();
                    Reply(226, "Aborted.");
                    break;
                default:
                    Reply(502, command + " isn't supported.");
                    break;
            }
        }

        private string PathOf(string argument)
        {
            return XboxFileSystem.Combine(directory, argument);
        }

        private void ChangeDirectory(string argument)
        {
            string target = PathOf(argument);
            FileEntry entry = Files.Stat(target);
            if (target != "/" && (entry == null || !entry.IsDirectory))
            {
                Reply(550, "No such folder.");
                return;
            }
            directory = target;
            Reply(250, "Now in " + directory + ".");
        }

        private void EnterPassive(bool extended)
        {
            ClosePassive();
            passive = NativeSocket.Listen(0, 1);
            uint ignored, ip;
            int port, controlPort;
            passive.LocalEndPoint(out ignored, out port);
            // Clients connect back to the address they reached us on. The
            // console reports 0.0.0.0 for it, so take its own address then.
            control.LocalEndPoint(out ip, out controlPort);
            if (ip == 0)
            {
                ip = server.IpAddress;
            }
            if (extended)
            {
                Reply(229, "Entering Extended Passive Mode (|||" + port + "|)");
            }
            else
            {
                Reply(227, string.Format(Invariant, "Entering Passive Mode ({0},{1},{2},{3},{4},{5})",
                                         ip >> 24, (ip >> 16) & 0xFF, (ip >> 8) & 0xFF, ip & 0xFF, port >> 8, port & 0xFF));
            }
        }

        private void ClosePassive()
        {
            if (passive != null)
            {
                passive.Dispose();
                passive = null;
            }
        }

        // Active mode: where PORT or EPRT asked us to connect to.
        private void SetActive(string argument, bool extended)
        {
            ClosePassive();
            try
            {
                if (extended)
                {
                    // |1|address|port|
                    string[] parts = argument.Split(argument.Length > 0 ? argument[0] : '|');
                    if (parts.Length < 4 || parts[1] != "1")
                    {
                        Reply(522, "IPv4 only (1).");
                        return;
                    }
                    activeAddress = ParseAddress(parts[2].Split('.'));
                    activePort = int.Parse(parts[3], Invariant);
                }
                else
                {
                    // h1,h2,h3,h4,p1,p2
                    string[] parts = argument.Split(',');
                    activeAddress = ParseAddress(parts);
                    activePort = int.Parse(parts[4], Invariant) << 8 | int.Parse(parts[5], Invariant);
                }
                Reply(200, "OK.");
            }
            catch (Exception)
            {
                activePort = 0;
                Reply(501, "That isn't an address.");
            }
        }

        private static uint ParseAddress(string[] parts)
        {
            return (uint)(byte.Parse(parts[0], Invariant) << 24 | byte.Parse(parts[1], Invariant) << 16 |
                          byte.Parse(parts[2], Invariant) << 8 | byte.Parse(parts[3], Invariant));
        }

        // The data connection: the client connects to the passive port, or
        // in active mode we connect to the client. Either gives up after a
        // while, so a client that can't connect doesn't hold the session.
        private NativeSocket OpenData()
        {
            if (passive == null && activePort == 0)
            {
                Reply(425, "Use PASV or PORT first.");
                return null;
            }
            Reply(150, "Opening the data connection.");
            try
            {
                if (passive != null)
                {
                    if (!passive.WaitReadable(DataTimeout))
                    {
                        Reply(425, "The data connection didn't arrive.");
                        return null;
                    }
                    return passive.Accept();
                }
                return NativeSocket.Connect(activeAddress, activePort);
            }
            catch (IOException e)
            {
                Reply(425, "No data connection: " + e.Message);
                return null;
            }
            finally
            {
                ClosePassive();
                activePort = 0;
            }
        }

        private void SendListing(string argument, bool namesOnly)
        {
            // Clients send options such as "-la"; they aren't paths.
            string target = argument.StartsWith("-") || argument.Length == 0 ? directory : PathOf(argument);
            List<FileEntry> entries = Files.List(target);
            StringBuilder text = new StringBuilder();
            foreach (FileEntry entry in entries.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (namesOnly)
                {
                    text.Append(entry.Name).Append("\r\n");
                    continue;
                }
                DateTime time = entry.Modified;
                string when = time.Year == DateTime.Now.Year
                    ? time.ToString("MMM dd HH:mm", Invariant)
                    : time.ToString("MMM dd  yyyy", Invariant);
                text.AppendFormat(Invariant, "{0} 1 xbox xbox {1,12} {2} {3}\r\n",
                                  entry.IsDirectory ? "drwxr-xr-x" : "-rw-r--r--", entry.Size, when, entry.Name);
            }
            NativeSocket data = OpenData();
            if (data == null)
            {
                return;
            }
            using (data)
            {
                data.Send(text.ToString());
            }
            Reply(226, entries.Count + " entries.");
        }

        private void SendFile(string path)
        {
            long offset = restartAt;
            restartAt = 0;
            using (KernelFile file = Files.OpenRead(path))
            {
                NativeSocket data = OpenData();
                if (data == null)
                {
                    return;
                }
                long sent = 0;
                using (data)
                using (NativeBuffer buffer = new NativeBuffer(TransferSize))
                {
                    int read;
                    while (!closed && (read = file.Read(buffer, buffer.Length, offset)) > 0)
                    {
                        data.Send(buffer, read);
                        offset += read;
                        sent += read;
                    }
                }
                server.Report("FTP sent {0} ({1} bytes)", path, sent);
            }
            Reply(226, "Sent.");
        }

        private void ReceiveFile(string path)
        {
            restartAt = 0;
            using (KernelFile file = Files.OpenWrite(path))
            {
                NativeSocket data = OpenData();
                if (data == null)
                {
                    return;
                }
                long offset = 0;
                using (data)
                using (NativeBuffer buffer = new NativeBuffer(TransferSize))
                {
                    int received;
                    while (!closed && (received = data.Receive(buffer, buffer.Length)) > 0)
                    {
                        file.Write(buffer, received, offset);
                        offset += received;
                    }
                }
                server.Report("FTP received {0} ({1} bytes)", path, offset);
            }
            Reply(226, "Received.");
        }

        private void Reply(int code, string text)
        {
            Send(code.ToString(Invariant) + " " + text + "\r\n");
        }

        private void Send(string text)
        {
            control.Send(text);
        }

        // A command line, or null when the client has gone.
        private string ReadLine()
        {
            while (true)
            {
                string buffered = pending.ToString();
                int end = buffered.IndexOf('\n');
                if (end >= 0)
                {
                    pending.Remove(0, end + 1);
                    return buffered.Substring(0, end).TrimEnd('\r');
                }
                if (pending.Length > 4096)
                {
                    return null;
                }
                int received = control.Receive(input, input.Length);
                if (received == 0)
                {
                    return null;
                }
                pending.Append(Encoding.UTF8.GetString(input.Bytes, 0, received));
            }
        }
    }
}
