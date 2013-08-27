using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Snaptech.Common.Sockets
{
	public class ConnectedClient : IDisposable
	{
		public Socket Socket { get; private set; }
		public ConnectedClient(Socket clientSocket)
		{
			Socket = clientSocket;
		}

		public bool finalized = false;
		~ConnectedClient()
		{
			lock (this)
			{
				if (Socket != null)
				{
					Socket.Close();
					Socket = null;
					GC.SuppressFinalize(this);
				}
			}
		}

		public void Dispose()
		{
			lock (this)
			{
				if (Socket != null)
				{
					if (Socket.Connected)
						Socket.Shutdown(SocketShutdown.Both);
				}
			}
		}
	}
}
