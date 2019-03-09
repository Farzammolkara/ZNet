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
            List<RemotePeer> remotePeerlist = new List<RemotePeer>();
            for (int i = 0; i < 10; i++)
            {
                RUDPPeer tmp = host.CreateRUDPPeer();
                tmp.OnConnectionStatusChange += (ZNet.ConnectonStaus status, ZNet.RemotePeer RemotePeer) =>
                {
                    Console.WriteLine("Main: Connection status change to: " + status);
                };

                tmp.OnMessageReceive += (string data, ZNet.RemotePeer RemotePeer) =>
                {
                    Console.WriteLine("Main: Message received: " + data);
                };

                RemotePeer remotepeer = tmp.Connect("127.0.0.1", 42);

                remotePeerlist.Add(remotepeer);
            }




            while (0 == 0)
            {
                host.ServiceAllPeers();
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    switch (key.Key)
                    {
                        case ConsoleKey.S:
                            Console.WriteLine("Read key and send message.");
                            string tmp = key.ToString();
                            var rpeer = remotePeerlist.GetEnumerator();
                            while (rpeer.MoveNext())
                            {
                                rpeer.Current.Send(ref tmp);
                            }
                            break;
                        default:
                            break;
                    }
                }
            }
            host.Destroy();
        }
    }
}
/* Host host = new ZNet.Host();
 RUDPPeer peer = host.CreateRUDPPeer();

 peer.OnConnectionStatusChange += (ZNet.ConnectonStaus status, ZNet.RemotePeer RemotePeer) =>
         {
             Console.WriteLine("Main: Connection status change to: " + status);
         };

peer.OnMessageReceive += (string data, ZNet.RemotePeer RemotePeer) =>
         {
             Console.WriteLine("Main: Message received: " + data);
         };

         RemotePeer remotepeer = peer.Connect("127.0.0.1", 42);

         while (0 == 0)
         {
             host.ServiceAllPeers();
             if (Console.KeyAvailable)
             {
                 ConsoleKeyInfo key = Console.ReadKey(true);
                 switch (key.Key)
                 {
                    case ConsoleKey.S:
                         Console.WriteLine("Read key and send message.");
                         string tmp = key.ToString();
remotepeer.Send(ref tmp);
                         break;
                     default:
                         break;
                 }
             }
         }
         host.Destroy();
     }
 }
*/
