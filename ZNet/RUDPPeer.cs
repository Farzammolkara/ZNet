using System;
using System.Net;
using System.Collections.Generic;

namespace ZNet
{
    public class RUDPPeer
    {
        // TODO the call back should tell Connection status change happend to which remote peer, unless log on server peer would be reddecules!
        public event Action<RemotePeer.ConnectonStaus, RemotePeer> OnConnectionStatusChange;
        public event Action<string> OnMessageReceive;

        private System.Net.Sockets.Socket socket;
        private System.Net.EndPoint SenderEndPoint;
        const int buff_size = 10 * 1024 * 1024;
        private byte[] incommingBuffer = new byte[10 * 1024 * 1024];

        List<RemotePeer> RemotePeerList = new List<RemotePeer>();

        private static object isLock = new object();

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
            //TODO is it reqired to check for the connect result?

            System.Net.IPAddress RemotePeerIPAddress = System.Net.IPAddress.Parse(IPV4IP);
            System.Net.IPEndPoint RemotePeerEndPoint = new System.Net.IPEndPoint(RemotePeerIPAddress, port);

            RemotePeer remotepeer = TouchPeer(RemotePeerEndPoint);
            if (remotepeer.RemotePeerType == RemotePeer.RemotePeerTypes.NotDeterminedYet)
                remotepeer.RemotePeerType = RemotePeer.RemotePeerTypes.Master;

            remotepeer.ResetReceives();
            remotepeer.ResetSends();

            Protocol message = new Protocol();
            message.Header.SendType = ProtocolHeader.MessageSendType.Internal;
            message.Data.Data = "Hello";

            Send(remotepeer, message);

            //ConnectionStatusChange(ConnectonStaus.Connecting, remotepeer);
            remotepeer.ConnectionStatusChange(RemotePeer.ConnectonStaus.Connecting);

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
            RemotePeer rp = new RemotePeer(OnConnectionStatusChange);
            rp.ipEndPoint = remotePeerEndPoint;
            RemotePeerList.Add(rp);

            return rp;
        }

        private RemotePeer GetPeer(IPEndPoint remotePeerEndPoint)
        {
            var itr = RemotePeerList.GetEnumerator();
            while (itr.MoveNext())
            {
                IPEndPoint tmp = itr.Current.ipEndPoint;
                if (tmp.Equals(remotePeerEndPoint))
                    return itr.Current;
            }
            return null;
        }

