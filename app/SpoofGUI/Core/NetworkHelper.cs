using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SpoofGUI.Core;

public static class NetworkHelper
{
    private static readonly string[] VirtualKeywords = new[]
    {
        "virtual", "loopback", "wintun", "vpn", "hyper-v", "vmware", "virtualbox",
        "tap", "tunnel", "tailscale", "zerotier", "fortinet", "cisco", "globalprotect",
        "openvpn", "wireguard", "npcap", "software", "pseudo"
    };

    public static string GetLocalPhysicalIPAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet && 
                    ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                
                if (IsVirtual(ni.Name, ni.Description))
                    continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.Address.ToString();
                    }
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    public static bool IsVirtualInterface(string ipStr)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.ToString() == ipStr)
                    {
                        if (IsVirtual(ni.Name, ni.Description))
                            return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private static bool IsVirtual(string name, string desc)
    {
        name = name.ToLowerInvariant();
        desc = desc.ToLowerInvariant();
        foreach (var keyword in VirtualKeywords)
        {
            if (name.Contains(keyword) || desc.Contains(keyword))
                return true;
        }
        return false;
    }
}
