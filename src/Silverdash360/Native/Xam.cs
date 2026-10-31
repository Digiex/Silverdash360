using System.Runtime.InteropServices;

namespace Silverdash360.Native
{
    /// <summary>
    /// The console's system library (xam.xex). It exports by ordinal only, so
    /// each entry point is "#" and the decimal ordinal. Pointers are passed as
    /// guest addresses (see <see cref="NativeBuffer"/>). These are the calls
    /// the console's own dashboard makes to list content, profiles and tiles.
    /// </summary>
    public static class Xam
    {
        public const uint ErrorSuccess = 0;
        public const uint ErrorInsufficientBuffer = 122;
        public const uint ErrorNoMoreFiles = 18;
        public const uint ErrorIoPending = 997;

        /// <summary>Any user, or no user, where a call takes a user index.</summary>
        public const uint UserIndexNone = 0xFE;
        public const int MaxUsers = 4;

        // 0x250: DWORD XamEnumerate(HANDLE, DWORD flags, PVOID buffer,
        //   DWORD bufferSize, PDWORD itemsReturned, PXOVERLAPPED)
        [DllImport("xam.xex", EntryPoint = "#592")]
        public static extern uint XamEnumerate(uint handle, uint flags, uint buffer, uint bufferSize,
                                               out uint itemsReturned, uint overlapped);

        // 0x279: DWORD XamContentAggregateCreateEnumerator(XUID, DWORD deviceId,
        //   DWORD contentType, DWORD titleId, PHANDLE). Title 0 lists every
        //   title and type 0xFFFFFFFF every type. Items are 0x200-byte
        //   XCONTENT_DATA_INTERNAL records.
        [DllImport("xam.xex", EntryPoint = "#633")]
        public static extern uint XamContentAggregateCreateEnumerator(ulong xuid, uint deviceId, uint contentType,
                                                                      uint titleId, out uint handle);

        // 0x26E: DWORD XamContentGetMetaDataInternal(XCONTENT_DATA_INTERNAL*,
        //   XCONTENT_METADATA*, PXOVERLAPPED). The metadata is the package
        //   header's from offset 0x344, 0x93D6 bytes.
        [DllImport("xam.xex", EntryPoint = "#622")]
        public static extern uint XamContentGetMetaDataInternal(uint contentData, uint metadata, uint overlapped);

        // 0x271: DWORD XamContentCreateInternal(LPCSTR rootName,
        //   XCONTENT_DATA_INTERNAL*, DWORD flags, PDWORD disposition,
        //   PDWORD licenseMask, DWORD cacheSize, ULONGLONG contentSize,
        //   PXOVERLAPPED). Mounts the package as rootName: (flags 3 opens an
        //   existing one), as the console's dashboard mounts themes.
        [DllImport("xam.xex", EntryPoint = "#625", CharSet = CharSet.Ansi)]
        public static extern uint XamContentCreateInternal(string rootName, uint contentData, uint flags,
                                                           uint disposition, uint licenseMask, uint cacheSize,
                                                           ulong contentSize, uint overlapped);

        // 0x25F: DWORD XamContentGetDeviceName(DWORD deviceId, LPWSTR name,
        //   DWORD chars): the storage device's name, such as "Hard Drive".
        [DllImport("xam.xex", EntryPoint = "#607")]
        public static extern uint XamContentGetDeviceName(uint deviceId, uint name, uint chars);

        // 0x25A: DWORD XamContentClose(LPCSTR rootName, PXOVERLAPPED)
        [DllImport("xam.xex", EntryPoint = "#602", CharSet = CharSet.Ansi)]
        public static extern uint XamContentClose(string rootName, uint overlapped);

        // 0x237: DWORD XamProfileOpen(XUID, LPCSTR mountName, DWORD flags,
        //   XCONTENT_DATA*). Mounts a signed-in profile's own package, where
        //   the console's dashboard keeps its per-profile files.
        [DllImport("xam.xex", EntryPoint = "#567", CharSet = CharSet.Ansi)]
        public static extern uint XamProfileOpen(ulong xuid, string mountName, uint flags, uint contentData);

        // 0x238: DWORD XamProfileClose(LPCSTR mountName)
        [DllImport("xam.xex", EntryPoint = "#568", CharSet = CharSet.Ansi)]
        public static extern uint XamProfileClose(string mountName);