        private void RemovePeer(RemotePeer remotepeer)
        {
            if (RemotePeerList.Contains(remotepeer))
                RemotePeerList.Remove(remotepeer);
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
                    int result = socket.ReceiveFrom(incommingBuffer, System.Net.Sockets.SocketFlags.None, ref SenderEndPoint);
                    if (result > 0)
                    {
                        IPEndPoint ipendpoint = (IPEndPoint)SenderEndPoint;
                        RemotePeer remotepeer = TouchPeer(ipendpoint);

                        Protocol message = new Protocol();
                        message.DeserializeFromBytes(incommingBuffer);

                        // if I don't know you, the message gotta be internal and a Hello! unless I'm gonna destroy that!
                        if (remotepeer.RemotePeerType == RemotePeer.RemotePeerTypes.NotDeterminedYet)
                        {
                            if ((message.Header.SendType == ProtocolHeader.MessageSendType.Internal) && (message.Data.Data == "Hello"))
                                remotepeer.RemotePeerType = RemotePeer.RemotePeerTypes.Slave;
                            else
                            {
                                RemovePeer(remotepeer);
                                return;
                            }
                        }

                        if (message.Header.SequenceNumber < remotepeer.LastDispatchedMessage)
                            return;

                        remotepeer.MessageReceived(message);
                        remotepeer.MarkToRemoveIncommings(message.Header.AckList);
                        remotepeer.OutGoingAcksReceived(message.Header.AckList);
                    }
                }
            }
            catch (Exception e)
            { }
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
                CheckConnectivity();
                SendOutGoingMessages();
                RemoveIncommingMessages();
            }
        }

        private void CheckConnectivity()
        {
            List<RemotePeer> RemotePeerListCopy = RemotePeerList;
            var itr = RemotePeerListCopy.GetEnumerator();
            while (itr.MoveNext())
            {
                //if (itr.Current.RemotePeerType == RemotePeer.RemotePeerTypes.Master)
                //{
                RemotePeer tmp = itr.Current;
                if (tmp.GetConnectionStatus() == RemotePeer.ConnectonStaus.Disconnected)
                {
                    
                    if (tmp.RemotePeerType == RemotePeer.RemotePeerTypes.Master)
                    {
                        Console.WriteLine("Disc 1");
                        // TODO Reconnect
                        tmp.ConnectionStatusChange(RemotePeer.ConnectonStaus.Disconnected);
                        Disconnect();
                        Connect(tmp.ipEndPoint.Address.ToString(), tmp.ipEndPoint.Port);
                        Console.WriteLine("Disc 2");
                    }
                    else
                    {
                        //TODO delete the peer
                        Console.WriteLine("Disc 3");
                        tmp.ConnectionStatusChange(RemotePeer.ConnectonStaus.Disconnected);
                        // TODO: Why?! 
                        //RemotePeerList.Remove(tmp);
                        itr.Current.shouldberemoved = true;
                        Console.WriteLine("Disc 4");
                    }
                }
                else
                {
                    if ((tmp.RemotePeerType == RemotePeer.RemotePeerTypes.Master) && (tmp.GetConnectionStatus() == RemotePeer.ConnectonStaus.Connected))
                    {
                        tmp.Ping();
                    }
                }
                //}
            }
            List<RemotePeer> RemotePeerListCopys = RemotePeerList;
            var itrremove = RemotePeerListCopys.ToArray().GetEnumerator();
            while (itrremove.MoveNext())
            {
                RemotePeer tmp = (RemotePeer)itrremove.Current;
                if (tmp.shouldberemoved)
                {
                    RemotePeerList.Remove(tmp);
                }
            }

        }

        private void Disconnect()
        {
            socket.Shutdown(System.Net.Sockets.SocketShutdown.Both);
            socket.Close();
            socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            //socket.Disconnect(false);
        }

        private void SendOutGoingMessages()
        {
            var itr = RemotePeerList.GetEnumerator();
            while (itr.MoveNext())
            {
                var msgitr = itr.Current.OutGoingMessageList.GetEnumerator();
                while (msgitr.MoveNext())
                {
                    if (msgitr.Current.Sent == 0)
                        RealSend(itr.Current, msgitr.Current);
                }
            }
        }

        private void RealSend(RemotePeer remotepeer, Protocol message)
        {
            message.Header.AckList = remotepeer.GetIncommingMessageSequences(message.Header.SequenceNumber);
            message.Header.DispatchedList = remotepeer.GetDispatchedMessageSequences(message.Header.SequenceNumber);
            byte[] tosendbuffer = message.SerializeToBytes();
            int e = socket.SendTo(tosendbuffer, remotepeer.ipEndPoint);
            message.Sent++;
        }

        private void DispatchReceivedMessage()
        {
            var peeritr = RemotePeerList.GetEnumerator();
            while (peeritr.MoveNext())
            {
                // TODO gotta go inside the remotepeer
                int lastSequenceNumber = 0;
                var incommingmsgitr = peeritr.Current.IncommingMessageList.GetEnumerator();
                while (incommingmsgitr.MoveNext())
                {
                    //if it's the first message then dispach, if not gotta be sequenced!
                    int index = peeritr.Current.IncommingMessageList.IndexOf(incommingmsgitr.Current);
                    Protocol message = incommingmsgitr.Current;
                    if ((index == 0) || (message.Header.SequenceNumber == lastSequenceNumber + 1))
                    {
                        if (message.Dispatched == 0)
                        {
                            Dispatch(peeritr.Current, message);
                        }
                        lastSequenceNumber = message.Header.SequenceNumber;
                    }
                    else
                    {
                        //TODO or return?!
                        break;
                    }
                }
            }
        }

        private void RemoveIncommingMessages()
        {
            var peeritr = RemotePeerList.GetEnumerator();
            while (peeritr.MoveNext())
            {
                // TODO gotta go inside the remotepeer
                int lastSequenceNumber = 0;
                peeritr.Current.IncommingMessageList.Reverse();
                List<Protocol> IncommingMessageListCopy = peeritr.Current.IncommingMessageList;

                var incommingmsgitr = IncommingMessageListCopy.GetEnumerator();
                while (incommingmsgitr.MoveNext())
                {
                    //if it's the first message then dispach, if not gotta be sequenced!
                    Protocol message = incommingmsgitr.Current;
                    int index = IncommingMessageListCopy.IndexOf(incommingmsgitr.Current);

                    if ((index == 0) || (message.Header.SequenceNumber == lastSequenceNumber - 1))
                    {
                        lastSequenceNumber = message.Header.SequenceNumber;
                        if (message.Dispatched == 1 && message.ReadyToDelete == 1)
                        {
                            //it's ok to delete this message as we know that if it has been dispatched, it's pervious messages has been dispatched too. So,
                            //maybe there would be messages before this that we don't delete but ofc they have been dispatched before.
                            // peeritr.Current.IncommingMessageList.Remove(message);
                        }
                    }
                    else
                    {
                        //TODO or return?!
                        //break;
                    }
                }
                peeritr.Current.IncommingMessageList.Reverse();
                //peeritr.Current.IncommingMessageList = IncommingMessageListCopy;
            }
        }

        private void Dispatch(RemotePeer senderpeer, Protocol message)
        {
            if (message.Header.SendType == ProtocolHeader.MessageSendType.Internal)
                DispatchInternal(senderpeer, message);

            if ((message.Header.SendType == ProtocolHeader.MessageSendType.Ping) && (senderpeer.connectionStatus == RemotePeer.ConnectonStaus.Connected))
                DispatchPing(senderpeer, message);

            if ((message.Header.SendType == ProtocolHeader.MessageSendType.External) && (senderpeer.connectionStatus == RemotePeer.ConnectonStaus.Connected))
                OnMessageReceive(message.Data.Data);

            message.Dispatched++;
            senderpeer.LastDispatchedMessage = message.Header.SequenceNumber;
        }

        private void DispatchPing(RemotePeer senderpeer, Protocol message)
        {
            if (message.Data.Data == "Ping")
            {
                Console.WriteLine("ping received: " + message.Header.SequenceNumber);
                Protocol msg = new Protocol();
                msg.Header.SendType = ProtocolHeader.MessageSendType.Ping;
                msg.Data.Data = "Pong";
                Send(senderpeer, msg);
            }
            if (message.Data.Data == "Pong")
            {
                Console.WriteLine("pong received: " + message.Header.SequenceNumber);
            }
        }

        private void DispatchInternal(RemotePeer senderpeer, Protocol message)
        {
            if (message.Data.Data == "Hello")
            {
                Protocol msg = new Protocol();
                msg.Header.SendType = ProtocolHeader.MessageSendType.Internal;
                msg.Data.Data = "HandShake";
                Send(senderpeer, msg);
                senderpeer.ConnectionStatusChange(RemotePeer.ConnectonStaus.Connected);
            }
            if (message.Data.Data == "HandShake")
            {
                senderpeer.ConnectionStatusChange(RemotePeer.ConnectonStaus.Connected);
            }
        }

        //public void ConnectionStatusChange(ConnectonStaus connectionstat, RemotePeer remotepeer)
        //{
        //    remotepeer.connectionStatus = connectionstat;
        //    OnConnectionStatusChange(connectionstat, remotepeer);
        //}
    }
}