using System;
using System.Collections.Generic;
using System.Net;

namespace ZNet
{
	public class RemotePeer
	{
		public IPEndPoint ipEndPoint;
		private int OutGoingSequenceNumber = 0;

		public List<Protocol> OutGoingMessageList = new List<Protocol>();
		public List<Protocol> IncommingMessageList = new List<Protocol>();

		public int AddMessageToOutGoings(Protocol message)
		{
			int sequence = GetNewSequenceNumber();
			message.Header.SequenceNumber = sequence;
			OutGoingMessageList.Add(message);

			return sequence;
		}

		private int GetNewSequenceNumber()
		{
			return ++OutGoingSequenceNumber;
		}
	}
}