        // 0x294: DWORD XamContentLaunchImageInternalEx(XCONTENT_DATA_INTERNAL*,
        //   LPCSTR xexName, DWORD)
        [DllImport("xam.xex", EntryPoint = "#660", CharSet = CharSet.Ansi)]
        public static extern uint XamContentLaunchImageInternalEx(uint contentData, string xexName, uint unknown);

        // 0x1A4: VOID XamLoaderLaunchTitle(LPCSTR path, DWORD flags). A null
        //   path returns to the console's dashboard.
        [DllImport("xam.xex", EntryPoint = "#420", CharSet = CharSet.Ansi)]
        public static extern void XamLoaderLaunchTitle(string path, uint flags);

        // 0x1A5: VOID XamLoaderLaunchTitleEx(LPCSTR path, LPCSTR dDrivePath,
        //   LPCSTR commandLine, DWORD flags). XamLoaderLaunchTitle calls it
        //   with flag 4, which returns to the dashboard for a path that starts
        //   at a device (\Device\...); without it such a path is started.
        [DllImport("xam.xex", EntryPoint = "#421", CharSet = CharSet.Ansi)]
        public static extern void XamLoaderLaunchTitleEx(string path, string dDrivePath, string commandLine, uint flags);

        // 0x210: XUSER_SIGNIN_STATE XamUserGetSigninState(DWORD userIndex)
        [DllImport("xam.xex", EntryPoint = "#528")]
        public static extern uint XamUserGetSigninState(uint userIndex);

        // 0x227: DWORD XamUserGetSigninInfo(DWORD userIndex, DWORD flags,
        //   XUSER_SIGNIN_INFO*), 0x28 bytes. Without flags the XUID is the
        //   online one for a profile signed in to LIVE.
        public const uint SigninInfoOfflineXuidOnly = 2;

        [DllImport("xam.xex", EntryPoint = "#551")]
        public static extern uint XamUserGetSigninInfo(uint userIndex, uint flags, uint info);

        // 0x219: DWORD XamUserReadProfileSettings(DWORD titleId, DWORD userIndex,
        //   DWORD xuidCount, XUID*, DWORD settingCount, DWORD* settingIds,
        //   DWORD* bufferSize, XUSER_READ_PROFILE_SETTING_RESULT*, PXOVERLAPPED)
        [DllImport("xam.xex", EntryPoint = "#537")]
        public static extern uint XamUserReadProfileSettings(uint titleId, uint userIndex, uint xuidCount, uint xuids,
                                                             uint settingCount, uint settingIds, uint bufferSize,
                                                             uint results, uint overlapped);

        // 0x2F3: DWORD XamUserCreateTitlesPlayedEnumerator(DWORD titleId,
        //   DWORD userIndex, XUID, DWORD startingIndex, DWORD count,
        //   DWORD* bufferSize, PHANDLE). Items are 0xA8 bytes.
        [DllImport("xam.xex", EntryPoint = "#755")]
        public static extern uint XamUserCreateTitlesPlayedEnumerator(uint titleId, uint userIndex, ulong xuid,
                                                                      uint startingIndex, uint count,
                                                                      out uint bufferSize, out uint handle);

        // 0x2EE: DWORD XamUserCreateAchievementEnumerator(DWORD titleId,
        //   DWORD userIndex, XUID, DWORD detailFlags, DWORD startingIndex,
        //   DWORD count, DWORD* bufferSize, PHANDLE)
        [DllImport("xam.xex", EntryPoint = "#750")]
        public static extern uint XamUserCreateAchievementEnumerator(uint titleId, uint userIndex, ulong xuid,
                                                                     uint detailFlags, uint startingIndex, uint count,
                                                                     out uint bufferSize, out uint handle);

        // 0x2EF: DWORD XamReadTile(DWORD tileType, DWORD titleId, ULONGLONG itemId,
        //   DWORD userIndex, PBYTE buffer, DWORD* bufferSize, PXOVERLAPPED).
        //   The tile is a PNG. A null buffer asks for the size.
        [DllImport("xam.xex", EntryPoint = "#751")]
        public static extern uint XamReadTile(uint tileType, uint titleId, ulong itemId, uint userIndex,
                                              uint buffer, uint bufferSize, uint overlapped);

        // 0x2BC: DWORD XamShowSigninUI(DWORD usersNeeded, DWORD flags)
        [DllImport("xam.xex", EntryPoint = "#700")]
        public static extern uint XamShowSigninUI(uint usersNeeded, uint flags);

