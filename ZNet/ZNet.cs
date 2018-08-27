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
		public Host()
		{ }

		public void Initiate()
		{
			throw new NotImplementedException();
		}

		public RUDPPeer CreateRUDPPeer()
		{
			throw new NotImplementedException();
		}

		public void Service()
		{
			throw new NotImplementedException();
		}
	}
}
