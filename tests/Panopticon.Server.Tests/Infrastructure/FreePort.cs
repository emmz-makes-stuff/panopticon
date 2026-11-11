using System.Net;
using System.Net.Sockets;

namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>A loopback TCP port that's free now, for a test host to listen on. The CLI integration tests share it.</summary>
public static class FreePort
{
    public static int Next()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
