using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;

namespace Snaptech.Common.Sockets
{
	public interface IMessage
	{
		MemoryStream Serialize();
		IMessage Deserialize(SocketAsyncEventArgs args);
	}

    [Serializable]
    public abstract class MessageBase : IMessage
    {
		public virtual MemoryStream Serialize()
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

		public virtual IMessage Deserialize(SocketAsyncEventArgs args)
        {
			using (MemoryStream ms = new MemoryStream(args.Buffer, 0, args.Offset + args.BytesTransferred))
            {
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    BinaryFormatter bf = new BinaryFormatter();
					return bf.Deserialize(ds) as IMessage;
                }
            }
        }
    }
}
