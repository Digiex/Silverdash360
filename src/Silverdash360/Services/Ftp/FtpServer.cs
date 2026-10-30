using System;
using System.Collections.Generic;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Files;

namespace Silverdash360.Services.Ftp
{
    /// <summary>
    /// An FTP server for the console's drives, running while the dashboard
    /// does. It's off until started, and takes the user "xbox" with a
    /// password made up at each start, shown in settings.
    /// </summary>
    public sealed class FtpServer
    {
        public const int DefaultPort = 21;
        public const string UserName = "xbox";

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
        public string Password { get; private set; }
        public bool IsRunning { get { return listener != null; } }

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
                try
                {
                    using (NativeBuffer address = new NativeBuffer(0x24))
                    {
                        Net.XNetGetTitleXnAddr(Net.CallerTitle, address.Address);
                        uint ip = address.U32(0);
                        return ip != 0 ? NativeSocket.FormatAddress(ip) : null;
                    }
                }
                catch (Exception)
                {
                    return null;
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
                Password = random.Next(100000, 1000000).ToString();
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
