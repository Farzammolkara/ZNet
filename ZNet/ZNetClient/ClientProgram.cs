using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZNet;

namespace ZNetClient
{
    class ClientProgram
    {
        static void Main(string[] args)
        {
            Host host = new ZNet.Host();
            RUDPPeer peer = host.CreateRUDPPeer();

            peer.OnConnectionStatusChange += (ZNet.ConnectonStaus status, ZNet.RemotePeer RemotePeer) =>
            {
                Console.WriteLine("Main: Connection status change to: " + status);
            };

            peer.OnMessageReceive += (string data, ZNet.RemotePeer RemotePeer) =>
            {
                Console.WriteLine("Main: Message received: " + data);
            };

            RemotePeer remotepeer = peer.Connect("192.168.1.162", 42);

            while (0 == 0)
            {
                host.ServiceAllPeers();
            }
            host.Destroy();
        }
    }
}
