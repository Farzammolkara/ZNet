using System;
using System.Net;
using System.Collections.Generic;

namespace ZNet
{
	public class RUDPPeer
	{
		public event Action<ConnectonStaus> OnConnectionStatusChange;
		public event Action<string> OnMessageReceive;

		private System.Net.Sockets.Socket socket;
		private System.Net.EndPoint SenderEndPoint;
		const int buff_size = 10 * 1024 * 1024;
		private byte[] incomming = new byte[10 * 1024 * 1024];

		List<RemotePeer> RemotePeerList = new List<RemotePeer>();

		private object isLock = new object();

		public RUDPPeer()
		{
			socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
			socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReceiveBuffer, buff_size);
			socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.SendBuffer, buff_size);
			socket.SendBufferSize = buff_size;
			socket.ReceiveBufferSize = buff_size;

			socket.Blocking = false;

			IPAddress tempaddress = IPAddress.Parse("127.0.0.1");
			IPEndPoint tmpendpoint = new IPEndPoint(tempaddress, 0);
			SenderEndPoint = (EndPoint)tmpendpoint;

			System.Threading.Thread ReceiveThread = new System.Threading.Thread(new System.Threading.ThreadStart(ReceiveLoop));
			ReceiveThread.Start();
		}

		public void Bind(string ip, int port)
		{
			socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(ip), port));
		}

		public RemotePeer Connect(string IPV4IP, int port)
		{
			socket.Connect(IPV4IP, port);

			System.Net.IPAddress RemotePeerIPAddress = System.Net.IPAddress.Parse(IPV4IP);
			System.Net.IPEndPoint RemotePeerEndPoint = new System.Net.IPEndPoint(RemotePeerIPAddress, port);

			RemotePeer remotepeer = TouchPeer(RemotePeerEndPoint);
			// TODO protocol.
			Protocol message = new Protocol();
			message.Header.SendType = ProtocolHeader.MessageSendType.Internal;
			message.Data.Data = "Hello";

			Send(remotepeer, message);

			OnConnectionStatusChange(ConnectonStaus.Connecting);

			return remotepeer;
		}

		private RemotePeer TouchPeer(IPEndPoint remotePeerEndPoint)
		{
			RemotePeer rp = GetPeer(remotePeerEndPoint);
			if (rp != null)
				return rp;

			rp = CreatePeer(remotePeerEndPoint);
			return rp;
		}

		private RemotePeer CreatePeer(IPEndPoint remotePeerEndPoint)
		{
			RemotePeer rp = new RemotePeer();
			rp.ipEndPoint = remotePeerEndPoint;
			RemotePeerList.Add(rp);

			return rp;
		}

		private RemotePeer GetPeer(IPEndPoint remotePeerEndPoint)
		{
			var itr = RemotePeerList.GetEnumerator();
			while (itr.MoveNext())
			{
				if (itr.Current.ipEndPoint == remotePeerEndPoint)
					return itr.Current;
			}
			return null;
		}

		private void ReceiveLoop()
		{
			while (true)
			{
				lock (isLock)
				{
					ReceiveMessage();
				}
				System.Threading.Thread.Sleep(1);
			}
		}

		private void ReceiveMessage()
		{
			try
			{
				int result = socket.ReceiveFrom(incomming, System.Net.Sockets.SocketFlags.None, ref SenderEndPoint);
				if (result > 0)
				{
					IPEndPoint ipendpoint = (IPEndPoint)SenderEndPoint;
					RemotePeer peer = TouchPeer(ipendpoint);
					Protocol message = new Protocol();
					message.DeserializeFromBytes(incomming);

					peer.IncommingMessageList.Add(message);
				}
			}
			catch (Exception e)
			{}
		}

		private int Send(RemotePeer remotepeer, Protocol message)
		{
			int messagenumber = remotepeer.AddMessageToOutGoings(message);
			return messagenumber;
		}

		internal void Service()
		{
			lock (isLock)
			{
				DispatchReceivedMessage();
				SendOutGoingMessages();
			}
		}

		private void SendOutGoingMessages()
		{
			var itr = RemotePeerList.GetEnumerator();
			while (itr.MoveNext())
			{
				var msgitr = itr.Current.OutGoingMessageList.GetEnumerator();
				while (msgitr.MoveNext())
				{
					if(msgitr.Current.Sent == 0)
					RealSend(itr.Current, msgitr.Current);
				}
			}
		}

		private void RealSend(RemotePeer remotepeer, Protocol message)
		{
			byte[] tosendbuffer = message.SerializeToBytes();
			int e = socket.SendTo(tosendbuffer, remotepeer.ipEndPoint);
			message.Sent++;
		}

		private void DispatchReceivedMessage()
		{
			var peeritr = RemotePeerList.GetEnumerator();
			while (peeritr.MoveNext())
			{
				var incommingmsgitr = peeritr.Current.IncommingMessageList.GetEnumerator();
				while (incommingmsgitr.MoveNext())
				{
					Protocol message = incommingmsgitr.Current;
					if (message.Dispatched == 0)
						Dispatch(peeritr.Current, message);
				}
			}
		}

		private void Dispatch(RemotePeer senderpeer, Protocol message)
		{
			if (message.Header.SendType == ProtocolHeader.MessageSendType.Internal)
				DispatchInternal(senderpeer, message);

			if (message.Header.SendType == ProtocolHeader.MessageSendType.External)
				OnMessageReceive(message.Data.Data);

			message.Dispatched++;
		}

		private void DispatchInternal(RemotePeer senderpeer, Protocol message)
		{
			if (message.Data.Data == "Hello")
			{
				Protocol msg = new Protocol();
				msg.Header.SendType = ProtocolHeader.MessageSendType.Internal;
				msg.Data.Data = "HandShake";
				Send(senderpeer, msg);
				OnConnectionStatusChange(ConnectonStaus.Connected);
			}
			if (message.Data.Data == "HandShake")
			{
				OnConnectionStatusChange(ConnectonStaus.Connected);
			}
		}
	}
}