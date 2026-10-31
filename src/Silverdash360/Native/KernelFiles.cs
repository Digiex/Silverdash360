using System.Runtime.InteropServices;

namespace Silverdash360.Native
{
    /// <summary>The kernel's file calls, by ordinal.</summary>
    public static partial class Kernel
    {
        public const int StatusSuccess = 0;
        public const int StatusNoMoreFiles = unchecked((int)0x80000006);
        public const int StatusNoSuchFile = unchecked((int)0xC000000F);
        public const int StatusEndOfFile = unchecked((int)0xC0000011);

        public const uint GenericRead = 0x80000000;
        public const uint GenericWrite = 0x40000000;
        public const uint Delete = 0x00010000;
        public const uint Synchronize = 0x00100000;
        public const uint FileListDirectory = 0x0001;
        public const uint FileReadAttributes = 0x0080;

        public const uint FileShareRead = 1;
        public const uint FileShareWrite = 2;
        public const uint FileShareDelete = 4;

        // Creation dispositions.
        public const uint FileOpen = 1;
        public const uint FileCreate = 2;
        public const uint FileOverwriteIf = 5;

        // Create options.
        public const uint FileDirectoryFile = 0x01;
        public const uint FileSynchronousIoNonAlert = 0x20;
        public const uint FileNonDirectoryFile = 0x40;

        public const uint ObjCaseInsensitive = 0x40;
        public const uint FileAttributeDirectory = 0x10;
        public const uint FileAttributeNormal = 0x80;

        // Information classes.
        public const uint FileRenameInformation = 10;
        public const uint FileDispositionInformation = 13;

        // 0xD2: NTSTATUS NtCreateFile(PHANDLE, ACCESS_MASK, POBJECT_ATTRIBUTES,
        //   PIO_STATUS_BLOCK, PLARGE_INTEGER allocationSize, ULONG attributes,
        //   ULONG shareAccess, ULONG disposition, ULONG createOptions)
        [DllImport("xboxkrnl.exe", EntryPoint = "#210")]
        public static extern int NtCreateFile(out uint handle, uint access, uint objectAttributes, uint ioStatus,
                                              uint allocationSize, uint attributes, uint share, uint disposition,
                                              uint options);

        // 0xF0: NTSTATUS NtReadFile(HANDLE, HANDLE event, PVOID apc, PVOID apcContext,
        //   PIO_STATUS_BLOCK, PVOID buffer, ULONG length, PLARGE_INTEGER offset)
        [DllImport("xboxkrnl.exe", EntryPoint = "#240")]
        public static extern int NtReadFile(uint handle, uint ev, uint apc, uint apcContext, uint ioStatus,
                                            uint buffer, uint length, uint offset);

        // 0xFF: NTSTATUS NtWriteFile(the same as NtReadFile)
        [DllImport("xboxkrnl.exe", EntryPoint = "#255")]
        public static extern int NtWriteFile(uint handle, uint ev, uint apc, uint apcContext, uint ioStatus,
                                             uint buffer, uint length, uint offset);

        // 0xE4: NTSTATUS NtQueryDirectoryFile(HANDLE, HANDLE event, PVOID apc,
        //   PVOID apcContext, PIO_STATUS_BLOCK, FILE_DIRECTORY_INFORMATION*,
        //   ULONG length, PANSI_STRING mask, BOOLEAN restartScan)
        [DllImport("xboxkrnl.exe", EntryPoint = "#228")]
        public static extern int NtQueryDirectoryFile(uint handle, uint ev, uint apc, uint apcContext, uint ioStatus,
                                                      uint information, uint length, uint mask, uint restartScan);

        // 0xE7: NTSTATUS NtQueryFullAttributesFile(POBJECT_ATTRIBUTES,
        //   FILE_NETWORK_OPEN_INFORMATION*), 0x38 bytes
        [DllImport("xboxkrnl.exe", EntryPoint = "#231")]
        public static extern int NtQueryFullAttributesFile(uint objectAttributes, uint information);

        // 0xF7: NTSTATUS NtSetInformationFile(HANDLE, PIO_STATUS_BLOCK, PVOID,
        //   ULONG length, FILE_INFORMATION_CLASS)
        [DllImport("xboxkrnl.exe", EntryPoint = "#247")]
        public static extern int NtSetInformationFile(uint handle, uint ioStatus, uint information, uint length,
                                                      uint informationClass);
    }
}
