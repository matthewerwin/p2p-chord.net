using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    [Serializable]
    public class MessageChordFindSuccessor : MessageChord
    {
        public UInt64 Key { get; private set; }
        public MessageChordFindSuccessor(UInt64 key)
            : base()
        {
            Key = key;
        }
    }
}
