using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverlight360;

namespace Silverdash360.Services
{
    /// <summary>
    /// The Xbox Dashboard's System Info (dash 2.0.17559, 0x92280938): the
    /// console's serial number and ID, the versions, and the codes support
    /// asks for, each got and written as it does; and its hidden Reset System
    /// Settings, a button sequence on the page (0x92280E68), a question, then
    /// the console's settings cleared and a restart (0x92281078).
    /// </summary>
    public static class SystemInfo
    {
        // 0x244: NTSTATUS XeKeysGetKey(WORD key, PVOID buffer, PDWORD size)
        [DllImport("xboxkrnl.exe", EntryPoint = "#580")]
        private static extern int XeKeysGetKey(uint key, uint buffer, uint size);

        // 0x246: NTSTATUS XeKeysGetConsoleID(PBYTE raw, PCHAR text /*12*/)
        [DllImport("xboxkrnl.exe", EntryPoint = "#582")]
        private static extern int XeKeysGetConsoleID(uint raw, uint text);

        // 0x363: NTSTATUS XeKeysGetTruncatedSecondaryConsoleId(PULONGLONG)
        [DllImport("xboxkrnl.exe", EntryPoint = "#867")]
        private static extern int XeKeysGetTruncatedSecondaryConsoleId(uint id);

        // 0x182: XeCryptHmacSha(key, keyLength, in1, length1, in2, length2, in3, length3, out, outLength)
        [DllImport("xboxkrnl.exe", EntryPoint = "#386")]
        private static extern void XeCryptHmacSha(uint key, uint keyLength, uint in1, uint length1, uint in2, uint length2,
                                                  uint in3, uint length3, uint output, uint outputLength);

        // 0x174: XeCryptDesParity(in, length, out)
        [DllImport("xboxkrnl.exe", EntryPoint = "#372")]
        private static extern void XeCryptDesParity(uint input, uint length, uint output);

        // 0x175: XeCryptDesKey(XECRYPT_DES_STATE*, const BYTE key[8])
        [DllImport("xboxkrnl.exe", EntryPoint = "#373")]
        private static extern void XeCryptDesKey(uint state, uint key);

        // 0x176: XeCryptDesEcb(const XECRYPT_DES_STATE*, const BYTE in[8], BYTE out[8], BOOL encrypt)
        [DllImport("xboxkrnl.exe", EntryPoint = "#374")]
        private static extern void XeCryptDesEcb(uint state, uint input, uint output, uint encrypt);

        // 0x197: NTSTATUS XexGetProcedureAddress(HANDLE module, DWORD ordinal, PVOID* address)
        [DllImport("xboxkrnl.exe", EntryPoint = "#407")]
        private static extern int XexGetProcedureAddress(uint module, uint ordinal, uint address);

        // 0x195: NTSTATUS XexGetModuleHandle(PCSTR name, PHANDLE)
        [DllImport("xboxkrnl.exe", EntryPoint = "#405")]
        private static extern int XexGetModuleHandle(uint name, uint handle);

        // xam 0xAF3: ULONGLONG XamGetSecurityViolationsDetected()
        [DllImport("xam.xex", EntryPoint = "#2803")]
        private static extern ulong XamGetSecurityViolationsDetected();

        // xam 0x642: XamPackageManagerGetAuthoritativeManifestVersion(PULONGLONG)
        [DllImport("xam.xex", EntryPoint = "#1602")]
        private static extern int XamPackageManagerGetAuthoritativeManifestVersion(uint version);

        // xam 0x643: XamGetCurrentSystemOnlineManifestRevision(PULONGLONG)
        [DllImport("xam.xex", EntryPoint = "#1603")]
        private static extern int XamGetCurrentSystemOnlineManifestRevision(uint revision);

        // xam 0x885: XamNuiGetDeviceStatus(status*): flags at 0xC.
        [DllImport("xam.xex", EntryPoint = "#2181")]
        private static extern int XamNuiGetDeviceStatus(uint status);

        // xam 0x8E2: XamNuiGetSupportString(PCHAR buffer, DWORD size)
        [DllImport("xam.xex", EntryPoint = "#2274")]
        private static extern int XamNuiGetSupportString(uint buffer, uint size);

