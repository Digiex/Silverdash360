using System.Runtime.InteropServices;

namespace Silverdash360.Native
{
    /// <summary>
    /// The console's Winsock, exported by xam.xex as NetDll_* functions. Each
    /// takes the caller first (<see cref="CallerTitle"/>). Plain TCP needs the
    /// title's "insecure sockets" privilege, which the apps' host has.
    /// </summary>
    public static class Net
    {
        public const uint CallerTitle = 1;

        public const int AfInet = 2;
        public const int SockStream = 1;
        public const int IpProtoTcp = 6;
        public const int SolSocket = 0xFFFF;
        /// <summary>
        /// Marks a socket as plain TCP rather than the console's encrypted
        /// protocol. A title may set it with the "insecure sockets" privilege
        /// (XEX privilege 6); without it, PCs can't talk to the socket.
        /// </summary>
        public const int SoMarkInsecure = 0x5801;
        public const int SoReuseAddr = 0x0004;
        public const int ShutdownBoth = 2;
        public const int SocketError = -1;
        public const uint InvalidSocket = 0xFFFFFFFF;

        /// <summary>XNetStartupParams.cfgFlags: allow plain sockets.</summary>
        public const byte StartupBypassSecurity = 0x01;

        // 1: int WSAStartup(DWORD caller, WORD version, WSADATA*)
        [DllImport("xam.xex", EntryPoint = "#1")]
        public static extern int WSAStartup(uint caller, ushort version, uint data);

        // 0x33: int XNetStartup(DWORD caller, const XNetStartupParams*)
        [DllImport("xam.xex", EntryPoint = "#51")]
        public static extern int XNetStartup(uint caller, uint parameters);

        // 3: SOCKET socket(DWORD caller, int af, int type, int protocol)
        [DllImport("xam.xex", EntryPoint = "#3")]
        public static extern uint socket(uint caller, int af, int type, int protocol);

        // 4: int closesocket(DWORD caller, SOCKET)
        [DllImport("xam.xex", EntryPoint = "#4")]
        public static extern int closesocket(uint caller, uint s);

        // 5: int shutdown(DWORD caller, SOCKET, int how)
        [DllImport("xam.xex", EntryPoint = "#5")]
        public static extern int shutdown(uint caller, uint s, int how);

        // 7: int setsockopt(DWORD caller, SOCKET, int level, int name, const char* value, int length)
        [DllImport("xam.xex", EntryPoint = "#7")]
        public static extern int setsockopt(uint caller, uint s, int level, int name, uint value, int length);

        // 9: int getsockname(DWORD caller, SOCKET, sockaddr*, int* length)
        [DllImport("xam.xex", EntryPoint = "#9")]
        public static extern int getsockname(uint caller, uint s, uint name, uint length);

        // 0xB: int bind(DWORD caller, SOCKET, const sockaddr*, int length)
        [DllImport("xam.xex", EntryPoint = "#11")]
        public static extern int bind(uint caller, uint s, uint name, int length);

        // 0xD: int listen(DWORD caller, SOCKET, int backlog)
        [DllImport("xam.xex", EntryPoint = "#13")]
        public static extern int listen(uint caller, uint s, int backlog);

        // 0xE: SOCKET accept(DWORD caller, SOCKET, sockaddr*, int* length)
        [DllImport("xam.xex", EntryPoint = "#14")]
        public static extern uint accept(uint caller, uint s, uint address, uint length);

        // 0x12: int recv(DWORD caller, SOCKET, char* buffer, int length, int flags)
        [DllImport("xam.xex", EntryPoint = "#18")]
        public static extern int recv(uint caller, uint s, uint buffer, int length, int flags);

        // 0x16: int send(DWORD caller, SOCKET, const char* buffer, int length, int flags)
        [DllImport("xam.xex", EntryPoint = "#22")]
        public static extern int send(uint caller, uint s, uint buffer, int length, int flags);

        // 0x1B: int WSAGetLastError()
        [DllImport("xam.xex", EntryPoint = "#27")]
        public static extern int WSAGetLastError();

        // 0x49: DWORD XNetGetTitleXnAddr(DWORD caller, XNADDR*). The XNADDR
        //   starts with the console's IP address.
        [DllImport("xam.xex", EntryPoint = "#73")]
        public static extern uint XNetGetTitleXnAddr(uint caller, uint address);
    }
}
