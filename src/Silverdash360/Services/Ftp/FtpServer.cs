using System;
using System.Collections.Generic;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Files;
using Silverlight360;

namespace Silverdash360.Services.Ftp
{
    /// <summary>
    /// An FTP server for the console's drives, running while the dashboard
    /// does. It's off until started, or starts with the dashboard when
    /// <see cref="AutoStart"/> is set. It takes one user, "xbox" unless
    /// changed, with a password made up the first time and kept; settings
    /// shows and changes both.
    /// </summary>
    public sealed class FtpServer
    {
        public const int DefaultPort = 21;
        public const string DefaultUserName = "xbox";
        private const string AutoStartSetting = "ftp.autoStart";
        private const string UserSetting = "ftp.user";
        private const string PasswordSetting = "ftp.password";

        private readonly object sync = new object();
        private readonly List<FtpSession> sessions = new List<FtpSession>();
        private readonly Random random = new Random();
        private NativeSocket listener;

        public FtpServer(XboxFileSystem files)
        {
            Files = files;
            Port = DefaultPort;
        }

        public XboxFileSystem Files { get; private set; }
        /// <summary>The port asked for.</summary>
        public int Port { get; set; }

        /// <summary>
        /// The port listened on, which can differ: xenia moves ports below
        /// 1024 up by 10000.
        /// </summary>
        public int ListeningPort { get; private set; }
        // Read on the sessions' threads as they log in.
        private volatile string userName = DefaultUserName;
        private volatile string password;

        public string UserName { get { return userName; } }
        public string Password { get { return password; } }
        public bool IsRunning { get { return listener != null; } }

        /// <summary>Whether it starts with the dashboard. On the UI thread.</summary>
        public bool AutoStart
        {
            get { return Settings.GetBool(AutoStartSetting, false); }
            set { Settings.SetBool(AutoStartSetting, value); }
        }

        /// <summary>
        /// Takes the user and password from settings, making the password
        /// up the first time. On the UI thread, once settings are loaded.
        /// </summary>
        public void LoadSettings()
        {
            string user = Settings.Get(UserSetting, null);
            userName = string.IsNullOrEmpty(user) ? DefaultUserName : user;
            string saved = Settings.Get(PasswordSetting, null);
            if (string.IsNullOrEmpty(saved))
            {
                saved = NewPassword();
                Settings.Set(PasswordSetting, saved);
            }
            password = saved;
        }

        /// <summary>Changes the user; the next login takes it. On the UI thread.</summary>
        public void SetUserName(string value)
        {
            userName = value;
            Settings.Set(UserSetting, value);
        }

        /// <summary>Changes the password; the next login takes it. On the UI thread.</summary>
        public void SetPassword(string value)
        {
            password = value;
            Settings.Set(PasswordSetting, value);
        }

        // Six digits, as easy to type on a PC as to read off the screen.
        private string NewPassword()
        {
            return random.Next(100000, 1000000).ToString();
        }

        /// <summary>Something happened; raised on the server's threads.</summary>
        public event Action<string> Message;

        public int SessionCount
        {
            get { lock (sync) { return sessions.Count; } }
        }

        /// <summary>The console's address for clients, or null when it's unknown.</summary>
        public string Address
        {
            get
            {
                uint ip = IpAddress;
                return ip != 0 ? NativeSocket.FormatAddress(ip) : null;
            }
        }

        /// <summary>The console's IPv4 address from its XNADDR, or 0.</summary>
        public uint IpAddress
        {
            get
            {
                try
                {
                    using (NativeBuffer address = new NativeBuffer(0x24))
                    {
                        Net.XNetGetTitleXnAddr(Net.CallerTitle, address.Address);
                        return address.U32(0);
                    }
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        public void Start()
        {
            lock (sync)
            {
                if (listener != null)
                {
                    return;
                }
                if (password == null)
                {
                    // Before settings are read: one for this run alone, so
                    // the one kept isn't replaced.
                    password = NewPassword();
                }
                listener = NativeSocket.Listen(Port, 4);
                uint ignored;
                int port;
                listener.LocalEndPoint(out ignored, out port);
                ListeningPort = port != 0 ? port : Port;
            }
            Thread thread = new Thread(AcceptLoop) { IsBackground = true, Name = "FTP" };
            thread.Start(listener);
            Report("FTP server listening on port {0}", ListeningPort);
            // Debug output only: xenia's log or a development kit, never the screen.
            Log.Write("FTP password {0}", Password);
        }

        public void Stop()
        {
            NativeSocket closing;
            List<FtpSession> open;
            lock (sync)
            {
                closing = listener;
                listener = null;
                open = new List<FtpSession>(sessions);
            }
            if (closing == null)
            {
                return;
            }
            // Closing the socket ends the accept loop.
            closing.Dispose();
            foreach (FtpSession session in open)
            {
                session.Close();
            }
            Report("FTP server stopped");
        }

        private void AcceptLoop(object state)
        {
            NativeSocket socket = (NativeSocket)state;
            while (true)
            {
                NativeSocket client;
                try
                {
                    client = socket.Accept();
                }
                catch (Exception e)
                {
                    if (IsRunning)
                    {
                        Report("FTP accept stopped: {0}", e.Message);
                    }
                    return;
                }
                FtpSession session = new FtpSession(this, client);
                lock (sync)
                {
                    sessions.Add(session);
                }
                Thread thread = new Thread(delegate()
                {
                    try
                    {
                        session.Run();
                    }
                    finally
                    {
                        lock (sync)
                        {
                            sessions.Remove(session);
                        }
                    }
                }) { IsBackground = true, Name = "FTP session" };
                thread.Start();
            }
        }

        internal void Report(string format, params object[] args)
        {
            string text = string.Format(format, args);
            Log.Write(text);
            if (Message != null)
            {
                Message(text);
            }
        }
    }
}
