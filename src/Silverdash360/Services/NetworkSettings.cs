using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Ftp;

namespace Silverdash360.Services
{
    /// <summary>
    /// The Xbox Dashboard's Network Settings (dash 2.0.17559), its pages and
    /// items in its order: Configure Network (IP, DNS, wireless, PPPoE,
    /// advanced settings, factory defaults), Test Xbox Live Connection,
    /// Network Statistics and the network's status. The wired settings are
    /// xam's configuration (<see cref="XNet.LoadParameters"/>), edited in a
    /// copy that a page's Done saves and takes into use at once, as the Xbox
    /// Dashboard does; the wireless network is the adapter's, set through its
    /// options. Its PC and Windows Media Center tests, ad-hoc networks and
    /// wireless bridges aren't here.
    /// </summary>
    public static class NetworkSettings
    {
        public const string Page = "Network Settings";
        public const string Configure = "Configure Network";
        public const string IpPage = "IP Settings";
        public const string DnsPage = "DNS Settings";
        public const string WirelessPage = "Wireless";
        public const string PppoePage = "PPPoE Settings";
        public const string AdvancedPage = "Advanced Settings";
        public const string TestPage = "Test Xbox Live Connection";
        public const string StatisticsPage = "Network Statistics";

        /// <summary>Something shown changed in the background: a scan or a test moved on.</summary>
        public static event Action Changed;

        // XNetConfigParams (0x1EC, 0x9228DF5B8..): flags and fields.
        private const int Flags = 0x4C;
        private const int AlternateMac = 0x4E;
        private const int IpAddress = 0x54;
        private const int SubnetMask = 0x58;
        private const int Gateway = 0x5C;
        private const int PrimaryDns = 0x60;
        private const int SecondaryDns = 0x64;
        private const int HostName = 0x68;
        private const int PppoeUser = 0x90;
        private const int PppoePassword = 0xD0;
        private const int PppoeService = 0x138;
        private const ushort UsesAlternateMac = 0x1;
        private const ushort UsesPppoe = 0x2;
        private const ushort StaticIp = 0x4;
        private const ushort ManualDns = 0x8;
        private const ushort HasHostName = 0x10;

        // The configuration being edited, from Configure Network's opening.
        private static byte[] draft = new byte[XNet.ParametersSize];

        private static ushort DraftFlags
        {
            get { return BigEndian.U16(draft, Flags); }
            set { BigEndian.SetU16(draft, Flags, value); }
        }

        private static bool DraftFlag(ushort flag)
        {
            return (DraftFlags & flag) != 0;
        }

        private static void SetDraftFlag(ushort flag, bool on)
        {
            DraftFlags = (ushort)(on ? DraftFlags | flag : DraftFlags & ~flag);
        }

        private static string DraftText(int offset, int size)
        {
            return BigEndian.Ansi(draft, offset, size);
        }

        private static void SetDraftText(int offset, int size, string text)
        {
            Array.Clear(draft, offset, size);
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            Buffer.BlockCopy(bytes, 0, draft, offset, Math.Min(bytes.Length, size - 1));
        }

        private static SettingChoice Choice(string name, uint value)
        {
            return new SettingChoice { Name = name, Value = value };
        }

        /// <summary>Adds Network Settings' pages to Console Settings'.</summary>
        public static void Add(List<ConsoleSetting> all)
        {
            all.Add(new ConsoleSetting
            {
                Group = Page, Title = Configure, Description = "Set up your network connection",
                Opens = Configure, OnOpen = () => draft = XNet.LoadParameters(),
                Value = ConnectionType,
            });
            all.Add(new ConsoleSetting
            {
                Group = Page, Title = TestPage, Description = "See whether the console reaches the Internet and Xbox Live",
                Opens = TestPage, OnOpen = () => StartTest(TestPage),
            });
            all.Add(new ConsoleSetting
            {
                Group = Page, Title = StatisticsPage, Description = "Measure the connection's latency, packet loss and speeds",
                Opens = StatisticsPage, OnOpen = () => StartTest(StatisticsPage),
            });
            AddStatus(all);
            AddConfigure(all);
            AddIp(all);
            AddDns(all);
            AddWireless(all);
            AddPppoe(all);
            AddAdvanced(all);
            AddTest(all, TestPage);
            AddTest(all, StatisticsPage);
        }

