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
        private const int ConnectioTimeOut = 10000;
        private int MessageWithAckRTT = 100;
        public ConnectonStaus connectionStatus = ConnectonStaus.Disconnected;
        private int LastAckedSentMessageSequence = -1;
        private int TotalSendTime = 0;
        private int TotalSendCount = 0;
        private int TotalPingPongTime = 0;
        private int TotalPingPongCount = 0;
        private int TotalResendCount = 0;
        private int TotalReceiveCount = 0;
        private int TotalRedundantReceiveCount = 0;
        
        private int LastReceiveTime = System.Environment.TickCount & Int32.MaxValue;
        private SortedDictionary<int, Protocol> IncommingMessageList = new SortedDictionary<int, Protocol>();
        public SortedDictionary<int, Protocol> OutGoingMessageList = new SortedDictionary<int, Protocol>();
        public SortedDictionary<int, Protocol> OutgoingCopyForResend = new SortedDictionary<int, Protocol>();
        private Dictionary<int, int> PingTime = new Dictionary<int, int>();

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
        public int Send(ref string stringmessage)
        {
            Protocol msg = new Protocol();
            msg.Header.SendType = ProtocolHeader.MessageSendType.External;
            msg.Data.Data = stringmessage;
            return Send(msg);
        }

        public void ConnectionStatusChange(ConnectonStaus connectionstat)
        {
            Console.WriteLine("RemotePeer: ConnectionStatues Changed from: " + connectionStatus + " to " + connectionstat);
            connectionStatus = connectionstat;

            // TODO : null check for events is required
            OnConnectionStatusChange(connectionstat, this);
        }

        public void MessageReceived(Protocol message)
        {
            LastReceiveTime = System.Environment.TickCount & Int32.MaxValue;
            TotalReceiveCount++;
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
            }
            else
            {
                TotalRedundantReceiveCount++;
                Console.WriteLine(" Redundant");
            }

            Console.WriteLine("RemotePeer: " + message.Header.SequenceNumber + " msg received at " + LastReceiveTime +" data: "+ message.Data.Data.ToString() + " total receive: " + TotalReceiveCount+" total redundant: "+ TotalRedundantReceiveCount);
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
                    if ((outgoingmsgseq == ackseqnumber) && (msgitr.Current.Value.OutGoingReceiveAcked == 0))
                    {
                        int Now = System.Environment.TickCount & Int32.MaxValue;
                        msgitr.Current.Value.OutGoingReceiveAcked = 1;
                        msgitr.Current.Value.OutGoingAckReceiveTime = Now;
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
                    // TODO : In cases like this, you can use == or != instead of </>
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
                int seq = Send(message);
                PingTime[seq] = Now;
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
            // TODO : You should use "else if"

            if (message.Header.SendType == ProtocolHeader.MessageSendType.Internal)
                DispatchInternal(message);

            else if ((message.Header.SendType == ProtocolHeader.MessageSendType.Ping) && (connectionStatus == ConnectonStaus.Connected))
                DispatchPing(message);

            else if ((message.Header.SendType == ProtocolHeader.MessageSendType.External) && (connectionStatus == ConnectonStaus.Connected))
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
            if (message.Data.Data.Contains("Ping"))
            {
                Console.WriteLine("RemotePeer: DispatchPing: " + message.Header.SequenceNumber + ":" + message.Data.Data);
                Protocol msg = new Protocol();
                msg.Header.SendType = ProtocolHeader.MessageSendType.Ping;
                msg.Data.Data = "Pong_" + message.Header.SequenceNumber;
                Send(msg);
            }
            if (message.Data.Data.Contains("Pong"))
            {
                int pingseq = Convert.ToInt32(message.Data.Data.Substring(5));
                int sendtime = 0;
                int LRTT = -1;
                if (PingTime.ContainsKey(pingseq))
                {
                    sendtime = PingTime[pingseq];
                    int Now = System.Environment.TickCount & Int32.MaxValue;
                    LRTT = Now - sendtime;
                    PingTime.Remove(pingseq);
                    TotalPingPongTime += LRTT;
                    TotalPingPongCount++;
                }
                Console.WriteLine("RemotePeer: DispatchPing: " + message.Header.SequenceNumber + ":" + message.Data.Data+":With RTT: "+LRTT+" with average RTT: "+(TotalPingPongTime/TotalPingPongCount));
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
            if (!IncommingMessageAckPackList.ContainsKey(carriermessage))
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
                    int sendacktime = message.OutGoingAckReceiveTime - message.SentTime;
                    TotalSendTime += sendacktime;
                    TotalSendCount++;
                    Console.Write("RemotePeer: Remove Outgoing Message: " + message.Header.SequenceNumber);
                    OutGoingMessageList.Remove(message.Header.SequenceNumber);
                    Console.WriteLine(" OutGoingMessageList size: " + OutGoingMessageList.Count);
                    int NewRTT = TotalSendTime / TotalSendCount;
                    Console.WriteLine("RUDPPeer: Total send count is: "+ TotalSendCount + " redundant send: "+ TotalResendCount);
                    Console.WriteLine("RUDPPeer: Time to send and get the ack back is: "+ sendacktime+ " average sendandack: " + NewRTT);
                    if (NewRTT > MessageWithAckRTT)
                        MessageWithAckRTT = NewRTT;
                }
                else
                {
                    break;
                }
            }
        }
        public void ResendOutgoingMessage()
        {
            int Now = System.Environment.TickCount & Int32.MaxValue;
            SortedDictionary<int, Protocol> OutGoingMessageListCopy = OutGoingMessageList;
            var outgoingmsgitr = OutGoingMessageListCopy.GetEnumerator();
            while (outgoingmsgitr.MoveNext())
            {
                Protocol message = outgoingmsgitr.Current.Value;
                //TODO This condition must be checked on tests and if required get compeleted by time(RT) or the many times that this has been happpened++
                if ((message.Sent == 1) && (message.OutGoingReceiveAcked == 0) && (message.Header.SequenceNumber < LastAckedSentMessageSequence)
                    && (Now - message.SentTime > MessageWithAckRTT*2))
                {
                    Console.WriteLine("RemotePeer: message sent turned to 0: " + message.Header.SequenceNumber);
                    message.Sent = 0;
                    TotalResendCount++;
                }
            }
        }
    }
}
