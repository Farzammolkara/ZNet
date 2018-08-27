using System;

namespace ZNet
{
	public class RUDPPeer
	{
		public event Action<ConnectonStaus> OnConnectionStatusChange;
		public event Action<string> OnMessageReceive;

		public RemotePeer Connect(string ip, int port)
		{
			throw new NotImplementedException();
		}
	}
}