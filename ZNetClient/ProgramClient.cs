using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZNetClient
{
	class ProgramClient
	{
		static void Main(string[] args)
		{
			ZNet.Host host = new ZNet.Host();

			ZNet.RUDPPeer rudppeer = host.CreateRUDPPeer();

			rudppeer.OnConnectionStatusChange += (ZNet.ConnectonStaus status, ZNet.RemotePeer RemotePeer) =>
			{
				Console.WriteLine("Connection status change to: " + status);
			};

			rudppeer.OnMessageReceive += (string data) =>
			{
				Console.WriteLine("Message received: " + data);
			};

			ZNet.RemotePeer remotepeer = rudppeer.Connect("127.0.0.1", 5555);

			while (0 == 0)
			{
				host.Service();

				if (Console.KeyAvailable)
				{
					ConsoleKeyInfo key = Console.ReadKey(true);
					switch (key.Key)
					{
						case ConsoleKey.S:
							Console.WriteLine("I want to send a message now");
							break;
						default:
							break;
					}
				}
			}
		}
	}
}
