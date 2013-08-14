using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    public class PeerClient : NetworkPeer
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
        public PeerClient(string ip, int port) :
            base(IPAddress.Parse(ip), port, false)
        {
            OnConnectEventHandler = new EventHandler<SocketAsyncEventArgs>(OnConnect);
        }

        public async Task SendAsync( Message message )
        {
            if (ConnectedSocket == null || !ConnectedSocket.ConnectedSocket.Connected)
                throw new Exception("Client is not connected");

            await SendMessageAsync(ConnectedSocket, message);
        }

        public async Task<Message> ReceiveAsync( )
        {
            if (ConnectedSocket == null || !ConnectedSocket.ConnectedSocket.Connected)
                throw new Exception("Client is not connected");

            return await ReceiveMessageAsync(ConnectedSocket);
        }

        public Task DisconnectAsync()
        {
            return base.DisconnectAsync(ConnectedSocket);
        }

        public Task ConnectAsync(Message message = null)
        {
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            try
            {
                SocketAsyncEventArgs connectEvent = EventArgsPool.TakeItem(tcs);
                try
                {
                    connectEvent.UserToken = tcs;
                    connectEvent.Completed += OnConnectEventHandler;
                    if (message != null)
                        base.SerializeMessage(connectEvent, message);

                    IPEndPoint ipEndPoint = new IPEndPoint(IP, Port);
                    connectEvent.RemoteEndPoint = ipEndPoint;
                    connectEvent.AcceptSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    connectEvent.AcceptSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true); // no NAGLE since we're acting like UDP

                    if (!connectEvent.AcceptSocket.ConnectAsync(connectEvent))
                        OnConnect(null, connectEvent); //synchronous completion
                }
                catch { RecycleEventArgs(connectEvent); throw; }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            return tcs.Task;
        }

        private void OnConnect(object sender, SocketAsyncEventArgs connectEvent)
        {
            TaskCompletionSource<object> tcs = connectEvent.UserToken as TaskCompletionSource<object>;
            try
            {
                try
                {
                    //technically we might need to check if BytesTransferred == Count if Connect/Send were combined
                    if (connectEvent.SocketError == SocketError.Success)
                    {
                        ConnectedSocket = new ConnectedClient(connectEvent.ConnectSocket);
                        tcs.TrySetResult(connectEvent.SocketError);
                    }
                    else
                    {
                        tcs.TrySetResult(connectEvent.SocketError);
                        CloseClientSocket(new ConnectedClient(connectEvent.ConnectSocket));
                    }
                }
                finally { RecycleEventArgs(connectEvent); }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
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

