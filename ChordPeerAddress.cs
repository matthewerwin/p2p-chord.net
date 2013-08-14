using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    [Serializable]
    public class ChordPeerAddress
    {
        public string IP { get; private set; }
        public int Port { get; private set; }
        public UInt64 Key { get; private set; }

        public ChordPeerAddress(string ip, int port )
        {
            if( String.IsNullOrWhiteSpace(ip ) )
                IP = "127.0.0.1";
            else IP = ip;

            Port = port;
            Key = Utilities.GetMD5Hash(IP + ":" + port.ToString());
        }

        public override string ToString()
        {
            return IP + ":" + Port.ToString();
        }

        public int CompareTo(object obj)
        {
            if(!(obj is ChordPeerAddress))
                throw new ArgumentException("Invalid comparison object type");
            return this.Key.CompareTo((obj as ChordPeerAddress).Key);
        }

        public override bool Equals(object obj)
        {
            if( obj != null && obj is ChordPeerAddress)
                return (obj as ChordPeerAddress).Key == this.Key;
            return false;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}