        // The status the Xbox Dashboard shows with Y: what the network uses now.
        private static void AddStatus(List<ConsoleSetting> all)
        {
            Func<int, string> address = offset =>
            {
                uint value = BigEndian.U32(XNet.ConfigStatus(), offset);
                return value == 0 ? "Not available" : XNet.FormatAddress(value);
            };
            all.Add(Info(Page, "Connection", ConnectionType));
            all.Add(Info(Page, "IP Address", () => address(0x04)));
            all.Add(Info(Page, "Subnet Mask", () => address(0x08)));
            all.Add(Info(Page, "Gateway", () => address(0x0C)));
            all.Add(Info(Page, "Primary DNS Server", () => address(0x1C)));
            all.Add(Info(Page, "Secondary DNS Server", () => address(0x20)));
        }

        private static ConsoleSetting Info(string group, string title, Func<string> value)
        {
            return new ConsoleSetting { Group = group, Title = title, Value = value, Run = host => { } };
        }

        private static string ConnectionType()
        {
            uint link = XNet.LinkStatus();
            if ((link & 1) == 0)
            {
                return "Not connected";
            }
            if ((link & 0x20) == 0)
            {
                return "Wired";
            }
            string ssid = CurrentSsid();
            return ssid.Length > 0 ? "Wireless: " + ssid : "Wireless";
        }

        private static void AddConfigure(List<ConsoleSetting> all)
        {
            // Basic Settings: what the setting uses now when it's automatic,
            // what's set when it's manual (0x922DB7A4).
            all.Add(new ConsoleSetting
            {
                Group = Configure, Title = IpPage, Description = "Basic Settings: the console's IP address",
                Opens = IpPage, Value = () => DraftFlag(StaticIp) ? "Manual" : "Automatic",
            });
            all.Add(new ConsoleSetting
            {
                Group = Configure, Title = DnsPage, Description = "Basic Settings: the DNS servers",
                Opens = DnsPage, Value = () => DraftFlag(ManualDns) ? "Manual" : "Automatic",
            });
            if (WirelessAdapter())
            {
                all.Add(new ConsoleSetting
                {
                    Group = Configure, Title = WirelessPage, Description = "Basic Settings: the wireless network",
                    Opens = WirelessPage, OnOpen = Scan,
                    Value = () =>
                    {
                        string ssid = CurrentSsid();
                        return ssid.Length > 0 ? ssid : "Not set";
                    },
                });
            }
            all.Add(new ConsoleSetting
            {
                Group = Configure, Title = PppoePage, Description = "Additional Settings: PPPoE, for an Internet connection that asks for a user name",
                Opens = PppoePage, Value = () => DraftFlag(UsesPppoe) ? DraftText(PppoeUser, 0x40) : "Off",
            });
            all.Add(new ConsoleSetting
            {
                Group = Configure, Title = AdvancedPage, Description = "Additional Settings: host name and alternate MAC address",
                Opens = AdvancedPage,
            });
            all.Add(new ConsoleSetting
            {
                Group = Configure, Title = "Restore to Factory Defaults", Description = "Additional Settings: forget all network settings",
                Run = host => host.Confirm("Restore to Factory Defaults",
                                           "Are you sure you want to restore your network settings to factory defaults?", delegate
                {
                    // As the Xbox Dashboard (0x922DC2C0): the wireless network
                    // forgotten and the configuration cleared, then saved.
                    if (WirelessAdapter())
                    {
                        Forget();
                    }
                    draft = new byte[XNet.ParametersSize];
                    Save(host, "Your network settings are back to the factory defaults.");
                }),
            });
        }

        private static void AddIp(List<ConsoleSetting> all)
        {
            all.Add(new ConsoleSetting
            {
                Group = IpPage, Title = "IP Settings", Effect = SettingEffect.Draft,
                Choices = new List<SettingChoice> { Choice("Automatic", 0), Choice("Manual", 1) },
                Read = () => DraftFlag(StaticIp) ? 1u : 0u,
                Write = v => SetDraftFlag(StaticIp, v == 1),
            });
            Func<string> automatic = () => DraftFlag(StaticIp) ? null : "Choose Manual to set this.";
            all.Add(AddressField(IpPage, "IP Address", IpAddress, automatic, false));
            all.Add(AddressField(IpPage, "Subnet Mask", SubnetMask, automatic, true));
            all.Add(AddressField(IpPage, "Gateway", Gateway, automatic, false));
            all.Add(Done(IpPage, () => !DraftFlag(StaticIp) || (BigEndian.U32(draft, IpAddress) != 0 && BigEndian.U32(draft, SubnetMask) != 0)
                ? null : "Enter the IP address and subnet mask."));
        }

