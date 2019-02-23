using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZNet
{
    public class Host
    {
        List<RUDPPeer> RUDPPeerList = new List<RUDPPeer>();
        public Host()
        {
            Console.WriteLine("ZNet: Host constructed");
        }

        public RUDPPeer CreateRUDPPeer()
        {
            RUDPPeer peer = new RUDPPeer();
            RUDPPeerList.Add(peer);
            Console.WriteLine("ZNet: RUDPPeer created");
            return peer;
        }

        public void DestroyRUDPPeer(RUDPPeer peer)
        {
            RUDPPeerList.Remove(peer);
            peer.Destroy();
            Console.WriteLine("ZNet: RUDPPeer Destroyed : TODO");
        }

        public void ServiceAllPeers()
        {
            var peer = RUDPPeerList.GetEnumerator();
            while (peer.MoveNext())
            {
                peer.Current.Service();
            }
        }

        public void Destroy()
        {
            List<RUDPPeer> RUDPPeerListCopy = RUDPPeerList;
            var peer = RUDPPeerListCopy.GetEnumerator();
            while (peer.MoveNext())
            {
                DestroyRUDPPeer(peer.Current);
            }
            Console.WriteLine("ZNet: Host Destroyed");
        }
    }
}