        /// <summary>The page's lines, in its order: a title and its value.</summary>
        public static List<KeyValuePair<string, string>> Lines()
        {
            string serial = Serial();
            List<KeyValuePair<string, string>> lines = new List<KeyValuePair<string, string>>
            {
                Line("Console Serial Number", serial),
                Line("Console ID", Safe(ConsoleId)),
                Line("Xbox 360™ video game and entertainment system", ""),
                Line("© 2015 Microsoft Corporation. All rights reserved.", ""),
                Line("D", Safe(() => DashboardVersion() + " - K:" + KernelVersion(0x158) + " (BK:" + KernelVersion(0x157) + ")")),
            };
            // E and P, the package manager's manifest versions, are left out:
            // XamPackageManagerGetAuthoritativeManifestVersion takes the
            // console down when an app calls it (xenia only stubs it).
            AddCode(lines, "X", () => Groups(SecurityCode(serial)));
            AddCode(lines, "S", SecondaryId);
            AddCode(lines, "F", Kinect);
            AddCode(lines, "Kinect", KinectSupport);
            return lines;
        }

        /// <summary>The Xbox Dashboard's text beside System Info: the dashboard's version.</summary>
        public static string Summary()
        {
            return "Dashboard: " + Safe(DashboardVersion);
        }

        private static KeyValuePair<string, string> Line(string title, string value)
        {
            return new KeyValuePair<string, string>(title, value);
        }

        // A line the Xbox Dashboard leaves out when its value can't be had.
        private static void AddCode(List<KeyValuePair<string, string>> lines, string title, Func<string> value)
        {
            string text = Safe(value);
            if (!string.IsNullOrEmpty(text))
            {
                lines.Add(Line(title, text));
            }
        }

        private static string Safe(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                Log.Write("System info: {0}", e.Message);
                return "";
            }
        }

        // Key 0x14: 12 ASCII digits.
        private static string Serial()
        {
            using (NativeBuffer serial = new NativeBuffer(16))
            using (NativeBuffer size = new NativeBuffer(4))
            {
                size.SetU32(0, 12);
                return XeKeysGetKey(0x14, serial.Address, size.Address) < 0 ? "" : serial.Ansi(0, 12);
            }
        }

        // The kernel's text of the console certificate's ID, 12 characters.
        private static string ConsoleId()
        {
            using (NativeBuffer text = new NativeBuffer(16))
            {
                return XeKeysGetConsoleID(0, text.Address) < 0 ? "" : text.Ansi(0, 12);
            }
        }

        // The Xbox Dashboard shows its own version; the system's is the same.
        private static string DashboardVersion()
        {
            return new SystemService().SystemVersion;
        }

        // XboxKrnlVersion (0x158) and XboxKrnlBaseVersion (0x157): data
        // exports, four WORDs, the last with its top bit masked off.
        private static unsafe string KernelVersion(uint ordinal)
        {
            using (NativeBuffer name = new NativeBuffer(Encoding.UTF8.GetBytes("xboxkrnl.exe\0")))
            using (NativeBuffer handle = new NativeBuffer(4))
            using (NativeBuffer address = new NativeBuffer(4))
            {
                if (XexGetModuleHandle(name.Address, handle.Address) < 0 ||
                    XexGetProcedureAddress(handle.U32(0), ordinal, address.Address) < 0 || address.U32(0) == 0)
                {
                    return "";
                }
                ushort* version = (ushort*)address.U32(0);
                return string.Format("{0}.{1}.{2}.{3}", version[0], version[1], version[2], version[3] & 0x7FFF);
            }
        }

        private static string Manifest(Func<uint, int> read)
        {
            using (NativeBuffer value = new NativeBuffer(8))
            {
                return read(value.Address) < 0 ? "" : Groups(value.U64(0));
            }
        }

        private static string SecondaryId()
        {
            using (NativeBuffer value = new NativeBuffer(8))
            {
                return XeKeysGetTruncatedSecondaryConsoleId(value.Address) < 0 ? "" : Groups(value.U64(0));
            }
        }

        // Four groups of four hex digits, the most significant first.
        private static string Groups(ulong value)
        {
            return string.Format("{0:X4}-{1:X4}-{2:X4}-{3:X4}", (value >> 48) & 0xFFFF, (value >> 32) & 0xFFFF,
                                 (value >> 16) & 0xFFFF, value & 0xFFFF);
        }

