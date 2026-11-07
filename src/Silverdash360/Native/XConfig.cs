using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silverlight360;

namespace Silverdash360.Native
{
    /// <summary>
    /// The console's settings store (XConfig): settings by category and
    /// number, as the Xbox Dashboard's Console Settings read and write them
    /// (dash 17559), and the calls that apply some of them at once.
    /// </summary>
    public static class XConfig
    {
        public const ushort ConsoleCategory = 2;
        public const ushort UserCategory = 3;
        public const ushort XboxCategory = 7;

        // 0x10: NTSTATUS ExGetXConfigSetting(WORD category, WORD setting,
        //   PVOID buffer, WORD size, PWORD sizeRead)
        [DllImport("xboxkrnl.exe", EntryPoint = "#16")]
        private static extern int ExGetXConfigSetting(ushort category, ushort setting, uint buffer, ushort size, uint sizeRead);

        // 0x18: NTSTATUS ExSetXConfigSetting(WORD category, WORD setting, PVOID buffer, WORD size)
        [DllImport("xboxkrnl.exe", EntryPoint = "#24")]
        private static extern int ExSetXConfigSetting(ushort category, ushort setting, uint buffer, ushort size);

        // 0x2BC: NTSTATUS ExReadModifyWriteXConfigSettingUlong(WORD category,
        //   WORD setting, DWORD andMask, DWORD orMask): (old & and) | or.
        [DllImport("xboxkrnl.exe", EntryPoint = "#700")]
        private static extern int ExReadModifyWriteXConfigSettingUlong(ushort category, ushort setting, uint andMask, uint orMask);

        // 0x2DA: NTSTATUS VdSetOption(DWORD option, DWORD value, DWORD): views
        // of user setting 0x2C that the video driver takes at once.
        [DllImport("xboxkrnl.exe", EntryPoint = "#730")]
        private static extern int VdSetOption(uint option, uint value, uint unused);

        // 0x22: BYTE HalGetCurrentAVPack()
        [DllImport("xboxkrnl.exe", EntryPoint = "#34")]
        private static extern uint HalGetCurrentAVPack();

        // 0x27E: BOOL VdGetDisplayDiscoveryData(PVOID data): the display's
        // answers, its first word the size (0x23).
        [DllImport("xboxkrnl.exe", EntryPoint = "#638")]
        private static extern uint VdGetDisplayDiscoveryData(uint data);

        // 0x200: NTSTATUS XAudioSetSpeakerConfig(DWORD flags)
        [DllImport("xboxkrnl.exe", EntryPoint = "#512")]
        private static extern int XAudioSetSpeakerConfig(uint flags);

        // 0x29: VOID HalSendSMCMessage(PBYTE request, PBYTE reply): 16 bytes each.
        [DllImport("xboxkrnl.exe", EntryPoint = "#41")]
        private static extern void HalSendSMCMessage(uint request, uint reply);

        // 0x28: VOID HalReturnToFirmware(DWORD routine): 1 restarts the console.
        [DllImport("xboxkrnl.exe", EntryPoint = "#40")]
        private static extern void HalReturnToFirmware(uint routine);

        // 0xF9: NTSTATUS NtSetSystemTime(PLARGE_INTEGER time, PLARGE_INTEGER previous)
        [DllImport("xboxkrnl.exe", EntryPoint = "#249")]
        private static extern int NtSetSystemTime(uint time, uint previous);

        // xam 0xB43: XamSetCountry(BYTE): checks it, writes user setting 0xE
        // and turns the 24-hour clock on for most countries.
        [DllImport("xam.xex", EntryPoint = "#2883")]
        private static extern uint XamSetCountry(uint country);

        // xam 0x1F4: DWORD XMsgInProcessCall(DWORD app, DWORD message, PVOID, PVOID)
        [DllImport("xam.xex", EntryPoint = "#500")]
        private static extern uint XMsgInProcessCall(uint app, uint message, uint param1, uint param2);

        // xam 0x1F6: DWORD XMsgSystemProcessCall(DWORD app, DWORD message, PVOID, DWORD)
        [DllImport("xam.xex", EntryPoint = "#502")]
        private static extern uint XMsgSystemProcessCall(uint app, uint message, uint param1, uint param2);

