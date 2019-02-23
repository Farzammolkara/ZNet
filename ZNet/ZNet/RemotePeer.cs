using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ZNet
{
    public enum RemotePeerTypes
    {
        NotDeterminedYet = 0,
        Master = 1,
        Slave = 2,
    }
    public enum ConnectonStaus
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
    }
    //==================================================================================
    public class RemotePeer
    {
        public RemotePeerTypes RemotePeerType;
        public IPEndPoint ipEndPoint;
        public int LastDispatchedMessage = -1;
        public int LastSendTime = 0;
        private int OutGoingSequenceNumber = 0;
        private const int PingInterval = 2000;
        private const int ConnectioTimeOut = 100000;
        public ConnectonStaus connectionStatus = ConnectonStaus.Disconnected;
        private int LastAckedSentMessageSequence = -1;

        private int LastReceiveTime = System.Environment.TickCount & Int32.MaxValue;
        private SortedDictionary<int, Protocol> IncommingMessageList = new SortedDictionary<int, Protocol>();
        public SortedDictionary<int, Protocol> OutGoingMessageList = new SortedDictionary<int, Protocol>();
        public SortedDictionary<int, Protocol> OutgoingCopyForResend = new SortedDictionary<int, Protocol>();

        // first int is the carrier seq, the list is the ack list it has been caried.
        private Dictionary<int, List<int>> IncommingMessageAckPackList = new Dictionary<int, List<int>>();


        public event Action<ConnectonStaus, RemotePeer> OnConnectionStatusChange;
        public event Action<string, RemotePeer> OnMessageReceive;
        //======================================================================================
        public void Reset()
        {
            // clear the outgoing list
            // clear the receive buffer
        }

        public int Send(Protocol message)
        {
            int messagenumber = AddMessageToOutGoings(message);
            Console.WriteLine("RemotePeer: Message added to Outgoings: " + message.Header.SequenceNumber + "-" + message.Data.Data);
            return messagenumber;
        }

        public void ConnectionStatusChange(ConnectonStaus connectionstat)
        {
            Console.WriteLine("RemotePeer: ConnectionStatues Changed from: " + connectionStatus + " to " + connectionstat);
            connectionStatus = connectionstat;
            OnConnectionStatusChange(connectionstat, this);
        }

        public void MessageReceived(Protocol message)
        {
            LastReceiveTime = System.Environment.TickCount & Int32.MaxValue;
            Console.Write("RemotePeer: " + message.Header.SequenceNumber + " msg received at " + LastReceiveTime);
            // Check the message not to be duplicated!
            bool redundant = false;
            var incommingmsgitr = IncommingMessageList.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                int seq = incommingmsgitr.Current.Key;
                if (seq == message.Header.SequenceNumber)
                    redundant = true;
            }
            if (!redundant)
            {
                IncommingMessageList[message.Header.SequenceNumber] = message;
                Console.WriteLine(" " + message.Data.Data.ToString());
            }
            else
                Console.WriteLine(" Redundant");
        }

        public void MarkIncommingsforAckDelivery(List<int> ackList)
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
                            if (incommingmsgseqtodelete == incommingmsgitr.Current.Key)
                            {
                                incommingmsgitr.Current.Value.IncommingAckInformDelivered = 1;
                            }
                        }
                    }
                    IncommingMessageAckPackList.Remove(carrierseq);
                }
            }
        }

        public void OutGoingAcksReceived(List<int> ackList)
        {
            var msgitr = OutGoingMessageList.GetEnumerator();
            while (msgitr.MoveNext())
            {
                int outgoingmsgseq = msgitr.Current.Key;
                var ackitr = ackList.GetEnumerator();
                while (ackitr.MoveNext())
                {
                    int ackseqnumber = ackitr.Current;
                    if (outgoingmsgseq == ackseqnumber)
                    {
                        msgitr.Current.Value.OutGoingReceiveAcked = 1;
                        if (LastAckedSentMessageSequence < msgitr.Current.Key)
                            LastAckedSentMessageSequence = msgitr.Current.Key;
                    }
                }
            }
        }

        public void ResendAllOutgoingMessages(SortedDictionary<int, Protocol> outgoing)
        {
            var itr = outgoing.GetEnumerator();
            while (itr.MoveNext())
            {
                if (itr.Current.Value.Header.SendType == ProtocolHeader.MessageSendType.External)
                    Send(itr.Current.Value);
            }
        }

        public void DispatchMessageList()
        {
            int lastpatchedmessageseqnumber = 0;
            int index = 0;
            var messageitr = IncommingMessageList.GetEnumerator();
            while (messageitr.MoveNext())
            {
                Protocol message = messageitr.Current.Value;
                int seq = messageitr.Current.Key;
				if ((index == 0) || (seq == lastpatchedmessageseqnumber + 1))
				{
					if (message.Dispatched < 1)
						Dispatch(message);
					lastpatchedmessageseqnumber = seq;
					message.Dispatched++;
					LastDispatchedMessage = message.Header.SequenceNumber;
					index++;
				}
				else if (message.Header.SendType == ProtocolHeader.MessageSendType.Ping)
				{
					if (message.Dispatched < 1)
						Dispatch(message);
					message.Dispatched++;
					index++;
				}
            }
        }

        internal void CheckConnectivity()
        {
            ConnectonStaus newconnectionstatus = GetConnectionStatus();
            if (connectionStatus != ConnectonStaus.Disconnected)
            {
                if (newconnectionstatus == ConnectonStaus.Disconnected)
                {
                    ConnectionStatusChange(ConnectonStaus.Disconnected);
                }
                else
                {
                    if ((RemotePeerType == RemotePeerTypes.Master) && (newconnectionstatus == ConnectonStaus.Connected))
                    {
                        Ping();
                    }
                }
            }
        }

        private void Ping()
        {
            int Now = System.Environment.TickCount & Int32.MaxValue;
            if (Now - LastSendTime > PingInterval)
            {
                Protocol message = new Protocol();
                message.Header.SendType = ProtocolHeader.MessageSendType.Ping;
                message.Data.Data = "Ping";

                Send(message);
            }
        }

        private ConnectonStaus GetConnectionStatus()
        {
            int Now = System.Environment.TickCount & Int32.MaxValue;
            if ((Now - LastReceiveTime > ConnectioTimeOut))
            {
                Console.WriteLine("RemotePeer: Remotepeer " + ipEndPoint.Address.ToString() + ":" + ipEndPoint.Port + " timeout on last receive timeout: " + LastReceiveTime);
                return ConnectonStaus.Disconnected;
            }

            return connectionStatus;
        }

        private void Dispatch(Protocol message)
        {
            if (message.Header.SendType == ProtocolHeader.MessageSendType.Internal)
                DispatchInternal(message);

            if ((message.Header.SendType == ProtocolHeader.MessageSendType.Ping) && (connectionStatus == ConnectonStaus.Connected))
                DispatchPing(message);

            if ((message.Header.SendType == ProtocolHeader.MessageSendType.External) && (connectionStatus == ConnectonStaus.Connected))
            {
                Console.WriteLine("RemotePeer: Dispatch External Message:" + message.Header.SequenceNumber);
                OnMessageReceive(message.Data.Data, this);
            }
        }

        private void DispatchInternal(Protocol message)
        {
            if (message.Data.Data == "Hello")
            {
                Console.WriteLine("RemotePeer: DispatchInternal: " + message.Header.SequenceNumber + ":" + message.Data.Data);

                Protocol msg = new Protocol();
                msg.Header.SendType = ProtocolHeader.MessageSendType.Internal;
                msg.Data.Data = "HandShake";
                Send(msg);
                ConnectionStatusChange(ConnectonStaus.Connected);
                ResendAllOutgoingMessages(OutgoingCopyForResend);
            }
            if (message.Data.Data == "HandShake")
            {
                Console.WriteLine("RemotePeer: DispatchInternal: " + message.Header.SequenceNumber + ":" + message.Data.Data);
                ConnectionStatusChange(ConnectonStaus.Connected);
            }
        }

        private void DispatchPing(Protocol message)
        {
            if (message.Data.Data == "Ping")
            {
                Console.WriteLine("RemotePeer: DispatchPing: " + message.Header.SequenceNumber + ":" + message.Data.Data);
                Protocol msg = new Protocol();
                msg.Header.SendType = ProtocolHeader.MessageSendType.Ping;
                msg.Data.Data = "Pong";
                Send(msg);
            }
            if (message.Data.Data == "Pong")
            {
                Console.WriteLine("RemotePeer: DispatchPing: " + message.Header.SequenceNumber + ":" + message.Data.Data);
            }
        }

        public int AddMessageToOutGoings(Protocol message)
        {
            int sequence = GetNewSequenceNumber();
            message.Header.SequenceNumber = sequence;
            OutGoingMessageList[sequence] = message;

            //LastSendTime = System.Environment.TickCount & Int32.MaxValue;

            return sequence;
        }

        private int GetNewSequenceNumber()
        {
            return ++OutGoingSequenceNumber;
        }

        public List<int> GetIncommingMessageSequences(int carriermessage)
        {
            List<int> AckPack = new List<int>();

            var incommingmsgitr = IncommingMessageList.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                int seq = incommingmsgitr.Current.Key;
                AckPack.Add(seq);
            }
            if(!IncommingMessageAckPackList.ContainsKey(carriermessage))
                IncommingMessageAckPackList.Add(carriermessage, AckPack);

            return AckPack;
        }

        public void RemoveIncommingMessage()
        {
            int lastSequenceNumber = 0;
            int index = 0;
            SortedDictionary<int, Protocol> IncommingMessageListCopy = IncommingMessageList;
            var incommingmsgitr = IncommingMessageListCopy.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                //if it's the first message then dispach, if not gotta be sequenced!
                Protocol message = incommingmsgitr.Current.Value;
                if ((index == 0) || (message.Header.SequenceNumber == lastSequenceNumber + 1))
                {
                    lastSequenceNumber = message.Header.SequenceNumber;
                    if (message.Dispatched == 1 && message.IncommingAckInformDelivered == 1)
                    {
                        Console.Write("RemotePeer: Remove Incomming Message: " + lastSequenceNumber);
                        IncommingMessageList.Remove(lastSequenceNumber);
                        Console.WriteLine(" IncommingMessageList size: " + IncommingMessageList.Count);
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        public void RemoveOutgoingMessage()
        {
            List<Protocol> OutGoingMessageListCopy = OutGoingMessageList.Values.ToList<Protocol>();
            var outgoingmsgitr = OutGoingMessageListCopy.GetEnumerator();
            while (outgoingmsgitr.MoveNext())
            {
                Protocol message = outgoingmsgitr.Current;
                if (message.OutGoingReceiveAcked == 1)
                {
                    Console.Write("RemotePeer: Remove Outgoing Message: " + message.Header.SequenceNumber);
                    OutGoingMessageList.Remove(message.Header.SequenceNumber);
                    Console.WriteLine(" OutGoingMessageList size: " + OutGoingMessageList.Count);
                }
                else
                {
                    break;
                }
            }
        }
        public void ResendOutgoingMessage()
        {
            SortedDictionary<int, Protocol> OutGoingMessageListCopy = OutGoingMessageList;
            var outgoingmsgitr = OutGoingMessageListCopy.GetEnumerator();
            while (outgoingmsgitr.MoveNext())
            {
                Protocol message = outgoingmsgitr.Current.Value;
                //TODO This condition must be checked on tests and if required get compeleted by time(RT) or the many times that this has been happpened++
                if ((message.Sent == 1) && (message.OutGoingReceiveAcked == 0) && (message.Header.SequenceNumber < LastAckedSentMessageSequence))
                {
                    Console.WriteLine("RemotePeer: message sent turned to 0: " + message.Header.SequenceNumber);
                    message.Sent = 0;
                }
            }
        }
    }
}
