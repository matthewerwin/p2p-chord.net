using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;

namespace Snaptech.Common.Sockets
{
    public class SocketClient : SocketBase
    {
        /// <summary>
        /// TO DO: make sure this gets disposed!
        /// </summary>
        private ConnectedClient ConnectedSocket { get; set; }


        /// <summary>
        /// Create a new listening server on IP:Port and hook into delegates for send/receive
        /// </summary>
        /// <param name="ip">Address to bind to</param>
        /// <param name="port">Port to bind to</param>
        /// <param name="messageSentHandler">Consider an interface vs a delegate</param>
        /// <param name="messageReceivedHandler">Consider an interface vs a delegate</param>
        public SocketClient(string ip, int port, IMessage message) :
            base(IPAddress.Parse(ip), port, false, message)
        {
            OnConnectEventHandler = new EventHandler<SocketAsyncEventArgs>(OnConnect);
        }

        public async Task SendAsync( IMessage message )
        {
            if (ConnectedSocket == null || !ConnectedSocket.Socket.Connected)
                throw new Exception("Client is not connected");

            await SendMessageAsync(ConnectedSocket, message);
        }

        public async Task<IMessage> ReceiveAsync( )
        {
            if (ConnectedSocket == null || !ConnectedSocket.Socket.Connected)
                throw new Exception("Client is not connected");

            return await ReceiveMessageAsync(ConnectedSocket);
        }

        public Task DisconnectAsync()
        {
            return base.DisconnectAsync(ConnectedSocket);
        }

		public Task ConnectAsync(IMessage message = null, Func<ConnectedClient, IMessage, Task> receiveMessageHandler = null)
        {
			TaskCompletionSource<object> tcs;
			TaskToken taskToken = null;

			if (receiveMessageHandler != null)
			{
				taskToken = new TaskToken(receiveMessageHandler);
				tcs = taskToken.TaskCompletionSource;
			}
			else
				tcs = new TaskCompletionSource<object>();

            try
            {
				SocketAsyncEventArgs connectEvent = (taskToken != null ? EventArgsPool.TakeItem(taskToken) : EventArgsPool.TakeItem(tcs));

                try
                {
                    //connectEvent.UserToken = tcs;
                    connectEvent.Completed += OnConnectEventHandler;
                    if (message != null)
                        base.SerializeMessage(connectEvent, message);

                    IPEndPoint ipEndPoint = new IPEndPoint(IP, Port);
                    connectEvent.RemoteEndPoint = ipEndPoint;
                    Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true); // no NAGLE since we're acting like UDP

                    if (!socket.ConnectAsync(connectEvent))
                        OnConnect(null, connectEvent); //synchronous completion
                }
                catch { RecycleEventArgs(connectEvent); throw; }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            return tcs.Task;
        }

        private void OnConnect(object sender, SocketAsyncEventArgs connectEvent)
        {
			TaskToken taskToken = null;
			TaskCompletionSource<object> tcs;
			
			if (connectEvent.UserToken is TaskToken)
			{
				taskToken = connectEvent.UserToken as TaskToken;
				tcs = taskToken.TaskCompletionSource;
			}
			else
				tcs = connectEvent.UserToken as TaskCompletionSource<object>;

            try
            {
                try
                {
                    //technically we might need to check if BytesTransferred == Count if Connect/Send were combined
                    if (connectEvent.SocketError == SocketError.Success)
                    {
                        ConnectedSocket = new ConnectedClient(connectEvent.ConnectSocket);
						tcs.TrySetResult(connectEvent.SocketError);

						if (taskToken != null)
							ContinouslyReceive(taskToken, new ConnectedClient(connectEvent.ConnectSocket));
                    }
                    else
                    {
                        tcs.TrySetException(new Exception( connectEvent.SocketError.ToString( )));
                        CloseClientSocket(new ConnectedClient(connectEvent.ConnectSocket));
                    }
                }
                finally { RecycleEventArgs(connectEvent); }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }

		private void ContinouslyReceive(TaskToken lt, ConnectedClient clientSocket)
		{
			try
			{
				ReceiveMessageAsync(clientSocket).ContinueWith(
					 (x) =>
					 {
						 try
						 {
							 if (x.IsCompleted)
							 {
								 IMessage message = x.Result;
								 if (message != null)
								 {
									 ContinouslyReceive(lt, clientSocket);
									 try { lt.MessageHandler(clientSocket, message); }
									 catch { } //eat any exception that's unhandled by the delegate! 
								 }
							 }
							 else
							 {
								 CloseClientSocket(clientSocket);
								 if (x.IsFaulted)
									 lt.TaskCompletionSource.TrySetException(x.Exception);
								 else if (x.IsCanceled)
									 lt.TaskCompletionSource.TrySetCanceled();
							 }
						 }
						 catch (Exception ex)
						 {
							 try { CloseClientSocket(clientSocket); }
							 catch { }
							 lt.TaskCompletionSource.TrySetException(ex);
						 }
					 });
			}
			catch (Exception ex) { lt.TaskCompletionSource.TrySetException(ex); }
		}

        public new void Dispose()
        {
            base.Dispose();
            if (ConnectedSocket != null)
            {
                using (ConnectedSocket) { }
                ConnectedSocket = null;
            }
        }
    }
}