        // xam 0x7B: NetDll_XnpNoteSystemTime(DWORD)
        [DllImport("xam.xex", EntryPoint = "#123")]
        private static extern void NetDll_XnpNoteSystemTime(uint caller);

        // xam 0x3CC: DWORD XGetGameRegion()
        [DllImport("xam.xex", EntryPoint = "#972")]
        private static extern uint XGetGameRegion();

        // 0x270: VOID VdEnumerateVideoModes(BOOL (*callback)(const VD_ENUM_MODE*, PVOID), PVOID context):
        // calls back with each mode the display takes, until the callback returns 0.
        private delegate int ModeCallback(uint mode, uint context);

        [DllImport("xboxkrnl.exe", EntryPoint = "#624")]
        private static extern void VdEnumerateVideoModes(ModeCallback callback, uint context);

        // 0x1D4: BOOL VdSetDisplayModeOverride(DWORD width, DWORD height, FLOAT refresh,
        //   DWORD scan /* 1 interlaced, 2 progressive */, const OVERRIDE* options):
        // records a mode for the display's next start (it doesn't change the
        // picture by itself); FALSE when the kernel has no timing for it.
        // Width 0 clears it.
        [DllImport("xboxkrnl.exe", EntryPoint = "#468")]
        private static extern int VdSetDisplayModeOverride(uint width, uint height, float refresh, uint scan, uint options);

        // xam 0x50B: XamGetConsoleFriendlyName(WCHAR name[24])
        [DllImport("xam.xex", EntryPoint = "#1291")]
        private static extern uint XamGetConsoleFriendlyName(uint name);

        // xam 0x50C: XamSetConsoleFriendlyName(const WCHAR* name): 23 characters at most.
        [DllImport("xam.xex", EntryPoint = "#1292")]
        private static extern uint XamSetConsoleFriendlyName(uint name);

        // xam 0x1CA: VOID XamMuteSound(BOOL)
        [DllImport("xam.xex", EntryPoint = "#458")]
        private static extern void XamMuteSound(uint mute);

        // xam 0x1A2: VOID XamSetInactivityTimeFromConfig(BOOL)
        [DllImport("xam.xex", EntryPoint = "#418")]
        private static extern void XamSetInactivityTimeFromConfig(uint autoOff);

        /// <summary>A setting's bytes, or null when it can't be read.</summary>
        public static byte[] Get(ushort category, ushort setting, int size)
        {
            using (NativeBuffer buffer = new NativeBuffer(size + 4))
            {
                int status = ExGetXConfigSetting(category, setting, buffer.Address, (ushort)size, buffer.Address + (uint)size);
                return status < 0 ? null : buffer.Slice(0, size);
            }
        }

        public static void Set(ushort category, ushort setting, byte[] value)
        {
            using (NativeBuffer buffer = new NativeBuffer(value))
            {
                Check(ExSetXConfigSetting(category, setting, buffer.Address, (ushort)value.Length), category, setting);
            }
        }

        public static uint GetU32(ushort category, ushort setting)
        {
            byte[] value = Get(category, setting, 4);
            if (value == null)
            {
                throw new InvalidOperationException(string.Format("Setting {0}/{1:X} can't be read.", category, setting));
            }
            return BigEndian.U32(value, 0);
        }

        public static void SetU32(ushort category, ushort setting, uint value)
        {
            byte[] bytes = new byte[4];
            BigEndian.SetU32(bytes, 0, value);
            Set(category, setting, bytes);
        }

        public static ushort GetU16(ushort category, ushort setting)
        {
            byte[] value = Get(category, setting, 2);
            if (value == null)
            {
                throw new InvalidOperationException(string.Format("Setting {0}/{1:X} can't be read.", category, setting));
            }
            return (ushort)((value[0] << 8) | value[1]);
        }

        public static void SetU16(ushort category, ushort setting, ushort value)
        {
            Set(category, setting, new[] { (byte)(value >> 8), (byte)value });
        }

        /// <summary>Changes the bits of a 4-byte setting in one step: (old &amp; ~mask) | value.</summary>
        public static void SetBits(ushort category, ushort setting, uint mask, uint value)
        {
            ReadModifyWrite(category, setting, ~mask, value & mask);
        }

