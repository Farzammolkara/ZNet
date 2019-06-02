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
        public int LastExternalDispatchedMessage = -1;
        public int LastInternalDispatchedMessage = -1;
        public int LastInternalSendTime = 0;
        private int ExternalOutGoingSequenceNumber = 0;
        private int InternalOutGoingSequenceNumber = 0;
        private const int PingInterval = 2000;
        private const int ConnectioTimeOut = 10000;
        private int MessageWithAckRTT = 100;
        public ConnectonStaus connectionStatus = ConnectonStaus.Disconnected;
        private int LastAckedSentExternalMessageSequence = -1;
        private int LastAckedSentInternalMessageSequence = -1;
        private int TotalSendTime = 0;
        private int TotalSendCount = 0;
        private int TotalPingPongTime = 0;
        private int TotalPingPongCount = 0;
        private int TotalResendCount = 0;
        private int TotalReceiveCount = 0;
        public int TotalRedundantReceiveCount = 0;
        public static int NotDeterminedYeteceiveCount = 0;

        private int LastReceiveTime = System.Environment.TickCount & Int32.MaxValue;
        private SortedDictionary<int, Protocol> IncommingExternalMessageList = new SortedDictionary<int, Protocol>();
        private SortedDictionary<int, Protocol> IncommingInternalMessageList = new SortedDictionary<int, Protocol>();
        public SortedDictionary<int, Protocol> ExternalOutGoingMessageList = new SortedDictionary<int, Protocol>();
        public SortedDictionary<int, Protocol> InternalOutGoingMessageList = new SortedDictionary<int, Protocol>();
        public SortedDictionary<int, Protocol> OutgoingCopyForResend = new SortedDictionary<int, Protocol>();
        private Dictionary<int, int> PingTime = new Dictionary<int, int>();

        // first int is the carrier seq, the list is the ack list it has been caried.
        private Dictionary<int, List<int>> IncommingExternalMessageAckPackList = new Dictionary<int, List<int>>();
        private Dictionary<int, List<int>> IncommingInternalMessageAckPackList = new Dictionary<int, List<int>>();


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
            SortedDictionary<int, Protocol> tmp_incommingmsg = new SortedDictionary<int, Protocol>();

            if (message.Header.SendType == ProtocolHeader.MessageSendType.External)
                tmp_incommingmsg = IncommingExternalMessageList;
            else if (message.Header.SendType == ProtocolHeader.MessageSendType.Internal)
                tmp_incommingmsg = IncommingInternalMessageList;

            LastReceiveTime = System.Environment.TickCount & Int32.MaxValue;
            TotalReceiveCount++;
            // Check the message not to be duplicated!
            bool redundant = false;
            var incommingmsgitr = tmp_incommingmsg.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                int seq = incommingmsgitr.Current.Key;
                if (seq == message.Header.SequenceNumber)
                    redundant = true;
            }
            if (!redundant)
            {
                tmp_incommingmsg[message.Header.SequenceNumber] = message;
            }
            else
            {
                TotalRedundantReceiveCount++;
                Console.WriteLine(" Redundant");
            }

            Console.WriteLine("RemotePeer: msg seq: " + message.Header.SequenceNumber + " msg received at " + LastReceiveTime + " data: " + message.Data.Data.ToString() + " total receive: " + TotalReceiveCount + " total redundant: " + TotalRedundantReceiveCount + " NotDetermminedPeer msg cnt: " + NotDeterminedYeteceiveCount);
        }

        public void MarkExternalIncommingsforAckDelivery(List<int> ackList)
        {
            var ackitr = ackList.GetEnumerator();
            while (ackitr.MoveNext())
            {
                int carrierseq = ackitr.Current;
                if (IncommingExternalMessageAckPackList.ContainsKey(carrierseq))
                {
                    List<int> AckPack = IncommingExternalMessageAckPackList[carrierseq];

                    var incommingmsgtodeleteitr = AckPack.GetEnumerator();
                    while (incommingmsgtodeleteitr.MoveNext())
                    {
                        int incommingmsgseqtodelete = incommingmsgtodeleteitr.Current;

                        var incommingmsgitr = IncommingExternalMessageList.GetEnumerator();
                        while (incommingmsgitr.MoveNext())
                        {
                            if (incommingmsgseqtodelete == incommingmsgitr.Current.Key)
                            {
                                incommingmsgitr.Current.Value.IncommingAckInformDelivered = 1;
                            }
                        }
                    }
                    IncommingExternalMessageAckPackList.Remove(carrierseq);
                }
            }
        }
        public void MarkInternalIncommingsforAckDelivery(List<int> ackList)
        {
            var ackitr = ackList.GetEnumerator();
            while (ackitr.MoveNext())
            {
                int carrierseq = ackitr.Current;
                if (IncommingInternalMessageAckPackList.ContainsKey(carrierseq))
                {
                    List<int> AckPack = IncommingInternalMessageAckPackList[carrierseq];

                    var incommingmsgtodeleteitr = AckPack.GetEnumerator();
                    while (incommingmsgtodeleteitr.MoveNext())
                    {
                        int incommingmsgseqtodelete = incommingmsgtodeleteitr.Current;

                        var incommingmsgitr = IncommingInternalMessageList.GetEnumerator();
                        while (incommingmsgitr.MoveNext())
                        {
                            if (incommingmsgseqtodelete == incommingmsgitr.Current.Key)
                            {
                                incommingmsgitr.Current.Value.IncommingAckInformDelivered = 1;
                            }
                        }
                    }
                    IncommingInternalMessageAckPackList.Remove(carrierseq);
                }
            }
        }

        public void ExternalOutGoingAcksReceived(List<int> ackList)
        {
            var msgitr = ExternalOutGoingMessageList.GetEnumerator();
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
                        if (LastAckedSentExternalMessageSequence < msgitr.Current.Key)
                            LastAckedSentExternalMessageSequence = msgitr.Current.Key;
                    }
                }
            }
        }
        public void InternalOutGoingAcksReceived(List<int> ackList)
        {
            var msgitr = InternalOutGoingMessageList.GetEnumerator();
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
                        if (LastAckedSentInternalMessageSequence < msgitr.Current.Key)
                            LastAckedSentInternalMessageSequence = msgitr.Current.Key;
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
                {
                    itr.Current.Value.Sent = 0;
                    Send(itr.Current.Value);
                }
            }
        }

        public void DispatchMessageList()
        {
            DispatchInternalMessages();
            DispatchExternalMessages();
        }

        private void DispatchInternalMessages()
        {
            int lastpatchedmessageseqnumber = 0;
            int index = 0;
            var messageitr = IncommingInternalMessageList.GetEnumerator();
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
                    LastInternalDispatchedMessage = message.Header.SequenceNumber;
                    index++;
                }
            }
        }

        private void DispatchExternalMessages()
        {
            int lastpatchedmessageseqnumber = 0;
            int index = 0;
            var messageitr = IncommingExternalMessageList.GetEnumerator();
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
                    LastExternalDispatchedMessage = message.Header.SequenceNumber;
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
            if (Now - LastInternalSendTime > PingInterval)
            {
                Protocol message = new Protocol();
                message.Header.SendType = ProtocolHeader.MessageSendType.Internal;
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

            //else if ((message.Header.SendType == ProtocolHeader.MessageSendType.Ping) && (connectionStatus == ConnectonStaus.Connected))
            //    DispatchPing(message);

            else if ((message.Header.SendType == ProtocolHeader.MessageSendType.External) && (connectionStatus == ConnectonStaus.Connected))
            {
                //Console.WriteLine("RemotePeer: Dispatch External Message:" + message.Header.SequenceNumber);
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
                // go to be in the handshake and on the client side!
//                ResendAllOutgoingMessages(OutgoingCopyForResend);
            }
            else if (message.Data.Data == "HandShake")
            {
                //Console.WriteLine("RemotePeer: DispatchInternal: " + message.Header.SequenceNumber + ":" + message.Data.Data);
                ConnectionStatusChange(ConnectonStaus.Connected);
                ResendAllOutgoingMessages(OutgoingCopyForResend);
                OutgoingCopyForResend.Clear();
            }
            else if (connectionStatus == ConnectonStaus.Connected)
            {
                DispatchPing(message);
            }
        }

        private void DispatchPing(Protocol message)
        {
            if (message.Data.Data.Contains("Ping"))
            {
                Console.WriteLine("RemotePeer: DispatchPing: " + message.Header.SequenceNumber + ":" + message.Data.Data);
                Protocol msg = new Protocol();
                msg.Header.SendType = ProtocolHeader.MessageSendType.Internal;
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
                Console.WriteLine("RemotePeer: DispatchPing: " + message.Header.SequenceNumber + ":" + message.Data.Data + ":With RTT: " + LRTT + " with average RTT: " + (TotalPingPongTime / TotalPingPongCount));
            }
        }

        public int AddMessageToOutGoings(Protocol message)
        {
            int sequence = -1;
            if (message.Header.SendType == ProtocolHeader.MessageSendType.External)
            {
                sequence = ExternalGetNewSequenceNumber();
                message.Header.SequenceNumber = sequence;
                ExternalOutGoingMessageList[sequence] = message;
            }
            else if (message.Header.SendType == ProtocolHeader.MessageSendType.Internal)
            {
                sequence = InternalGetNewSequenceNumber();
                message.Header.SequenceNumber = sequence;
                InternalOutGoingMessageList[sequence] = message;
            }
            else
            {
                Console.Write("RemotePeer: WARNING!!!!!!: Sending a message with no send type");
            }

            return sequence;
        }

        private int ExternalGetNewSequenceNumber()
        {
            return ++ExternalOutGoingSequenceNumber;
        }
        private int InternalGetNewSequenceNumber()
        {
            return ++InternalOutGoingSequenceNumber;
        }

        public List<int> GetInternalIncommingMessageSequences(int carriermessage)
        {
            List<int> AckPack = new List<int>();

            var incommingmsgitr = IncommingInternalMessageList.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                int seq = incommingmsgitr.Current.Key;
                AckPack.Add(seq);
            }
            if (!IncommingInternalMessageAckPackList.ContainsKey(carriermessage))
                IncommingInternalMessageAckPackList.Add(carriermessage, AckPack);

            return AckPack;
        }

        public List<int> GetExternalIncommingMessageSequences(int carriermessage)
        {
            List<int> AckPack = new List<int>();

            var incommingmsgitr = IncommingExternalMessageList.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                int seq = incommingmsgitr.Current.Key;
                AckPack.Add(seq);
            }
            if (!IncommingExternalMessageAckPackList.ContainsKey(carriermessage))
                IncommingExternalMessageAckPackList.Add(carriermessage, AckPack);

            return AckPack;
        }

        public void RemoveIncommingMessage()
        {
            RemoveIncommingExternalMessages();
            RemoveIncommingInternalMessages();
        }

        private void RemoveIncommingExternalMessages()
        {
            int lastSequenceNumber = 0;
            int index = 0;
            List<Protocol> IncommingMessageListCopy = IncommingExternalMessageList.Values.ToList();
            var incommingmsgitr = IncommingMessageListCopy.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                //if it's the first message then dispach, if not gotta be sequenced!
                Protocol message = incommingmsgitr.Current;
                if ((index == 0) || (message.Header.SequenceNumber == lastSequenceNumber + 1))
                {
                    lastSequenceNumber = message.Header.SequenceNumber;
                    if (message.Dispatched >= 1 && message.IncommingAckInformDelivered == 1)
                    {
                        Console.Write("RemotePeer: Remove Incomming Message: " + lastSequenceNumber);
                        IncommingExternalMessageList.Remove(lastSequenceNumber);
                        Console.WriteLine(" IncommingMessageList size: " + IncommingExternalMessageList.Count);
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
        private void RemoveIncommingInternalMessages()
        {
            int lastSequenceNumber = 0;
            int index = 0;
            List<Protocol> IncommingMessageListCopy = IncommingInternalMessageList.Values.ToList();
            var incommingmsgitr = IncommingMessageListCopy.GetEnumerator();
            while (incommingmsgitr.MoveNext())
            {
                //if it's the first message then dispach, if not gotta be sequenced!
                Protocol message = incommingmsgitr.Current;
                if ((index == 0) || (message.Header.SequenceNumber == lastSequenceNumber + 1))
                {
                    lastSequenceNumber = message.Header.SequenceNumber;
                    if (message.Dispatched >= 1 && message.IncommingAckInformDelivered == 1)
                    {
                        Console.Write("RemotePeer: Remove Incomming Message: " + lastSequenceNumber);
                        IncommingInternalMessageList.Remove(lastSequenceNumber);
                        Console.WriteLine(" IncommingMessageList size: " + IncommingInternalMessageList.Count);
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
            RemoveOutgoingExternalMessage();
            RemoveOutgoingInternalMessage();
        }

        private void RemoveOutgoingExternalMessage()
        {
            List<Protocol> OutGoingMessageListCopy = ExternalOutGoingMessageList.Values.ToList<Protocol>();
            var outgoingmsgitr = OutGoingMessageListCopy.GetEnumerator();
            while (outgoingmsgitr.MoveNext())
            {
                Protocol message = outgoingmsgitr.Current;
                if (message.OutGoingReceiveAcked == 1)
                {
                    int sendacktime = message.OutGoingAckReceiveTime - message.SentTime;
                    TotalSendTime += sendacktime;
                    TotalSendCount++;
                    Console.Write("RemotePeer: External == Remove Outgoing Message seq: " + message.Header.SequenceNumber);
                    ExternalOutGoingMessageList.Remove(message.Header.SequenceNumber);
                    Console.WriteLine(" OutGoingMessageList size: " + ExternalOutGoingMessageList.Count);
                    int NewRTT = TotalSendTime / TotalSendCount;
                    //Console.WriteLine("RUDPPeer: Total send count is: " + TotalSendCount + " redundant send: " + TotalResendCount);
                    //Console.WriteLine("RUDPPeer: Time to send and get the ack back is: " + sendacktime + " average sendandack: " + NewRTT);
                    if (NewRTT > MessageWithAckRTT)
                        MessageWithAckRTT = NewRTT;
                }
                else
                {
                    break;
                }
            }
        }
        private void RemoveOutgoingInternalMessage()
        {
            List<Protocol> OutGoingMessageListCopy = InternalOutGoingMessageList.Values.ToList<Protocol>();
            var outgoingmsgitr = OutGoingMessageListCopy.GetEnumerator();
            while (outgoingmsgitr.MoveNext())
            {
                Protocol message = outgoingmsgitr.Current;
                if (message.OutGoingReceiveAcked == 1)
                {
                    int sendacktime = message.OutGoingAckReceiveTime - message.SentTime;
                    TotalSendTime += sendacktime;
                    TotalSendCount++;
                    Console.Write("RemotePeer: Internal -- Remove Outgoing Message seq: " + message.Header.SequenceNumber);
                    InternalOutGoingMessageList.Remove(message.Header.SequenceNumber);
                    Console.WriteLine(" OutGoingMessageList size: " + InternalOutGoingMessageList.Count);
                    int NewRTT = TotalSendTime / TotalSendCount;
                    //Console.WriteLine("RUDPPeer: Total send count is: " + TotalSendCount + " redundant send: " + TotalResendCount);
                    //Console.WriteLine("RUDPPeer: Time to send and get the ack back is: " + sendacktime + " average sendandack: " + NewRTT);
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
            SortedDictionary<int, Protocol> OutGoingMessageListCopy = ExternalOutGoingMessageList;
            var outgoingmsgitr = OutGoingMessageListCopy.GetEnumerator();
            while (outgoingmsgitr.MoveNext())
            {
                Protocol message = outgoingmsgitr.Current.Value;
                //TODO This condition must be checked on tests and if required get compeleted by time(RT) or the many times that this has been happpened++
                if ((message.Sent == 1) && (message.OutGoingReceiveAcked == 0) && (message.Header.SequenceNumber < LastAckedSentExternalMessageSequence)
                    && (Now - message.SentTime > MessageWithAckRTT * 2))
                {
                    Console.WriteLine("RemotePeer: message sent turned to 0: " + message.Header.SequenceNumber);
                    message.Sent = 0;
                    TotalResendCount++;
                }
            }
        }
    }
}