        private static void AddDns(List<ConsoleSetting> all)
        {
            all.Add(new ConsoleSetting
            {
                Group = DnsPage, Title = "DNS Settings", Effect = SettingEffect.Draft,
                Choices = new List<SettingChoice> { Choice("Automatic", 0), Choice("Manual", 1) },
                Read = () => DraftFlag(ManualDns) ? 1u : 0u,
                Write = v => SetDraftFlag(ManualDns, v == 1),
            });
            Func<string> automatic = () => DraftFlag(ManualDns) ? null : "Choose Manual to set this.";
            all.Add(AddressField(DnsPage, "Primary DNS Server", PrimaryDns, automatic, false));
            all.Add(AddressField(DnsPage, "Secondary DNS Server", SecondaryDns, automatic, false));
            all.Add(Done(DnsPage, () => !DraftFlag(ManualDns) || BigEndian.U32(draft, PrimaryDns) != 0
                ? null : "Enter the primary DNS server."));
        }

        // An address entered with the keyboard, 15 characters at most, checked
        // as the Xbox Dashboard checks it (a mask's bits from the top).
        private static ConsoleSetting AddressField(string group, string title, int offset, Func<string> disabled, bool mask)
        {
            return new ConsoleSetting
            {
                Group = group, Title = title, Disabled = disabled,
                Value = () =>
                {
                    uint value = BigEndian.U32(draft, offset);
                    return value == 0 ? "Not set" : XNet.FormatAddress(value);
                },
                Run = host =>
                {
                    uint value = BigEndian.U32(draft, offset);
                    host.Ask(title, "Enter the " + title.ToLowerInvariant() + ", such as 192.168.1.10.",
                             value == 0 ? "" : XNet.FormatAddress(value), typed =>
                    {
                        uint? address = XNet.ParseAddress(typed);
                        if (address == null || (mask && (~address.Value & (~address.Value + 1)) != 0))
                        {
                            host.Status(typed + " isn't a valid " + title.ToLowerInvariant() + ".");
                            return;
                        }
                        BigEndian.SetU32(draft, offset, address.Value);
                        host.Refresh();
                    });
                },
            };
        }

        // Saves the page's settings and takes them into use.
        private static ConsoleSetting Done(string group, Func<string> missing)
        {
            return new ConsoleSetting
            {
                Group = group, Title = "Done", Description = "Save these settings",
                Run = host =>
                {
                    string why = missing != null ? missing() : null;
                    if (why != null)
                    {
                        host.Status(why);
                        return;
                    }
                    Save(host, "Your network settings are saved.");
                },
            };
        }

        private static void Save(ISettingHost host, string done)
        {
            try
            {
                XNet.SaveParameters(draft);
                Log.Write("Network settings saved: flags {0:X4}", DraftFlags);
                host.Status(done);
                host.Back();
            }
            catch (Exception e)
            {
                Log.Write("Network settings weren't saved: {0}", e);
                host.Status("Your network settings weren't saved: " + e.Message);
            }
        }

        private static void AddPppoe(List<ConsoleSetting> all)
        {
            all.Add(TextField(PppoePage, "User Name", PppoeUser, 0x40, false));
            all.Add(TextField(PppoePage, "Password", PppoePassword, 0x40, true));
            all.Add(TextField(PppoePage, "Service Name", PppoeService, 0x28, false));
            all.Add(new ConsoleSetting
            {
                Group = PppoePage, Title = "Clear Settings", Description = "Turn PPPoE off and clear its settings",
                Run = host =>
                {
                    SetDraftText(PppoeUser, 0x40, "");
                    SetDraftText(PppoePassword, 0x40, "");
                    SetDraftText(PppoeService, 0x28, "");
                    Save(host, "PPPoE is off.");
                },
            });
            all.Add(new ConsoleSetting
            {
                Group = PppoePage, Title = "Done", Description = "Save these settings",
                Run = host => Save(host, "Your PPPoE settings are saved."),
            });
        }

        // Text entered with the keyboard, as the PPPoE fields and the host
        // name (63 or 39 characters at most). PPPoE is on with a user name.
        private static ConsoleSetting TextField(string group, string title, int offset, int size, bool secret)
        {
            return new ConsoleSetting
            {
                Group = group, Title = title,
                Value = () =>
                {
                    string text = DraftText(offset, size);
                    return text.Length == 0 ? "Not set" : secret ? new string('*', text.Length) : text;
                },
                Run = host => host.Ask(title, "Enter the " + title.ToLowerInvariant() + ".", secret ? "" : DraftText(offset, size), typed =>
                {
                    SetDraftText(offset, size, typed.Length > size - 1 ? typed.Substring(0, size - 1) : typed);
                    if (group == PppoePage)
                    {
                        SetDraftFlag(UsesPppoe, DraftText(PppoeUser, 0x40).Length > 0);
                    }
                    else if (offset == HostName)
                    {
                        SetDraftFlag(HasHostName, DraftText(HostName, 0x28).Length > 0);
                    }
                    host.Refresh();
                }),
            };
        }

