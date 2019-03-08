using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZNet;

namespace ZNetServer
{
    class ServerProgram
    {
        static void Main(string[] args)
        {
            Host host = new Host();
            RUDPPeer peer = host.CreateRUDPPeer();

            peer.OnConnectionStatusChange += (ZNet.ConnectonStaus status, ZNet.RemotePeer RemotePeer) =>
            {
                Console.WriteLine("Main: Connection status change to: " + status);
            };

            peer.OnMessageReceive += (string data, ZNet.RemotePeer RemotePeer) =>
            {
                Console.WriteLine("Main: Message received: " + data);
            };

            string IPV4 = "127.0.0.1";
            peer.Bind(ref IPV4, 42);

            while (0 == 0)
            {
                host.ServiceAllPeers();
            }
            host.Destroy();
        }
    }
}
