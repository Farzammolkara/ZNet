using System;
using System.Collections.Generic;
using System.Net;

namespace ZNet
{
	public class RemotePeer
	{
		public enum RemotePeerTypes
		{
			NotDeterminedYet = 0,
			Master = 1,
			Slave = 2,
		}

		public IPEndPoint ipEndPoint;
		private int OutGoingSequenceNumber = 0;

		public List<Protocol> OutGoingMessageList = new List<Protocol>();
		public List<Protocol> IncommingMessageList = new List<Protocol>();
		public RemotePeerTypes RemotePeerType = RemotePeerTypes.NotDeterminedYet;

		private int Now, LastSendTime;
		private const int PingInterval = 10000;

		public int AddMessageToOutGoings(Protocol message)
		{
			int sequence = GetNewSequenceNumber();
			message.Header.SequenceNumber = sequence;
			OutGoingMessageList.Add(message);

			LastSendTime = System.Environment.TickCount & Int32.MaxValue;

			return sequence;
		}

		private int GetNewSequenceNumber()
		{
			return ++OutGoingSequenceNumber;
		}

		public void Ping()
		{
			Now = System.Environment.TickCount & Int32.MaxValue;
			if (Now - LastSendTime > PingInterval)
			{
				Protocol message = new Protocol();
				message.Header.SendType = ProtocolHeader.MessageSendType.Ping;
				message.Data.Data = "Ping";

				AddMessageToOutGoings(message);
			}
		}
	}
}