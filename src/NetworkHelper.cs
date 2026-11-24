using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace PCOvladac
{
    public class NetworkInterfaceInfoItem
    {
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public bool IsPrimary { get; set; }
    }

    public static class NetworkHelper
    {
        private const string AutoStartRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegName = "PC-Ovladac";
        private const string FirewallRuleName = "PC-Ovladac";

        public static List<NetworkInterfaceInfoItem> GetLocalIPv4Addresses()
        {
            var results = new List<NetworkInterfaceInfoItem>();

            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    string descLower = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    bool isVirtual = descLower.Contains("virtual") ||
                                     descLower.Contains("vmware") ||
                                     descLower.Contains("vbox") ||
                                     descLower.Contains("hyper-v") ||
                                     descLower.Contains("vethernet") ||
                                     descLower.Contains("wsl") ||
                                     descLower.Contains("loopback") ||
                                     descLower.Contains("tailscale") ||
                                     descLower.Contains("zerotier");

                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    bool hasGateway = ipProps.GatewayAddresses != null && ipProps.GatewayAddresses.Count > 0;

                    foreach (UnicastIPAddressInformation ua in ipProps.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                        {
                            results.Add(new NetworkInterfaceInfoItem
                            {
                                Name = ni.Name,
                                IpAddress = ua.Address.ToString(),
                                IsPrimary = !isVirtual && hasGateway
                            });
                        }
                    }
                }
            }
            catch
            {
                // Záložní metoda
            }

            results.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
            return results;
        }

        public static string GetPrimaryLocalIp()
        {
            var list = GetLocalIPv4Addresses();
            if (list.Count > 0)
            {
                return list[0].IpAddress;
            }
            return "127.0.0.1";
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartRegPath, false))
                {
                    if (key == null) return false;
                    object val = key.GetValue(AppRegName);
                    return val != null && !string.IsNullOrEmpty(val.ToString());
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetAutoStart(bool enable, string exePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartRegPath, true))
                {
                    if (key == null) return false;

                    if (enable)
                    {
                        key.SetValue(AppRegName, "\"" + exePath + "\" --minimized");
                    }
                    else
                    {
                        if (key.GetValue(AppRegName) != null)
                        {
                            key.DeleteValue(AppRegName, false);
                        }
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsFirewallRulePresent(int port)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall firewall show rule name=\"" + FirewallRuleName + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                    return proc.ExitCode == 0 && output.Contains(port.ToString());
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool AddFirewallRule(int port, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                // Nejprve smažeme staré pravidlo (pokud existuje) a přidáme nové pro aktuální port + mDNS (UDP 5353)
                string commands = string.Format(
                    "/c netsh advfirewall firewall delete rule name=\"{0}\" >nul 2>&1 & " +
                    "netsh advfirewall firewall add rule name=\"{0}\" dir=in action=allow protocol=TCP localport={1} profile=private,domain & " +
                    "netsh advfirewall firewall delete rule name=\"{0}-mDNS\" >nul 2>&1 & " +
                    "netsh advfirewall firewall add rule name=\"{0}-mDNS\" dir=in action=allow protocol=UDP localport=5353 profile=private,domain",
                    FirewallRuleName, port);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = commands,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(15000);
                    if (proc.ExitCode == 0)
                    {
                        return true;
                    }
                    errorMessage = "Příkaz netsh skončil s kódem " + proc.ExitCode + ".";
                    return false;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // Uživatel zrušil UAC výzvu
                errorMessage = ex.NativeErrorCode == 1223
                    ? "Přidání výjimky bylo zrušeno uživatelem (UAC)."
                    : ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
