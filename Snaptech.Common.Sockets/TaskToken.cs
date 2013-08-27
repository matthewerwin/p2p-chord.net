using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snaptech.Common.Sockets
{
	public class TaskToken
	{
		public TaskCompletionSource<object> TaskCompletionSource { get; private set; }
		public Func<ConnectedClient, IMessage, Task> MessageHandler { get; private set; }

		public TaskToken(Func<ConnectedClient, IMessage, Task> messageHandler)
		{
			TaskCompletionSource = new TaskCompletionSource<object>();
			MessageHandler = messageHandler;
		}
	}
}
