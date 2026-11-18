using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Nega.Common.Networking;

/// <summary>
/// Tiện ích tính toán địa chỉ Directed Subnet Broadcast (IP | ~SubnetMask) trên tất cả các card mạng vật lý/Wi-Fi.
/// Khắc phục lỗi gói tin 255.255.255.255 bị đẩy nhầm vào card mạng ảo (Docker, VMware, VirtualBox, VPN) hoặc lỗi No route to host trên macOS.
/// </summary>
public static class SubnetBroadcastHelper
{
    public static IReadOnlyList<IPAddress> GetBroadcastAddresses()
    {
        var results = new List<IPAddress>
        {
            IPAddress.Loopback
        };

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties props = nic.GetIPProperties();
                foreach (UnicastIPAddressInformation unicast in props.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                        unicast.IPv4Mask is null)
                    {
                        continue;
                    }

                    byte[] ipBytes = unicast.Address.GetAddressBytes();
                    byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();
                    if (ipBytes.Length == 4 && maskBytes.Length == 4)
                    {
                        byte[] broadcastBytes = new byte[4];
                        for (int i = 0; i < 4; i++)
                        {
                            broadcastBytes[i] = (byte)(ipBytes[i] | (~maskBytes[i]));
                        }
                        var bcastIp = new IPAddress(broadcastBytes);
                        if (!results.Contains(bcastIp))
                        {
                            results.Add(bcastIp);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore interface enumeration failure
        }

        if (!results.Contains(IPAddress.Broadcast))
        {
            results.Add(IPAddress.Broadcast);
        }

        return results;
    }

    public static string GetPrimaryLocalIpAddress()
    {
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(unicast.Address))
                    {
                        return unicast.Address.ToString();
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return "127.0.0.1";
    }
}