        // 0x2C4: DWORD XamShowGamerCardUI(DWORD userIndex)
        [DllImport("xam.xex", EntryPoint = "#708")]
        public static extern uint XamShowGamerCardUI(uint userIndex);

        // 0x2C5: DWORD XamShowAchievementsUI(DWORD userIndex, DWORD titleId)
        [DllImport("xam.xex", EntryPoint = "#709")]
        public static extern uint XamShowAchievementsUI(uint userIndex, uint titleId);

        // 0x2BF: DWORD XamShowFriendsUI(DWORD userIndex)
        [DllImport("xam.xex", EntryPoint = "#703")]
        public static extern uint XamShowFriendsUI(uint userIndex);

        // 0x282: DWORD XamGetSystemVersion()
        [DllImport("xam.xex", EntryPoint = "#642")]
        public static extern uint XamGetSystemVersion();

        // 0x1CF: DWORD XamGetCurrentTitleId()
        [DllImport("xam.xex", EntryPoint = "#463")]
        public static extern uint XamGetCurrentTitleId();

        // 0x3CD: DWORD XGetLanguage()
        [DllImport("xam.xex", EntryPoint = "#973")]
        public static extern uint XGetLanguage();

    }

    /// <summary>The kernel (xboxkrnl.exe), by ordinal.</summary>
    public static partial class Kernel
    {
        // 3: ULONG DbgPrint(PCSZ format, ...)
        [DllImport("xboxkrnl.exe", EntryPoint = "#3", CharSet = CharSet.Ansi)]
        public static extern int DbgPrint(string format, string text);

        // 0x121: VOID RtlCopyString(PANSI_STRING destination, const ANSI_STRING* source):
        //   copies the smaller of the source's length and the destination's
        //   maximum length.
        [DllImport("xboxkrnl.exe", EntryPoint = "#289")]
        public static extern void RtlCopyString(uint destination, uint source);

        /// <summary>
        /// Bytes at an address the system handed back, such as a structure
        /// it allocated; at most 65535. Marshal's reads aren't public on this
        /// runtime, so RtlCopyString copies them, with both strings' lengths
        /// set to the count so nothing past it is touched.
        /// </summary>
        public static byte[] ReadMemory(uint address, int length)
        {
            using (NativeBuffer strings = new NativeBuffer(16))
            using (NativeBuffer copy = new NativeBuffer(length))
            {
                // Two ANSI_STRINGs: length, maximum length, buffer.
                strings.SetU16(0, (ushort)length);
                strings.SetU16(2, (ushort)length);
                strings.SetU32(4, address);
                strings.SetU16(8, 0);
                strings.SetU16(10, (ushort)length);
                strings.SetU32(12, copy.Address);
                RtlCopyString(strings[8], strings.Address);
                return copy.Slice(0, length);
            }
        }

        // 0xCF: NTSTATUS NtClose(HANDLE)
        [DllImport("xboxkrnl.exe", EntryPoint = "#207")]
        public static extern int NtClose(uint handle);

        // 0x84: VOID KeQuerySystemTime(PLARGE_INTEGER)
        [DllImport("xboxkrnl.exe", EntryPoint = "#132")]
        public static extern void KeQuerySystemTime(out long fileTime);

        // 0xC6: NTSTATUS MmQueryStatistics(PMM_STATISTICS stats). stats starts
        //   with its own size, 104: total physical pages at 4, kernel pages
        //   at 8, then the title's and the system's 11 counts each (available
        //   pages first), then the highest physical page.
        [DllImport("xboxkrnl.exe", EntryPoint = "#198")]
        public static extern int MmQueryStatistics(uint stats);

        // 0xE0: NTSTATUS NtOpenSymbolicLinkObject(PHANDLE, POBJECT_ATTRIBUTES)
        [DllImport("xboxkrnl.exe", EntryPoint = "#224")]
        public static extern int NtOpenSymbolicLinkObject(out uint handle, uint objectAttributes);

        // 0xEC: NTSTATUS NtQuerySymbolicLinkObject(HANDLE, PANSI_STRING target,
        //   PULONG returnedLength): the link's target, not NUL-terminated.
        [DllImport("xboxkrnl.exe", EntryPoint = "#236")]
        public static extern int NtQuerySymbolicLinkObject(uint handle, uint target, uint returnedLength);
    }
}