        // The X code: the security violations seen, DES-encrypted with a key
        // from HMAC-SHA1 of the serial number (with its NUL) and "XBOX360SSB".
        private static ulong SecurityCode(string serial)
        {
            using (NativeBuffer key = new NativeBuffer(13))
            using (NativeBuffer message = new NativeBuffer(Encoding.UTF8.GetBytes("XBOX360SSB")))
            using (NativeBuffer desKey = new NativeBuffer(8))
            using (NativeBuffer state = new NativeBuffer(0x100))
            using (NativeBuffer code = new NativeBuffer(8))
            {
                byte[] digits = Encoding.UTF8.GetBytes(serial);
                Buffer.BlockCopy(digits, 0, key.Bytes, 0, Math.Min(12, digits.Length));
                XeCryptHmacSha(key.Address, 13, message.Address, 10, 0, 0, 0, 0, desKey.Address, 8);
                XeCryptDesParity(desKey.Address, 8, desKey.Address);
                XeCryptDesKey(state.Address, desKey.Address);
                code.SetU64(0, XamGetSecurityViolationsDetected());
                XeCryptDesEcb(state.Address, code.Address, code.Address, 1);
                return code.U64(0);
            }
        }

        // F: a Kinect sensor's bit 3, when it's there and not in trouble.
        private static string Kinect()
        {
            using (NativeBuffer status = new NativeBuffer(0x40))
            {
                XamNuiGetDeviceStatus(status.Address);
                uint flags = status.U32(0x0C);
                return (flags & 1) != 0 && (flags & 0x40) == 0 ? ((flags >> 3) & 1).ToString() : "";
            }
        }

        private static string KinectSupport()
        {
            using (NativeBuffer text = new NativeBuffer(0x40))
            {
                XamNuiGetSupportString(text.Address, 0x40);
                return text.Ansi(0, 0x40);
            }
        }

        // The hidden sequence: LT, RT, X, Y, LB, RB, then up to four buttons
        // from console setting 2/5 (A, X, Y, U, D, L, R for the D-pad). A wrong
        // one in the first six starts it over; after them it fails at the end.
        public const string LeftTrigger = "LT";
        public const string RightTrigger = "RT";

        public static List<string> ResetSequence()
        {
            List<string> sequence = new List<string> { LeftTrigger, RightTrigger, "X", "Y", "LB", "RB" };
            byte[] key = XConfig.Get(XConfig.ConsoleCategory, 5, 4);
            if (key != null)
            {
                foreach (byte b in key)
                {
                    string button = b == 'A' ? "A" : b == 'X' ? "X" : b == 'Y' ? "Y" : b == 'U' ? "Up"
                        : b == 'D' ? "Down" : b == 'L' ? "Left" : b == 'R' ? "Right" : null;
                    if (button == null)
                    {
                        break;
                    }
                    sequence.Add(button);
                }
            }
            return sequence;
        }

        public const string ResetTitle = "Reset System Settings";
        public const string ResetQuestion = "Resetting your system settings will return all console settings to factory defaults. " +
                                            "Do you want to reset to factory defaults?";

        /// <summary>
        /// The console's settings back to the factory's, as the Xbox Dashboard
        /// does: the user, media center, console and network categories
        /// cleared (console setting 0x14 kept), the remote back to both, and
        /// the console restarted.
        /// </summary>
        public static void ResetSystemSettings()
        {
            Log.Write("Resetting the console's settings");
            byte[] zeros = new byte[0x74C];
            XConfig.Set(3, 0, Slice(zeros, 0x1FD));
            XConfig.Set(6, 0, zeros);
            byte[] kept = XConfig.Get(7, 0x14, 0x1A);
            XConfig.Set(7, 0, Slice(zeros, 0x1F9));
            XConfig.Set(5, 1, Slice(zeros, 0x1F0));
            XConfig.Set(4, 1, Slice(zeros, 0x1F0));
            if (kept != null)
            {
                XConfig.Set(7, 0x14, kept);
            }
            XConfig.SmcSend(0x95, 0x01);
            XConfig.ReturnToFirmware(6);
        }

        private static byte[] Slice(byte[] bytes, int length)
        {
            byte[] slice = new byte[length];
            Buffer.BlockCopy(bytes, 0, slice, 0, length);
            return slice;
        }
    }
}
