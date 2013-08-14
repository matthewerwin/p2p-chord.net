using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    [Serializable]
    public class MessageChordFindSuccessorReply : MessageChord
    {
        public ChordPeerAddress Successor { get; private set; }
        public MessageChordFindSuccessorReply(ChordPeerAddress successorPeer)
            : base()
        {
            Successor = successorPeer;
        }
    }
}
