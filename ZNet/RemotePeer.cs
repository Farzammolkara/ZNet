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
        public ConnectonStaus connectionStatus = ConnectonStaus.Disconnected;

        // the first int is the sequence number of the message which is carrying the acklist of incomming messages which is the second param.
        public Dictionary<int, List<int>> IncommingMessageAckPackList = new Dictionary<int, List<int>>();

		private int Now, LastSendTime;
        private int LastReceiveTime = System.Environment.TickCount & Int32.MaxValue;
        private const int PingInterval = 2000;
        private const int ConnectioTimeOut = 10000;

        public int AddMessageToOutGoings(Protocol message)
		{
			int sequence = GetNewSequenceNumber();
			message.Header.SequenceNumber = sequence;
			OutGoingMessageList.Add(message);

            // TODO Wrong place. How do you know that it is gonna be sent presently.
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

        public void OutGoingAcksReceived(List<int> ackList)
        {
            ackList.Sort();
            int biggestseq = ackList[ackList.Count-1];

            var msgitr = OutGoingMessageList.GetEnumerator();
            while (msgitr.MoveNext())
            {
                Protocol message = msgitr.Current;
                var ackitr = ackList.GetEnumerator();
                bool removed = false;
                while (ackitr.MoveNext())
                {
                    int seqnumber = ackitr.Current;
                    if (message.Header.SequenceNumber == seqnumber)
                    {
                        OutGoingMessageList.Remove(message);
                        removed = true;
                    }
                }
                if (!removed && (message.Header.SequenceNumber < biggestseq))
                {
                    //TODO
                    //resendCandidate when reaching 10 check the time and then resend!
                    message.Sent = 0;
                }
                if (message.Header.SequenceNumber > biggestseq)
                    return;
            }
        }

        internal void ResetReceives()
        {
            IncommingMessageAckPackList.Clear();
            IncommingMessageList.Clear();
        }

        public List<int> GetIncommingMessageSequences(int carriermessage)
        {
            List<int> AckPack = new List<int>();

            var incommingmsgitr = IncommingMessageList.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                int seq = incommingmsgitr.Current.Header.SequenceNumber;
                AckPack.Add(seq);
            }
            IncommingMessageAckPackList.Add(carriermessage, AckPack);

            return AckPack;
        }

        public void MarkToRemoveIncommings(List<int> ackList)
        {
            var ackitr = ackList.GetEnumerator();
            while (ackitr.MoveNext())
            {
                int carrierseq = ackitr.Current;
                if (IncommingMessageAckPackList.ContainsKey(carrierseq))
                {
                    List<int> AckPack = IncommingMessageAckPackList[carrierseq];

                    var incommingmsgtodeleteitr = AckPack.GetEnumerator();
                    while (incommingmsgtodeleteitr.MoveNext())
                    {
                        int incommingmsgseqtodelete = incommingmsgtodeleteitr.Current;

                        var incommingmsgitr = IncommingMessageList.GetEnumerator();
                        while (incommingmsgitr.MoveNext())
                        {
                            if (incommingmsgseqtodelete == incommingmsgitr.Current.Header.SequenceNumber)
                            {
                                incommingmsgitr.Current.ReadyToDelete = 1;
                            }
                        }
                    }
                    IncommingMessageAckPackList.Remove(carrierseq);
                }
            }
        }

        public void MessageReceived(Protocol message)
        {
            LastReceiveTime = System.Environment.TickCount & Int32.MaxValue;
            // Check the message not to be duplicated!
            bool redundant = false;
            var incommingmsgitr = IncommingMessageList.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                int seq = incommingmsgitr.Current.Header.SequenceNumber;
                if (seq == message.Header.SequenceNumber)
                    redundant = true;
            }
            if(!redundant)
                IncommingMessageList.Add(message);
        }

        public ConnectonStaus GetConnectionStatus()
        {
            Now = System.Environment.TickCount & Int32.MaxValue;
            // Retry when it's not connected
            if ((Now - LastReceiveTime > ConnectioTimeOut))
            {
                connectionStatus = ConnectonStaus.Disconnected;
                LastReceiveTime = Now;
            }

            return connectionStatus;
        }
    }
}