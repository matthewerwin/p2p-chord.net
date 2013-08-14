using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    [Serializable]
    public class MessageChordGetPredecessorReply : MessageChord
    {
        public ChordPeerAddress Predecessor { get; private set; }
        public MessageChordGetPredecessorReply(ChordPeerAddress predecessorPeer)
            : base()
        {
            Predecessor = predecessorPeer;
        }
    }
}
