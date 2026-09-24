using System.Net;
using System.Net.NetworkInformation;

namespace Deck.Shell.Network;

/// <summary>
/// Total bytes through the adapters that reach the internet. Only adapters with a default gateway
/// count: virtual switches (Hyper-V, WSL) carry the same traffic again and would double every
/// number.
/// </summary>
internal static class NetworkCounters
{
    public static (bool AnyUp, long Received, long Sent) Read()
    {
        NetworkInterface[] adapters;
        try
        {
            adapters = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return (false, 0, 0);
        }

        bool up = false;
        long received = 0;
        long sent = 0;

        foreach (var adapter in adapters)
        {
            try
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                bool hasGateway = adapter.GetIPProperties().GatewayAddresses
                    .Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                if (!hasGateway) continue;

                var stats = adapter.GetIPStatistics();
                up = true;
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
                // An adapter vanishing mid-read (a USB dongle, a VPN dropping) — skip it.
            }
        }

        return (up, received, sent);
    }
}
