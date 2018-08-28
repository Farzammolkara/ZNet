using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZNet
{
	public enum ConnectonStaus
	{
		Disconnected = 0,
		Connecting = 1,
		Connected = 2,
	}
	public class Host
	{
		List<RUDPPeer> RUDPPeerList = new List<RUDPPeer>();
		public Host()
		{ }

		public RUDPPeer CreateRUDPPeer()
		{
			RUDPPeer peer = new RUDPPeer();
			RUDPPeerList.Add(peer);
			return peer;
		}

		public void Service()
		{
			for (int i = 0; i < RUDPPeerList.Count; i++)
			{
				RUDPPeerList[i].Service();
			}
		}
	}
}
