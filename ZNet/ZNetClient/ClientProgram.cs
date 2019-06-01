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
            RemotePeer remotepeer;
            //List<RemotePeer> remotePeerlist = new List<RemotePeer>();
            //for (int i = 0; i < 10; i++)
            //{
            RUDPPeer tmp = host.CreateRUDPPeer();
            tmp.OnConnectionStatusChange += (ZNet.ConnectonStaus status, ZNet.RemotePeer RemotePeer) =>
            {
                Console.WriteLine("Main: Connection status change to: " + status);
                remotepeer = RemotePeer;
            };

            tmp.OnMessageReceive += (string data, ZNet.RemotePeer RemotePeer) =>
            {
                //Console.WriteLine("Main: Message received: " + data);
            };

            remotepeer = tmp.Connect("127.0.0.1", 42);

            //    remotePeerlist.Add(remotepeer);
            //}



            int send = -1;
            while (0 == 0)
            {
                host.ServiceAllPeers();
                if (send >= 0)
                {
                    string tmpstr = send.ToString();
                    remotepeer.Send(ref tmpstr);
                    send--;
                }
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    switch (key.Key)
                    {
                        case ConsoleKey.S:
                            Console.WriteLine("Read key and send message.");
                            string tmpk = key.ToString();
                            send = 1000;
                            /*var rpeer = remotePeerlist.GetEnumerator();
                            while (rpeer.MoveNext())
                            {
                                rpeer.Current.Send(ref tmp);
                            }*/
                            //remotepeer.Send(ref tmpk);
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