        /// <summary>A 4-byte setting changed in one step: (old &amp; andMask) | orMask.</summary>
        public static void ReadModifyWrite(ushort category, ushort setting, uint andMask, uint orMask)
        {
            Check(ExReadModifyWriteXConfigSettingUlong(category, setting, andMask, orMask), category, setting);
        }

        private static void Check(int status, ushort category, ushort setting)
        {
            if (status < 0)
            {
                throw new InvalidOperationException(string.Format("Setting {0}/{1:X} wasn't written ({2:X8}).", category, setting, status));
            }
        }

        public static void SetVideoOption(uint option, uint value)
        {
            int status = VdSetOption(option, value, 0);
            if (status < 0)
            {
                throw new InvalidOperationException(string.Format("Video option {0} wasn't set ({1:X8}).", option, status));
            }
        }

        /// <summary>The AV cable: 3 component HD, 4 HDMI with audio, 6 VGA, 8 HDMI or DVI, and so on.</summary>
        public static uint AVPack()
        {
            return HalGetCurrentAVPack() & 0xFF;
        }

        /// <summary>
        /// What the display told the console, 0x24 bytes, or null when it
        /// says nothing. The Xbox Dashboard reads 4 (bit 0: a TV that takes
        /// 3D and tells its size), 0x15 and 0x16 (its width and height in
        /// cm) and 0x1D, unaligned (3 for a display with color spaces).
        /// </summary>
        public static byte[] DisplayDiscovery()
        {
            using (NativeBuffer buffer = new NativeBuffer(0x40))
            {
                buffer.SetU32(0, 0x23);
                return VdGetDisplayDiscoveryData(buffer.Address) != 0 ? buffer.Slice(0, 0x24) : null;
            }
        }

        public static void SetSpeakerConfig(uint flags)
        {
            int status = XAudioSetSpeakerConfig(flags);
            if (status < 0)
            {
                throw new InvalidOperationException(string.Format("The speakers weren't set ({0:X8}).", status));
            }
        }

        /// <summary>A message to the system controller, 16 bytes; its reply, 16 bytes.</summary>
        public static byte[] Smc(params byte[] message)
        {
            using (NativeBuffer request = new NativeBuffer(16))
            using (NativeBuffer reply = new NativeBuffer(16))
            {
                Buffer.BlockCopy(message, 0, request.Bytes, 0, message.Length);
                HalSendSMCMessage(request.Address, reply.Address);
                return reply.Slice(0, 16);
            }
        }

        /// <summary>
        /// A command to the system controller that sends no reply, as the
        /// set commands (0x8x, 0x9x) are: given a reply buffer,
        /// HalSendSMCMessage waits for one with the kernel held, and the
        /// console stops (kernel 17559, 0x80067FCC skips the wait for none).
        /// </summary>
        public static void SmcSend(params byte[] message)
        {
            using (NativeBuffer request = new NativeBuffer(16))
            {
                Buffer.BlockCopy(message, 0, request.Bytes, 0, message.Length);
                HalSendSMCMessage(request.Address, 0);
            }
        }

        public static void MuteSound(bool mute)
        {
            XamMuteSound(mute ? 1u : 0u);
        }

        public static void ApplyInactivityTime(bool autoOff)
        {
            XamSetInactivityTimeFromConfig(autoOff ? 1u : 0u);
        }

        /// <summary>Sets the system clock, in UTC.</summary>
        public static void SetSystemTime(DateTime utc)
        {
            long fileTime = utc.Ticks - new DateTime(1601, 1, 1).Ticks;
            using (NativeBuffer time = new NativeBuffer(8))
            {
                time.SetU64(0, (ulong)fileTime);
                int status = NtSetSystemTime(time.Address, 0);
                if (status < 0)
                {
                    throw new InvalidOperationException(string.Format("The clock wasn't set ({0:X8}).", status));
                }
            }
        }

        /// <summary>
        /// Whether Xbox Live sets the clock, so it can't be changed: the
        /// Xbox Dashboard's test (dash 17559), which takes a failure or
        /// 0x001510F1 for no.
        /// </summary>
        public static bool ClockSetByLive()
        {
            uint result = XMsgInProcessCall(0xFC, 0x00058003, 0, 0);
            return (result & 0x80000000) == 0 && result != 0x001510F1;
        }