        private static void AddAdvanced(List<ConsoleSetting> all)
        {
            all.Add(TextField(AdvancedPage, "Host Name", HostName, 0x28, false));
            all.Add(new ConsoleSetting
            {
                Group = AdvancedPage, Title = "Alternate MAC Address", Description = "12 hexadecimal digits; empty for none",
                Value = () => DraftFlag(UsesAlternateMac) ? FormatMac() : "Not set",
                Run = host => host.Ask("Alternate MAC Address", "Enter the alternate MAC address: 12 hexadecimal digits.",
                                       DraftFlag(UsesAlternateMac) ? FormatMac().Replace("-", "") : "", typed =>
                {
                    string hex = typed.Replace("-", "").Replace(":", "").Trim();
                    if (hex.Length == 0)
                    {
                        SetDraftFlag(UsesAlternateMac, false);
                        host.Refresh();
                        return;
                    }
                    // Twelve hex digits, the second even (0x922DF8C4).
                    if (hex.Length != 12 || !hex.All(Uri.IsHexDigit) || Convert.ToInt32(hex.Substring(1, 1), 16) % 2 != 0)
                    {
                        host.Status(typed + " isn't a valid MAC address.");
                        return;
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        draft[AlternateMac + i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    }
                    SetDraftFlag(UsesAlternateMac, true);
                    host.Refresh();
                }),
            });
            all.Add(new ConsoleSetting
            {
                Group = AdvancedPage, Title = "Done", Description = "Save these settings",
                Run = host => Save(host, "Your network settings are saved."),
            });
        }

        private static string FormatMac()
        {
            return string.Join("-", Enumerable.Range(0, 6).Select(i => draft[AlternateMac + i].ToString("X2")).ToArray());
        }

        // Wireless, through the adapter's options (0x922D3EA0, 0x922D4230).
        private const uint OptSsid = 0x0D010102;
        private const uint OptInfrastructure = 0x0D010108;
        private const uint OptNetworkType = 0x0D010204;
        private const uint OptBssidList = 0x0D010217;
        private const uint OptAdapterStatus = 0x2AF9;
        private const uint OptSecurity = 0x2AFA;
        private const uint OptRssi = 0x0D010206;
        private const uint OptLinkSpeed = 0x2B00;

        public sealed class Network
        {
            public string Ssid;
            public int Rssi;
            /// <summary>The kernel's security: 0 none, 1 WEP, 2 WPA, 3 WPA2, 4 one the console doesn't take.</summary>
            public uint Security;
        }

        private static List<Network> networks = new List<Network>();
        private static bool scanning;

        private static bool WirelessAdapter()
        {
            byte[] status = XNet.GetOption(OptAdapterStatus, 4);
            return status != null && (BigEndian.U32(status, 0) & 1) != 0;
        }

        private static string CurrentSsid()
        {
            byte[] ssid = XNet.GetOption(OptSsid, 0x24);
            if (ssid == null)
            {
                return "";
            }
            int length = (int)Math.Min(32, BigEndian.U32(ssid, 0));
            return Encoding.UTF8.GetString(ssid, 4, length);
        }

        // A scan, read after the 5 s the Xbox Dashboard waits.
        private static void Scan()
        {
            if (scanning)
            {
                return;
            }
            scanning = true;
            new Thread(delegate()
            {
                try
                {
                    XNet.StartScan();
                    Thread.Sleep(5000);
                    networks = ReadNetworks();
                }
                catch (Exception e)
                {
                    Log.Write("Wireless scan failed: {0}", e.Message);
                }
                scanning = false;
                RaiseChanged();
            }) { IsBackground = true, Name = "Wireless scan" }.Start();
        }

        // OID_802_11_BSSID_LIST: a count, then 0xF4-byte entries; the SSID at
        // 0xC (length) and 0x10, the RSSI at 0x34, the security at 0xEC. No
        // SSID is left out, the strongest first (0x922D4478).
        private static List<Network> ReadNetworks()
        {
            byte[] list = XNet.GetOption(OptBssidList, 0x4480);
            List<Network> found = new List<Network>();
            if (list == null)
            {
                return found;
            }
            int count = (int)BigEndian.U32(list, 0);
            for (int i = 0; i < count && 4 + (i + 1) * 0xF4 <= list.Length; i++)
            {
                int at = 4 + i * 0xF4;
                int length = (int)Math.Min(32, BigEndian.U32(list, at + 0x0C));
                if (length == 0)
                {
                    continue;
                }
                string ssid = Encoding.UTF8.GetString(list, at + 0x10, length);
                if (found.Any(n => n.Ssid == ssid))
                {
                    continue;
                }
                found.Add(new Network { Ssid = ssid, Rssi = (int)BigEndian.U32(list, at + 0x34), Security = BigEndian.U32(list, at + 0xEC) });
            }
            return found.OrderByDescending(n => n.Rssi).ToList();
        }

        private static void AddWireless(List<ConsoleSetting> all)
        {
            string current = CurrentSsid();
            if (current.Length > 0)
            {
                all.Add(new ConsoleSetting
                {
                    Group = WirelessPage, Title = "Disconnect", Description = "Forget " + current,
                    Value = () => current,
                    Run = host =>
                    {
                        Forget();
                        Save(host, "The console has forgotten " + current + ".");
                    },
                });
            }
            foreach (Network network in networks)
            {
                Network chosen = network;
                all.Add(new ConsoleSetting
                {
                    Group = WirelessPage, Title = network.Ssid,
                    Description = SecurityName(network.Security),
                    Value = () => chosen.Ssid == current ? "Connected" : Bars(chosen.Rssi),
                    Disabled = () => chosen.Security > 3 ? "The console can't join this kind of network." : null,
                    Run = host => Join(host, chosen),
                });
            }
            all.Add(new ConsoleSetting
            {
                Group = WirelessPage, Title = scanning ? "Scanning..." : networks.Count == 0 ? "No networks found: scan again" : "Scan Again",
                Description = "Look for wireless networks",
                Run = host =>
                {
                    Scan();
                    host.Refresh();
                },
            });
        }

        private static string SecurityName(uint security)
        {
            switch (security)
            {
                case 0: return "Unsecured";
                case 1: return "WEP";
                case 2: return "WPA";
                case 3: return "WPA2";
                default: return "Not supported";
            }
        }

        // The Xbox Dashboard's bars from the RSSI (0x922D5178).
        private static string Bars(int rssi)
        {
            int bars = rssi < -90 ? 0 : rssi < -81 ? 1 : rssi < -71 ? 2 : rssi < -67 ? 3 : rssi < -57 ? 4 : 5;
            return new string('|', bars).PadRight(5, '.');
        }

        private static void Join(ISettingHost host, Network network)
        {
            if (network.Security == 0)
            {
                host.Confirm(network.Ssid, "This network is not secured. Anyone nearby can see what you send over it. Connect anyway?",
                             () => Connect(host, network, new byte[0]));
                return;
            }
            string kind = network.Security == 1 ? "WEP key: 5 or 13 characters, or 10 or 26 hexadecimal digits"
                                                : "password: 8 to 63 characters, or 64 hexadecimal digits";
            host.Ask(network.Ssid, "Enter the " + kind + ".", "", typed =>
            {
                byte[] key = Key(network.Security, typed);
                if (key == null)
                {
                    host.Status("That isn't a valid " + (network.Security == 1 ? "WEP key." : "password."));
                    return;
                }
                Connect(host, network, key);
            });
        }

        // The key as the adapter takes it (0x922D3B28, 0x922E0720): WEP 5 or
        // 13 bytes, as text or hex; WPA a passphrase of 8 to 63 characters,
        // or 64 hex digits for 32 bytes.
        private static byte[] Key(uint security, string typed)
        {
            bool hex = typed.All(Uri.IsHexDigit);
            if (security == 1)
            {
                if (typed.Length == 5 || typed.Length == 13)
                {
                    return Encoding.UTF8.GetBytes(typed);
                }
                return hex && (typed.Length == 10 || typed.Length == 26) ? FromHex(typed) : null;
            }
            if (typed.Length >= 8 && typed.Length <= 63)
            {
                return Encoding.UTF8.GetBytes(typed);
            }
            return hex && typed.Length == 64 ? FromHex(typed) : null;
        }

        private static byte[] FromHex(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        // Sets the network as the Xbox Dashboard does on saving: the mode,
        // the band, the security and key, then the SSID, which joins it.
        private static void Connect(ISettingHost host, Network network, byte[] key)
        {
            try
            {
                XNet.SetOption(OptInfrastructure, U32(1));
                XNet.SetOption(OptNetworkType, U32(4));
                byte[] security = new byte[0x48];
                BigEndian.SetU32(security, 0, network.Security);
                BigEndian.SetU32(security, 4, (uint)key.Length);
                Buffer.BlockCopy(key, 0, security, 8, Math.Min(64, key.Length));
                XNet.SetOption(OptSecurity, security);
                XNet.SetOption(OptSsid, Ssid(network.Ssid));
                Log.Write("Wireless network set: {0}", network.Ssid);
                draft = XNet.LoadParameters();
                Save(host, "Connecting to " + network.Ssid + ". Test Xbox Live Connection shows whether it worked.");
            }
            catch (Exception e)
            {
                Log.Write("Wireless network wasn't set: {0}", e);
                host.Status("The console didn't join " + network.Ssid + ": " + e.Message);
            }
        }

        // An empty SSID: the network forgotten.
        private static void Forget()
        {
            XNet.SetOption(OptSsid, Ssid(""));
        }

        private static byte[] Ssid(string ssid)
        {
            byte[] value = new byte[0x24];
            byte[] bytes = Encoding.UTF8.GetBytes(ssid);
            int length = Math.Min(32, bytes.Length);
            BigEndian.SetU32(value, 0, (uint)length);
            Buffer.BlockCopy(bytes, 0, value, 4, length);
            return value;
        }

        private static byte[] U32(uint value)
        {
            byte[] bytes = new byte[4];
            BigEndian.SetU32(bytes, 0, value);
            return bytes;
        }

        // Test Xbox Live Connection and Network Statistics: the Xbox
        // Dashboard's steps to the Live logon (0x922D8B90), each row
        // Testing..., Connected or Failed, and for the statistics then its
        // latency, upload and download tests (0x922F27E8). Rows a failed step
        // keeps from running show Blocked.
        private static readonly Dictionary<string, string> results = new Dictionary<string, string>();
        private static bool testing;
        private static string tested;
        private static readonly string[] TestRows = { "Network", "Internet", "Xbox Live" };
        private static readonly string[] StatisticsRows =
        {
            "Network", "Internet", "Xbox Live", "Latency", "Packet loss", "Upload speed", "Download speed",
        };

        private static void AddTest(List<ConsoleSetting> all, string page)
        {
            List<string> rows = Rows(page).ToList();
            if (page == StatisticsPage && WirelessAdapter())
            {
                rows.Add("Signal strength");
                rows.Add("Wi-Fi speed");
            }
            foreach (string row in rows)
            {
                string name = row;
                all.Add(Info(page, name, () =>
                {
                    string result;
                    return tested == page && results.TryGetValue(name, out result) ? result : "Unknown";
                }));
            }
            all.Add(new ConsoleSetting
            {
                Group = page, Title = testing ? "Testing..." : "Test Again", Description = "Run the test again",
                Run = host =>
                {
                    StartTest(page);
                    host.Refresh();
                },
            });
        }

        private static string[] Rows(string page)
        {
            return page == StatisticsPage ? StatisticsRows : TestRows;
        }

        private static void StartTest(string page)
        {
            if (testing)
            {
                return;
            }
            testing = true;
            tested = page;
            results.Clear();
            foreach (string row in Rows(page))
            {
                results[row] = "Blocked";
            }
            results["Network"] = results["Internet"] = results["Xbox Live"] = "Testing...";
            RaiseChanged();
            new Thread(() => RunTest(page)) { IsBackground = true, Name = "Network test" }.Start();
        }

        private static void RunTest(string page)
        {
            try
            {
                // Network: a link and an address.
                uint link = XNet.LinkStatus();
                uint address = BigEndian.U32(XNet.ConfigStatus(), 0x04);
                if ((link & 1) == 0 || address == 0)
                {
                    Fail("Network");
                    return;
                }
                Pass("Network");
                // Internet: the file Windows and the Xbox Dashboard check.
                if (!FetchNcsi())
                {
                    Fail("Internet");
                    return;
                }
                Pass("Internet");
                // Xbox Live: the logon's status, 0x001510F0 for connected.
                if (XConfig.LiveStatus() != 0x001510F0)
                {
                    Fail("Xbox Live");
                    return;
                }
                Pass("Xbox Live");
                if (page == StatisticsPage)
                {
                    RunStatistics();
                }
            }
            catch (Exception e)
            {
                Log.Write("Network test failed: {0}", e);
                foreach (string row in Rows(page).Where(r => results[r] == "Testing..."))
                {
                    results[row] = "Failed";
                }
            }
            finally
            {
                testing = false;
                RaiseChanged();
            }
        }

        private static void Pass(string row)
        {
            results[row] = "Connected";
            RaiseChanged();
        }

        // A row failed; the Live test's later rows can't be tested, and the
        // statistics' stay Blocked.
        private static void Fail(string row)
        {
            bool after = false;
            foreach (string name in Rows(tested))
            {
                if (name == row)
                {
                    results[name] = "Failed";
                    after = true;
                }
                else if (after)
                {
                    results[name] = tested == StatisticsPage ? "Blocked" : "Unknown";
                }
            }
        }

        // Network Statistics' measurements, in the Xbox Dashboard's order and
        // formats; each test has 10 s, as the Xbox Dashboard gives it.
        private static void RunStatistics()
        {
            results["Latency"] = results["Packet loss"] = "Testing...";
            RaiseChanged();
            int latency, loss;
            if (!Measure("latency", MeasureLatency, out latency, out loss))
            {
                results["Latency"] = results["Packet loss"] = "Failed";
                return;
            }
            results["Latency"] = latency + " ms";
            results["Packet loss"] = loss + "%";
            results["Upload speed"] = "Testing...";
            RaiseChanged();
            results["Upload speed"] = Speed("upload", MeasureUpload);
            if (results["Upload speed"] == "Failed")
            {
                return;
            }
            results["Download speed"] = "Testing...";
            RaiseChanged();
            results["Download speed"] = Speed("download", MeasureDownload);
            if (WirelessAdapter())
            {
                byte[] rssi = XNet.GetOption(OptRssi, 4);
                byte[] speed = XNet.GetOption(OptLinkSpeed, 4);
                results["Signal strength"] = rssi == null ? "Unknown"
                    : Math.Max(0, Math.Min(100, ((int)BigEndian.U32(rssi, 0) + 100) * 2)) + "%";
                results["Wi-Fi speed"] = speed == null ? "Unknown" : BigEndian.U32(speed, 0) / 1000000 + " Mbps";
            }
        }

        private delegate bool Latency(out int latency, out int loss);

        private static bool Measure(string name, Latency test, out int latency, out int loss)
        {
            latency = loss = 0;
            try
            {
                return test(out latency, out loss);
            }
            catch (Exception e)
            {
                Log.Write("Network statistics: the {0} test failed: {1}", name, e.Message);
                return false;
            }
        }

        // A rate in bits/s as the Xbox Dashboard shows it, or Failed for none.
        private static string Speed(string name, Func<double> test)
        {
            double bitsPerSecond;
            try
            {
                bitsPerSecond = test();
            }
            catch (Exception e)
            {
                Log.Write("Network statistics: the {0} test failed: {1}", name, e.Message);
                return "Failed";
            }
            return bitsPerSecond <= 0 ? "Failed" : ((bitsPerSecond + 5000) * 1e-6).ToString("0.00") + " Mbps";
        }

        private const int EchoCount = 250;
        private const int TestMilliseconds = 10000;
        private static readonly byte[] EchoText = Encoding.UTF8.GetBytes("Xbox One Echo Test\0");

        // Latency and packet loss: 250 UDP echoes to x1ds.xboxlive.com:3074,
        // one every 10 ms, then the replies until the deadline. A request is
        // four little-endian u16s (1, 0, 59, 23), its sequence number and the
        // text; a 59-byte reply from the server that echoes both counts once.
        private static bool MeasureLatency(out int latency, out int loss)
        {
            latency = loss = 0;
            uint server = NativeSocket.Resolve("x1ds.xboxlive.com");
            int[] sent = new int[EchoCount];
            bool[] answered = new bool[EchoCount];
            int accepted = 0;
            long total = 0;
            using (NativeSocket socket = NativeSocket.Udp())
            using (NativeBuffer reply = new NativeBuffer(256))
            {
                byte[] request = new byte[12 + EchoText.Length];
                ushort[] header = { 1, 0, 59, 23 };
                for (int i = 0; i < header.Length; i++)
                {
                    request[i * 2] = (byte)header[i];
                    request[i * 2 + 1] = (byte)(header[i] >> 8);
                }
                Array.Copy(EchoText, 0, request, 12, EchoText.Length);
                int deadline = Environment.TickCount + TestMilliseconds + 1000;
                int next = 0;
                while (Environment.TickCount - deadline < 0 && accepted < EchoCount)
                {
                    if (next < EchoCount)
                    {
                        request[8] = (byte)next;
                        request[9] = (byte)(next >> 8);
                        request[10] = request[11] = 0;
                        sent[next] = Environment.TickCount;
                        socket.SendTo(request, server, 3074);
                        next++;
                    }
                    while (socket.WaitReadable(10))
                    {
                        uint from;
                        int port;
                        int length = socket.ReceiveFrom(reply, out from, out port);
                        int now = Environment.TickCount;
                        int sequence = EchoSequence(reply.Bytes, length);
                        if (length != 59 || from != server || sequence < 0 || sequence >= next || answered[sequence])
                        {
                            continue;
                        }
                        answered[sequence] = true;
                        accepted++;
                        total += now - sent[sequence];
                    }
                }
            }
            loss = 100 - accepted * 100 / EchoCount;
            if (accepted == 0)
            {
                return false;
            }
            latency = (int)(total / accepted);
            return true;
        }

        // The sequence number before the echoed text in a reply, or -1.
        private static int EchoSequence(byte[] reply, int length)
        {
            for (int at = 4; at + EchoText.Length - 1 <= length; at++)
            {
                int i = 0;
                while (i < EchoText.Length - 1 && reply[at + i] == EchoText[i])
                {
                    i++;
                }
                if (i == EchoText.Length - 1)
                {
                    return reply[at - 4] | reply[at - 3] << 8 | reply[at - 2] << 16 | reply[at - 1] << 24;
                }
            }
            return -1;
        }

        // Upload: networktest.xboxlive.com:80 takes a 5-byte preamble, then
        // 16358-byte chunks for 10 s or 100 MB.
        private static double MeasureUpload()
        {
            using (NativeSocket socket = NativeSocket.Connect(NativeSocket.Resolve("networktest.xboxlive.com"), 80))
            using (NativeBuffer chunk = new NativeBuffer(16358))
            {
                using (NativeBuffer preamble = new NativeBuffer(new byte[] { 0x02, 0x2C, 0x6E, 0x15, 0x42 }))
                {
                    socket.Send(preamble, preamble.Length);
                }
                long bytes = 0;
                int start = Environment.TickCount;
                int elapsed = 0;
                while (elapsed < TestMilliseconds && bytes < 100000000)
                {
                    socket.Send(chunk, chunk.Length);
                    bytes += chunk.Length;
                    elapsed = Environment.TickCount - start;
                }
                return elapsed <= 0 ? 0 : bytes * 8 * 1000.0 / elapsed;
            }
        }

        // Download: an HTTP GET of the Xbox Dashboard's speed test file, read
        // for 10 s or until it ends. Raw HTTP, since the browser stack would
        // buffer the whole file before handing any of it over.
        private static double MeasureDownload()
        {
            const string host = "speedtest.xboxlive.com";
            using (NativeSocket socket = NativeSocket.Connect(NativeSocket.Resolve(host), 80))
            using (NativeBuffer buffer = new NativeBuffer(16358))
            {
                socket.Send("GET /speedtest/extraextralarge.txt HTTP/1.1\r\nHost: " + host +
                            "\r\nUser-Agent: Xbox 360 Network Statistics\r\nConnection: close\r\n\r\n");
                long bytes = 0;
                int start = Environment.TickCount;
                int elapsed = 0;
                bool header = true;
                StringBuilder head = new StringBuilder();
                while (elapsed < TestMilliseconds)
                {
                    if (!socket.WaitReadable(Math.Max(1, TestMilliseconds - elapsed)))
                    {
                        break;
                    }
                    int received = socket.Receive(buffer, buffer.Length);
                    if (received == 0)
                    {
                        break;
                    }
                    elapsed = Environment.TickCount - start;
                    if (!header)
                    {
                        bytes += received;
                        continue;
                    }
                    head.Append(Encoding.UTF8.GetString(buffer.Bytes, 0, received));
                    string text = head.ToString();
                    int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (end < 0)
                    {
                        continue;
                    }
                    if (!text.StartsWith("HTTP/1.1 2", StringComparison.Ordinal) &&
                        !text.StartsWith("HTTP/1.0 2", StringComparison.Ordinal))
                    {
                        throw new IOException("the server answered " + text.Substring(0, text.IndexOf('\r')) + ".");
                    }
                    header = false;
                    // What came after the header, counted as the bytes of
                    // the reply; the file is ASCII text, so UTF-8 keeps them.
                    bytes += Encoding.UTF8.GetByteCount(text.Substring(end + 4));
                }
                return elapsed <= 0 ? 0 : bytes * 8 * 1000.0 / elapsed;
            }
        }

        private static bool FetchNcsi()
        {
            ManualResetEvent done = new ManualResetEvent(false);
            bool ok = false;
            Background.OnUi(delegate
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new Uri("http://xncsi.xboxlive.com/XBOXNCSI.txt"));
                    request.BeginGetResponse(delegate(IAsyncResult result)
                    {
                        try
                        {
                            using (WebResponse response = request.EndGetResponse(result))
                            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            {
                                ok = reader.ReadToEnd().Length > 0;
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Write("Network test: the Internet check failed: {0}", e.Message);
                        }
                        done.Set();
                    }, null);
                }
                catch (Exception e)
                {
                    Log.Write("Network test: the Internet check couldn't start: {0}", e.Message);
                    done.Set();
                }
            });
            done.WaitOne(15000);
            return ok;
        }

        private static void RaiseChanged()
        {
            Action changed = Changed;
            if (changed != null)
            {
                Background.OnUi(changed);
            }
        }
    }
}
