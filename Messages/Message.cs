using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    [Serializable]
    public abstract class Message
    {
        public MemoryStream Serialize( )
        {
            MemoryStream ms = new MemoryStream();
            using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ds, this);
                ds.Close();
            }
            ms.Position = 0;
            return ms;
        }

        public static Message Deserialize(SocketAsyncEventArgs messageEvent)
        {
            using (MemoryStream ms = new MemoryStream(messageEvent.Buffer, 0, messageEvent.Offset + messageEvent.BytesTransferred))
            {
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    BinaryFormatter bf = new BinaryFormatter();
                    return bf.Deserialize(ds) as Message;
                }
            }
        }
    }
}
