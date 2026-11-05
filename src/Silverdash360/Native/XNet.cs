using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Silverdash360.Native
{
    /// <summary>
    /// The network settings, as the Xbox Dashboard's Network Settings (dash
    /// 2.0.17559) read and write them: xam's network configuration
    /// (XnpLoad/SaveConfigParams, kept encrypted in XConfig category 5) and
    /// the wireless adapter's options. The NetDll calls take a caller first;
    /// the Xbox Dashboard passes 1.
    /// </summary>
    public static class XNet
    {
        private const uint Caller = 1;

        // xam 0x65: NetDll_XnpLoadConfigParams(caller, XNetConfigParams* /*0x1EC*/)
        [DllImport("xam.xex", EntryPoint = "#101")]
        private static extern int XnpLoadConfigParams(uint caller, uint parameters);

        // xam 0x66: NetDll_XnpSaveConfigParams(caller, XNetConfigParams*)
        [DllImport("xam.xex", EntryPoint = "#102")]
        private static extern int XnpSaveConfigParams(uint caller, uint parameters);

        // xam 0x68: NetDll_XnpConfig(caller, XNetConfigParams*, DWORD flags): 0 takes them into use.
        [DllImport("xam.xex", EntryPoint = "#104")]
        private static extern int XnpConfig(uint caller, uint parameters, uint flags);

        // xam 0x69: NetDll_XnpGetConfigStatus(caller, XNetConfigStatus* /*0x3A*/)
        [DllImport("xam.xex", EntryPoint = "#105")]
        private static extern int XnpGetConfigStatus(uint caller, uint status);

        // xam 0x4B: DWORD NetDll_XNetGetEthernetLinkStatus(caller): bit 0 link, 0x20 wireless.
        [DllImport("xam.xex", EntryPoint = "#75")]
        private static extern uint XNetGetEthernetLinkStatus(uint caller);

        // xam 0x4E: NetDll_XNetGetOpt(caller, DWORD option, PBYTE value, PDWORD length)
        [DllImport("xam.xex", EntryPoint = "#78")]
        private static extern int XNetGetOpt(uint caller, uint option, uint value, uint length);

        // xam 0x4F: NetDll_XNetSetOpt(caller, DWORD option, const BYTE* value, DWORD length)
        [DllImport("xam.xex", EntryPoint = "#79")]
        private static extern int XNetSetOpt(uint caller, uint option, uint value, uint length);

        public const int ParametersSize = 0x1EC;

        /// <summary>The network configuration, its plaintext from 0x1C on (see <see cref="NetConfig"/>).</summary>
        public static byte[] LoadParameters()
        {
            using (NativeBuffer buffer = new NativeBuffer(ParametersSize))
            {
                XnpLoadConfigParams(Caller, buffer.Address);
                return buffer.Slice(0, ParametersSize);
            }
        }

        /// <summary>Saves the configuration and takes it into use, as the Xbox Dashboard does (0x922DF4F8).</summary>
        public static void SaveParameters(byte[] parameters)
        {
            using (NativeBuffer buffer = new NativeBuffer(parameters))
            {
                int result = XnpSaveConfigParams(Caller, buffer.Address);
                if (result != 0)
                {
                    XnpConfig(Caller, buffer.Address, 0);
                }
            }
        }

        /// <summary>What the network uses now: flags, IP, mask, gateway, DNS (0x3A bytes).</summary>
        public static byte[] ConfigStatus()
        {
            using (NativeBuffer buffer = new NativeBuffer(0x40))
            {
                XnpGetConfigStatus(Caller, buffer.Address);
                return buffer.Slice(0, 0x3A);
            }
        }

        public static uint LinkStatus()
        {
            return XNetGetEthernetLinkStatus(Caller);
        }

        /// <summary>A wireless option's bytes, or null when the adapter doesn't answer it.</summary>
        public static byte[] GetOption(uint option, int size)
        {
            using (NativeBuffer value = new NativeBuffer(size))
            using (NativeBuffer length = new NativeBuffer(4))
            {
                length.SetU32(0, (uint)size);
                int result = XNetGetOpt(Caller, option, size == 0 ? 0 : value.Address, length.Address);
                if (result != 0)
                {
                    return null;
                }
                return value.Slice(0, Math.Min(size, (int)length.U32(0)));
            }
        }

        public static void SetOption(uint option, byte[] value)
        {
            using (NativeBuffer buffer = new NativeBuffer(value))
            {
                int result = XNetSetOpt(Caller, option, buffer.Address, (uint)value.Length);
                if (result != 0)
                {
                    throw new InvalidOperationException(string.Format("The wireless option {0:X8} wasn't set ({1:X8}).", option, result));
                }
            }
        }

        /// <summary>Starts a wireless scan (OID_802_11_BSSID_LIST_SCAN, asked with no buffer).</summary>
        public static void StartScan()
        {
            using (NativeBuffer length = new NativeBuffer(4))
            {
                XNetGetOpt(Caller, 0x0D01021A, 0, length.Address);
            }
        }

        public static string FormatAddress(uint address)
        {
            return string.Format("{0}.{1}.{2}.{3}", address >> 24, (address >> 16) & 0xFF, (address >> 8) & 0xFF, address & 0xFF);
        }

        /// <summary>A dotted quad, or null when it isn't one; 0.0.0.0, 255.255.255.255 and 127.0.0.1 aren't, as for the Xbox Dashboard.</summary>
        public static uint? ParseAddress(string text)
        {
            string[] parts = (text ?? "").Trim().Split('.');
            if (parts.Length != 4)
            {
                return null;
            }
            uint address = 0;
            foreach (string part in parts)
            {
                uint value;
                if (!uint.TryParse(part, out value) || value > 255)
                {
                    return null;
                }
                address = (address << 8) | value;
            }
            if (address == 0 || address == 0xFFFFFFFF || address == 0x7F000001)
            {
                return null;
            }
            return address;
        }
    }
}
