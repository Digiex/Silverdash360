using System;
using System.IO;
using System.Text;
using Silverdash360.Native;

namespace Silverdash360.Services.Ftp
{
    /// <summary>
    /// A blocking TCP or UDP socket on the console's Winsock. Calls block
    /// their thread, so the servers run every socket on threads of their own.
    /// </summary>
    public sealed class NativeSocket : IDisposable
    {
        private static readonly object StartLock = new object();
        private static bool started;

        private uint handle;

        private NativeSocket(uint handle)
        {
            this.handle = handle;
        }

        /// <summary>Starts the network stack for plain sockets; safe to call again.</summary>
        public static void StartNetworking()
        {
            lock (StartLock)
            {
                if (started)
                {
                    return;
                }
                using (NativeBuffer parameters = new NativeBuffer(16))
                using (NativeBuffer data = new NativeBuffer(0x200))
                {
                    // XNetStartupParams: its size, then the flags.
                    parameters.Bytes[0] = 13;
                    parameters.Bytes[1] = Net.StartupBypassSecurity;
                    Net.XNetStartup(Net.CallerTitle, parameters.Address);
                    int result = Net.WSAStartup(Net.CallerTitle, 0x0202, data.Address);
                    if (result != 0)
                    {
                        throw new IOException("The network didn't start: " + result + ".");
                    }
                }
                started = true;
            }
        }