        /// <summary>The Xbox Live logon's status as the Xbox Dashboard asks it: 0x001510F0 when connected.</summary>
        public static uint LiveStatus()
        {
            return XMsgInProcessCall(0xFC, 0x00058003, 0, 0);
        }

        /// <summary>Tells the network stack the clock was set, as the Xbox Dashboard does after setting it.</summary>
        public static void NoteSystemTime()
        {
            NetDll_XnpNoteSystemTime(1);
        }

        public static void SetCountry(uint country)
        {
            XamSetCountry(country);
        }

        /// <summary>Tells the system the language or locale changed, as the Xbox Dashboard does.</summary>
        public static void NotifyLanguageChanged()
        {
            XMsgSystemProcessCall(0xFE, 0x8000000D, 0, 0);
        }

        public static uint GameRegion()
        {
            return XGetGameRegion();
        }

        /// <summary>A mode the display takes, as VdEnumerateVideoModes gives it.</summary>
        public sealed class VideoMode
        {
            public int Width;
            public int Height;
            public float Refresh;
            public bool Interlaced;
            public bool Widescreen;
            /// <summary>The display's own best mode (byte 7 is 3), which the Xbox Dashboard calls Optimal Resolution.</summary>
            public bool Preferred;
        }

        /// <summary>The modes the display takes, in the kernel's order.</summary>
        public static List<VideoMode> VideoModes()
        {
            List<VideoMode> modes = new List<VideoMode>();
            ModeCallback callback = delegate(uint mode, uint context)
            {
                modes.Add(ReadMode(mode));
                return 1;
            };
            VdEnumerateVideoModes(callback, 0);
            GC.KeepAlive(callback);
            return modes;
        }

        // VD_ENUM_MODE (kernel 17559, 0x800C39A8): u16 size at 0, u16 width
        // at 2, u16 height at 4, u8 at 7 (3 for the preferred mode), float
        // refresh at 8, flags at 0xC (bit 1 4:3, bit 2 interlaced).
        private static unsafe VideoMode ReadMode(uint address)
        {
            byte* mode = (byte*)address;
            uint flags = *(uint*)(mode + 0xC);
            return new VideoMode
            {
                Width = *(ushort*)(mode + 2),
                Height = *(ushort*)(mode + 4),
                Preferred = mode[7] == 3,
                Refresh = *(float*)(mode + 8),
                Widescreen = (flags & 2) == 0,
                Interlaced = (flags & 4) != 0,
            };
        }

        /// <summary>
        /// Records a mode for the display's next start, at one of the
        /// kernel's timings for it (the Xbox Dashboard's 59.94 Hz); false when
        /// it has no such timing.
        /// </summary>
        public static bool OverrideDisplayMode(int width, int height, bool interlaced, int timing)
        {
            using (NativeBuffer options = new NativeBuffer(16))
            {
                options.SetU32(0, 0x10);
                options.Bytes[0x0C] = (byte)timing;
                return VdSetDisplayModeOverride((uint)width, (uint)height, 59.94f, interlaced ? 1u : 2u, options.Address) != 0;
            }
        }

        public static void ClearDisplayModeOverride()
        {
            VdSetDisplayModeOverride(0, 0, 0f, 0, 0);
        }

        public static string ConsoleName()
        {
            using (NativeBuffer name = new NativeBuffer(48))
            {
                XamGetConsoleFriendlyName(name.Address);
                return name.Utf16(0, 24);
            }
        }

        /// <summary>Names the console, 23 characters at most.</summary>
        public static void SetConsoleName(string name)
        {
            if (name.Length > 23)
            {
                name = name.Substring(0, 23);
            }
            using (NativeBuffer buffer = new NativeBuffer(48))
            {
                for (int i = 0; i < name.Length; i++)
                {
                    buffer.SetU16(i * 2, name[i]);
                }
                uint result = XamSetConsoleFriendlyName(buffer.Address);
                if (result != 0)
                {
                    throw new InvalidOperationException(string.Format("The console wasn't named ({0:X8}).", result));
                }
            }
        }

        /// <summary>Restarts the console.</summary>
        public static void Restart()
        {
            HalReturnToFirmware(1);
        }

        /// <summary>HalReturnToFirmware: 1 restarts, 6 restarts as after a factory reset.</summary>
        public static void ReturnToFirmware(uint routine)
        {
            HalReturnToFirmware(routine);
        }
    }
}
