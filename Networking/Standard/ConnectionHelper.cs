using System.Net;
using System.Net.Sockets;

namespace QuazarAPI.Networking.Standard
{
    public static class ConnectionHelper
    {
        public static TcpClient Connect(IPAddress address, int port)
        {
            return new TcpClient(address.ToString(), port);
        }
    }
}
