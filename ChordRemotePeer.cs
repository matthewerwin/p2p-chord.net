using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    public class ChordRemotePeer : IDisposable
    {
        public ChordPeerAddress Address { get; private set; }
        private PeerClient Client { get; set; }

        public ChordRemotePeer(string ip, int port)
        {
            Address = new ChordPeerAddress(ip, port);
            Client = new PeerClient(ip, port);
        }

        public ChordRemotePeer(ChordPeerAddress address)
        {
            Address = address;
            Client = new PeerClient(address.IP, address.Port);
        }

        public UInt64 Key { get { return Address.Key; } }

        public override string ToString()
        {
            return Address.ToString( );
        }

        public int CompareTo(object obj)
        {
            if (obj is ChordPeerAddress)
                return this.Address.CompareTo((obj as ChordPeerAddress));
            else if (obj is ChordRemotePeer)
                return this.Address.CompareTo((obj as ChordRemotePeer).Address);

            throw new ArgumentException("Invalid comparison object type");
        }

        public override bool Equals(object obj)
        {
            if(obj != null )
            {
                if(obj is ChordPeerAddress)
                    return (obj as ChordPeerAddress).Equals(this.Address);
                else if (obj is ChordRemotePeer)
                    return (obj as ChordRemotePeer).Address.Equals(this.Address);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public void Dispose()
        {
            if (Client != null)
            {
                Client.Dispose();
                Client = null;
            }
        }
    }
}