        /// <summary>A socket listening on a port, 0 for any free one.</summary>
        public static NativeSocket Listen(int port, int backlog)
        {
            NativeSocket socket = Create();
            try
            {
                using (NativeBuffer address = Address(0, (ushort)port))
                {
                    Check(Net.bind(Net.CallerTitle, socket.handle, address.Address, 16), "bind");
                }
                Check(Net.listen(Net.CallerTitle, socket.handle, backlog), "listen");
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        /// <summary>A socket connected to an IPv4 address and port.</summary>
        public static NativeSocket Connect(uint ip, int port)
        {
            NativeSocket socket = Create();
            try
            {
                using (NativeBuffer address = Address(ip, (ushort)port))
                {
                    Check(Net.connect(Net.CallerTitle, socket.handle, address.Address, 16), "connect");
                }
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Waits until the socket has something to read, or for a listening
        /// socket a connection to accept; false when the time ran out.
        /// </summary>
        public bool WaitReadable(int milliseconds)
        {
            using (NativeBuffer set = new NativeBuffer(4 + 64 * 4))
            using (NativeBuffer timeout = new NativeBuffer(8))
            {
                set.SetU32(0, 1);
                set.SetU32(4, handle);
                timeout.SetU32(0, (uint)(milliseconds / 1000));
                timeout.SetU32(4, (uint)(milliseconds % 1000 * 1000));
                int ready = Net.select(Net.CallerTitle, 0, set.Address, 0, 0, timeout.Address);
                if (ready < 0)
                {
                    throw new IOException("select failed: " + Net.WSAGetLastError() + ".");
                }
                return ready > 0;
            }
        }

        /// <summary>A UDP socket bound to a free port, for sending datagrams and reading the replies.</summary>
        public static NativeSocket Udp()
        {
            NativeSocket socket = Create(Net.SockDgram, Net.IpProtoUdp);
            try
            {
                using (NativeBuffer address = Address(0, 0))
                {
                    Check(Net.bind(Net.CallerTitle, socket.handle, address.Address, 16), "bind");
                }
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        /// <summary>Sends a datagram to an IPv4 address and port.</summary>
        public void SendTo(byte[] data, uint ip, int port)
        {
            using (NativeBuffer buffer = new NativeBuffer(data))
            using (NativeBuffer address = Address(ip, (ushort)port))
            {
                if (Net.sendto(Net.CallerTitle, handle, buffer.Address, data.Length, 0, address.Address, 16) < 0)
                {
                    throw new IOException("sendto failed: " + Net.WSAGetLastError() + ".");
                }
            }
        }

        /// <summary>Receives a datagram into a pinned buffer, with the address it came from.</summary>
        public int ReceiveFrom(NativeBuffer buffer, out uint ip, out int port)
        {
            using (NativeBuffer from = new NativeBuffer(16))
            using (NativeBuffer length = new NativeBuffer(4))
            {
                length.SetU32(0, 16);
                int received = Net.recvfrom(Net.CallerTitle, handle, buffer.Address, buffer.Length, 0, from.Address,
                                            length.Address);
                if (received < 0)
                {
                    throw new IOException("recvfrom failed: " + Net.WSAGetLastError() + ".");
                }
                port = from.U16(2);
                ip = from.U32(4);
                return received;
            }
        }

        private static NativeSocket Create()
        {
            return Create(Net.SockStream, Net.IpProtoTcp);
        }

        private static NativeSocket Create(int type, int protocol)
        {
            StartNetworking();
            uint handle = Net.socket(Net.CallerTitle, Net.AfInet, type, protocol);
            if (handle == Net.InvalidSocket)
            {
                throw new IOException("Couldn't make a socket: " + Net.WSAGetLastError() + ".");
            }
            NativeSocket socket = new NativeSocket(handle);
            // Plain TCP rather than the console's encrypted protocol, which
            // nothing but another console speaks.
            if (socket.SetOption(Net.SoMarkInsecure, 1) != 0)
            {
                int error = Net.WSAGetLastError();
                socket.Dispose();
                throw new IOException("The console refused a plain TCP socket (error " + error +
                                      "). The title needs the insecure sockets privilege.");
            }
            socket.SetOption(Net.SoReuseAddr, 1);
            return socket;
        }

        private int SetOption(int name, int value)
        {
            using (NativeBuffer option = new NativeBuffer(4))
            {
                option.SetU32(0, (uint)value);
                return Net.setsockopt(Net.CallerTitle, handle, Net.SolSocket, name, option.Address, 4);
            }
        }

        /// <summary>Waits for a connection; throws when the socket is closed.</summary>
        public NativeSocket Accept()
        {
            uint accepted = Net.accept(Net.CallerTitle, handle, 0, 0);
            if (accepted == Net.InvalidSocket)
            {
                throw new IOException("accept failed: " + Net.WSAGetLastError() + ".");
            }
            return new NativeSocket(accepted);
        }

        /// <summary>The local IPv4 address and port.</summary>
        public void LocalEndPoint(out uint address, out int port)
        {
            using (NativeBuffer name = new NativeBuffer(16))
            using (NativeBuffer length = new NativeBuffer(4))
            {
                length.SetU32(0, 16);
                Check(Net.getsockname(Net.CallerTitle, handle, name.Address, length.Address), "getsockname");
                port = name.U16(2);
                address = name.U32(4);
            }
        }

        /// <summary>Receives up to count bytes into a pinned buffer; 0 when the peer closed.</summary>
        public int Receive(NativeBuffer buffer, int count)
        {
            int received = Net.recv(Net.CallerTitle, handle, buffer.Address, count, 0);
            if (received < 0)
            {
                throw new IOException("recv failed: " + Net.WSAGetLastError() + ".");
            }
            return received;
        }

        public void Send(NativeBuffer buffer, int count)
        {
            int sent = 0;
            while (sent < count)
            {
                int result = Net.send(Net.CallerTitle, handle, buffer[sent], count - sent, 0);
                if (result <= 0)
                {
                    throw new IOException("send failed: " + Net.WSAGetLastError() + ".");
                }
                sent += result;
            }
        }

        public void Send(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (NativeBuffer buffer = new NativeBuffer(bytes))
            {
                Send(buffer, bytes.Length);
            }
        }

        public void Dispose()
        {
            uint closing = handle;
            handle = Net.InvalidSocket;
            if (closing != Net.InvalidSocket)
            {
                Net.shutdown(Net.CallerTitle, closing, Net.ShutdownBoth);
                Net.closesocket(Net.CallerTitle, closing);
            }
        }

        private static NativeBuffer Address(uint ip, ushort port)
        {
            // sockaddr_in: family, port, address, 8 bytes of zeros; big-endian.
            NativeBuffer address = new NativeBuffer(16);
            address.SetU16(0, Net.AfInet);
            address.SetU16(2, port);
            address.SetU32(4, ip);
            return address;
        }

        private static void Check(int result, string call)
        {
            if (result == Net.SocketError)
            {
                throw new IOException(call + " failed: " + Net.WSAGetLastError() + ".");
            }
        }

        /// <summary>An IPv4 address in dotted form, or 0 when the text isn't one.</summary>
        public static uint ParseAddress(string text)
        {
            string[] parts = (text ?? "").Split('.');
            if (parts.Length != 4)
            {
                return 0;
            }
            uint ip = 0;
            foreach (string part in parts)
            {
                byte value;
                if (!byte.TryParse(part, out value))
                {
                    return 0;
                }
                ip = ip << 8 | value;
            }
            return ip;
        }

        public static string FormatAddress(uint ip)
        {
            return string.Format("{0}.{1}.{2}.{3}", ip >> 24, (ip >> 16) & 0xFF, (ip >> 8) & 0xFF, ip & 0xFF);
        }
    }
}
