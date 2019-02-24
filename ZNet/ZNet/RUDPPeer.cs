using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace ZNet
{
    public class RUDPPeer
    {
        Socket socket;
        const int buff_size = 10 * 1024 * 1024;
        private EndPoint SenderEndPoint;
        private byte[] incommingBuffer = new byte[10 * 1024 * 1024];

        private static object isLock = new object();
        private List<RemotePeer> RemotePeerList = new List<RemotePeer>();

        public event Action<ConnectonStaus, RemotePeer> OnConnectionStatusChange;
        public event Action<string, RemotePeer> OnMessageReceive;

        //===========================================================================================
        public RUDPPeer()
        {
            InitializeSocket();

            IPAddress tempaddress = IPAddress.Parse("127.0.0.1");
            IPEndPoint tmpendpoint = new IPEndPoint(tempaddress, 0);
            SenderEndPoint = (EndPoint)tmpendpoint;

            Console.WriteLine("RUDPPeer: RUDPPeer constructed");

            System.Threading.Thread ReceiveThread = new System.Threading.Thread(new System.Threading.ThreadStart(ReceiveLoop));
            ReceiveThread.Start();

            Console.WriteLine("RUDPPeer: ReceiveLoop thread created and started");

        }

        private void InitializeSocket()
        {
            lock (isLock)
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReceiveBuffer, buff_size);
                socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.SendBuffer, buff_size);
                socket.SendBufferSize = buff_size;
                socket.ReceiveBufferSize = buff_size;

                socket.Blocking = false;

                Console.WriteLine("RUDPPeer: socket initiated");
            }
        }

        public void Destroy()
        {
            Console.WriteLine("RUDPPeer: RUDPPeer Destroyed");
        }

        public RemotePeer Connect(string IPV4IP, int port)
        {
            socket.Connect(IPV4IP, port);

            Console.WriteLine("RUDPPeer: socket Connected to: " + IPV4IP + ":" + port);

            System.Net.IPAddress RemotePeerIPAddress = System.Net.IPAddress.Parse(IPV4IP);
            System.Net.IPEndPoint RemotePeerEndPoint = new System.Net.IPEndPoint(RemotePeerIPAddress, port);

            RemotePeer remotepeer = TouchPeer(RemotePeerEndPoint);
            remotepeer.RemotePeerType = RemotePeerTypes.Master;
            //SortedDictionary<int, Protocol > outgoingtmp = remotepeer.OutGoingMessageList;
            //remotepeer.Reset();

            Protocol message = new Protocol();
            message.Header.SendType = ProtocolHeader.MessageSendType.Internal;
            message.Data.Data = "Hello";
            remotepeer.Send(message);
            // TDOO this line should be moved to ManageRemoteConnections to reconnect and stuff
            //remotepeer.ResendAllOutgoingMessages(outgoingtmp);

            remotepeer.ConnectionStatusChange(ConnectonStaus.Connecting);
            return remotepeer;
        }

        public void Bind(string IPV4, int port)
        {
            Console.WriteLine("RUDPPeer: socket binded to: " + IPV4 + ":" + port);
            socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(IPV4), port));
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
                if (socket.Available > 0)
                {
                    int result = socket.ReceiveFrom(incommingBuffer, SocketFlags.None, ref SenderEndPoint);
                    if (result > 0)
                    {
                        IPEndPoint ipendpoint = (IPEndPoint)SenderEndPoint;
                        RemotePeer remotepeer = TouchPeer(ipendpoint);

                        Protocol message = new Protocol();
                        message.DeserializeFromBytes(incommingBuffer);

                        // if I don't know you, the message gotta be internal and a Hello! unless I'm gonna destroy that!
                        if (remotepeer.RemotePeerType == RemotePeerTypes.NotDeterminedYet)
                        {
                            if ((message.Header.SendType == ProtocolHeader.MessageSendType.Internal) && (message.Data.Data == "Hello"))
                                remotepeer.RemotePeerType = RemotePeerTypes.Slave;
                            else
                            {
                                removeRemotePeer(remotepeer);
                                return;
                            }
                        }

                        if (message.Header.SequenceNumber < remotepeer.LastDispatchedMessage)
                            return;

                        remotepeer.MessageReceived(message);
                        remotepeer.MarkIncommingsforAckDelivery(message.Header.AckList);
                        remotepeer.OutGoingAcksReceived(message.Header.AckList);
                    }
                }
            }
            catch (Exception e)
            { }
        }

        public void Service()
        {
            lock (isLock)
            {
                DispatchReceivedMessage();
                CheckConnectivity();
                SendOutGoingMessages();
                RemoveIncommingMessages();
                RemoveOutGoingMessage();
                ResendOutGoingMessage();
                ManageRemoteConnections();
            }
        }

        private void DispatchReceivedMessage()
        {
            var remotepeeritr = RemotePeerList.GetEnumerator();
            while (remotepeeritr.MoveNext())
            {
                remotepeeritr.Current.DispatchMessageList();
            }
        }

        private void CheckConnectivity()
        {
            var itr = RemotePeerList.GetEnumerator();
            while (itr.MoveNext())
            {
                itr.Current.CheckConnectivity();
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
                    if (msgitr.Current.Value.Sent == 0)
                        RealSend(itr.Current, msgitr.Current.Value);
                }
            }
        }

        private void RealSend(RemotePeer remotepeer, Protocol message)
        {
            int Now = System.Environment.TickCount & Int32.MaxValue;
            message.SentTime = Now;
            message.Header.AckList = remotepeer.GetIncommingMessageSequences(message.Header.SequenceNumber);
            remotepeer.LastSendTime = System.Environment.TickCount & Int32.MaxValue;
            byte[] tosendbuffer = message.SerializeToBytes();
            Console.WriteLine("RUDPPeer: RealSend: " + message.Header.SequenceNumber + ":" + message.Data.Data);

            int e = socket.SendTo(tosendbuffer, remotepeer.ipEndPoint);
            message.Sent++;
        }

        private void RemoveIncommingMessages()
        {
            var peeritr = RemotePeerList.GetEnumerator();
            while (peeritr.MoveNext())
            {
                peeritr.Current.RemoveIncommingMessage();
            }
        }

        private void RemoveOutGoingMessage()
        {
            var peeritr = RemotePeerList.GetEnumerator();
            while (peeritr.MoveNext())
            {
                peeritr.Current.RemoveOutgoingMessage();
            }
        }

        private void ResendOutGoingMessage()
        {
            var peeritr = RemotePeerList.GetEnumerator();
            while (peeritr.MoveNext())
            {
                peeritr.Current.ResendOutgoingMessage();
            }
        }

        private void ManageRemoteConnections()
        {
            RemotePeer [] RemotePeerListCopy = RemotePeerList.ToArray();
            var peeritr = RemotePeerListCopy.GetEnumerator();
            while (peeritr.MoveNext())
            {
                RemotePeer tmp = (RemotePeer)peeritr.Current;
                if (tmp.RemotePeerType == RemotePeerTypes.Master)
                {
                    if (tmp.connectionStatus == ConnectonStaus.Disconnected)
                    {
                        // get a copy of the outgoing messages.
                        SortedDictionary<int, Protocol> outgoingcopy = tmp.OutGoingMessageList;
                        // disconnect
                        Disconnect();
                        // removeRemotePeer the remotepeer
                        IPEndPoint ipendpoint = tmp.ipEndPoint;
                        Console.WriteLine("RUDPPeer: Remotepeer removed from RemotePeerList: " + tmp.ipEndPoint.Address.ToString() + ":" + tmp.ipEndPoint.Port);
                        RemotePeerList.Remove(tmp);
                        // connect
                        InitializeSocket();
                        RemotePeer newpeer = Connect(ipendpoint.Address.ToString(), ipendpoint.Port);
                        // resend
                        newpeer.OutgoingCopyForResend = outgoingcopy;
                    }
                }
                else if (tmp.RemotePeerType == RemotePeerTypes.Slave)
                {
                    if (tmp.connectionStatus == ConnectonStaus.Disconnected)
                    {
                        // remove the remotepeer with all it has
                        Console.WriteLine("RUDPPeer: Remotepeer removed from RemotePeerList: " + tmp.ipEndPoint.Address.ToString() + ":" + tmp.ipEndPoint.Port);
                        RemotePeerList.Remove(tmp);
                    }
                }
                else if (tmp.RemotePeerType == RemotePeerTypes.NotDeterminedYet)
                { throw new NotImplementedException(); }
            }
        }

        private void Disconnect()
        {
            Console.WriteLine(" RUDPPeer: Disconnect");
            socket.Shutdown(System.Net.Sockets.SocketShutdown.Both);
            socket.Close();
            //socket.Disconnect(false);
        }
        //===========================================================================================
        public RemotePeer TouchPeer(IPEndPoint remotePeerEndPoint)
        {
            RemotePeer rp = GetRemotePeer(remotePeerEndPoint);
            if (rp != null)
                return rp;

            rp = CreateRemotePeer(remotePeerEndPoint);
            return rp;
        }

        public RemotePeer CreateRemotePeer(IPEndPoint remotePeerEndPoint)
        {
            RemotePeer rp = new RemotePeer();
            rp.ipEndPoint = remotePeerEndPoint;
            rp.OnConnectionStatusChange += OnConnectionStatusChange;
            rp.OnMessageReceive += OnMessageReceive;
            RemotePeerList.Add(rp);
            Console.WriteLine("RUDPPeer: RemotePeer Created: " + remotePeerEndPoint.Address.ToString() + ":" + remotePeerEndPoint.Port);

            return rp;
        }

        public RemotePeer GetRemotePeer(IPEndPoint remotePeerEndPoint)
        {
            var remotepeer = RemotePeerList.GetEnumerator();
            while (remotepeer.MoveNext())
            {
                IPEndPoint currentipendpoint = remotepeer.Current.ipEndPoint;
                if (currentipendpoint.Equals(remotePeerEndPoint))
                    return remotepeer.Current;
            }
            return null;
        }

        public void removeRemotePeer(RemotePeer remotepeer)
        {
            if (RemotePeerList.Contains(remotepeer))
                RemotePeerList.Remove(remotepeer);
        }
        //===========================================================================================
    }
}
