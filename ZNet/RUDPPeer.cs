using System;
using System.Net;
using System.Collections.Generic;

namespace ZNet
{
    public class RUDPPeer
    {
        // TODO the call back should tell Connection status change happend to which remote peer, unless log on server peer would be reddecules!
        public event Action<ConnectonStaus, RemotePeer> OnConnectionStatusChange;
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
            //TODO is it reqired to check for the connect result?

            System.Net.IPAddress RemotePeerIPAddress = System.Net.IPAddress.Parse(IPV4IP);
            System.Net.IPEndPoint RemotePeerEndPoint = new System.Net.IPEndPoint(RemotePeerIPAddress, port);

            RemotePeer remotepeer = TouchPeer(RemotePeerEndPoint);
            if (remotepeer.RemotePeerType == RemotePeer.RemotePeerTypes.NotDeterminedYet)
                remotepeer.RemotePeerType = RemotePeer.RemotePeerTypes.Master;

            Protocol message = new Protocol();
            message.Header.SendType = ProtocolHeader.MessageSendType.Internal;
            message.Data.Data = "Hello";

            Send(remotepeer, message);

            OnConnectionStatusChange(ConnectonStaus.Connecting, remotepeer);

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
                    if (peer.RemotePeerType == RemotePeer.RemotePeerTypes.NotDeterminedYet)
                        peer.RemotePeerType = RemotePeer.RemotePeerTypes.Slave;
                    Protocol message = new Protocol();
                    message.DeserializeFromBytes(incomming);

                    peer.IncommingMessageList.Add(message);
                    peer.MarkToRemoveIncommings(message.Header.AckList);
                    peer.OutGoingAcksReceived(message.Header.AckList);
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
            var itr = RemotePeerList.GetEnumerator();
            while (itr.MoveNext())
            {
                if (itr.Current.RemotePeerType == RemotePeer.RemotePeerTypes.Master)
                {
                    itr.Current.Ping();
                }
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
                    if (msgitr.Current.Sent == 0)
                        RealSend(itr.Current, msgitr.Current);
                }
            }
        }

        private void RealSend(RemotePeer remotepeer, Protocol message)
        {
            message.Header.AckList = remotepeer.GetIncommingMessageSequences(message.Header.SequenceNumber);
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
                            lastSequenceNumber = message.Header.SequenceNumber;
                        }
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
                var incommingmsgitr = peeritr.Current.IncommingMessageList.GetEnumerator();
                while (incommingmsgitr.MoveNext())
                {
                    //if it's the first message then dispach, if not gotta be sequenced!
                    Protocol message = incommingmsgitr.Current;
                    if ((message.Header.SequenceNumber == lastSequenceNumber - 1))
                    {
                        if (message.Dispatched == 1 && message.ReadyToDelete == 1)
                        {
                            //it's ok to delete this message as we know that if it has been dispatched, it's pervious messages has been dispatched too. So,
                            //maybe there would be messages before this that we don't delete but ofc they have been dispatched before.
                            peeritr.Current.IncommingMessageList.Remove(message);
                            lastSequenceNumber = message.Header.SequenceNumber;
                        }
                    }
                    else
                    {
                        //TODO or return?!
                        //break;
                    }
                }
                peeritr.Current.IncommingMessageList.Reverse();
            }
        }

        private void Dispatch(RemotePeer senderpeer, Protocol message)
        {
            if (message.Header.SendType == ProtocolHeader.MessageSendType.Internal)
                DispatchInternal(senderpeer, message);

            if (message.Header.SendType == ProtocolHeader.MessageSendType.Ping)
                DispatchPing(senderpeer, message);

            if (message.Header.SendType == ProtocolHeader.MessageSendType.External)
                OnMessageReceive(message.Data.Data);

            message.Dispatched++;
        }

        private void DispatchPing(RemotePeer senderpeer, Protocol message)
        {
            if (message.Data.Data == "Ping")
            {
                Protocol msg = new Protocol();
                msg.Header.SendType = ProtocolHeader.MessageSendType.Ping;
                msg.Data.Data = "Pong";
                Send(senderpeer, msg);
            }
            if (message.Data.Data == "Pong")
            {
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
                OnConnectionStatusChange(ConnectonStaus.Connected, senderpeer);
            }
            if (message.Data.Data == "HandShake")
            {
                OnConnectionStatusChange(ConnectonStaus.Connected, senderpeer);
            }
        }
    }
}