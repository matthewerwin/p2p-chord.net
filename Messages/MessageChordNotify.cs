using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    [Serializable]
    public class MessageChordNotify : MessageChord
    {
        public ChordPeerAddress Peer { get; private set; }
        public MessageChordNotify(ChordPeerAddress peer )
            : base()
        {
            Peer = peer;
        }
    }
}
