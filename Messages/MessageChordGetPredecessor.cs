using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    [Serializable]
    public class MessageChordGetPredecessor : MessageChord
    {
        public UInt64 Key { get; private set; }
        public MessageChordGetPredecessor(UInt64 key)
            : base()
        {
            Key = key;
        }
    }
}
