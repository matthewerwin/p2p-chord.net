using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    public class ListenToken
    {
        public TaskCompletionSource<object> TaskCompletionSource { get; private set; }
        public Func<ConnectedClient, Message,Task> MessageHandler { get; private set; }

        public ListenToken( Func<ConnectedClient,Message,Task> messageHandler )
        {
            TaskCompletionSource = new TaskCompletionSource<object>();
            MessageHandler = messageHandler;
        }
    }

    public class ConnectedClient : IDisposable
    {
        public Socket ConnectedSocket { get; private set; }
        public ConnectedClient(Socket clientSocket)
        {
            ConnectedSocket = clientSocket;
        }

        public bool Connected
        {
            get
            {
                lock (this)
                {
                    if (ConnectedSocket != null)
                        return ConnectedSocket.Connected;
                    return false;
                }
            }
        }

        public void Dispose(bool finalizer)
        {
            lock (this)
            {
                if (ConnectedSocket != null)
                {
                    ConnectedSocket.Shutdown(SocketShutdown.Both);
                    ConnectedSocket.Close();

                    if (finalizer)
                    {
                        using (ConnectedSocket) { }
                        ConnectedSocket = null;
                    }
                }
            }
        }
    }

    public class PeerServer : NetworkPeer
    {
        private Socket ListeningSocket { get; set; }

        /// <summary>
        /// Create a new listening server on IP:Port and hook into delegates for send/receive
        /// </summary>
        /// <param name="ip">Address to bind to</param>
        /// <param name="port">Port to bind to</param>
        /// <param name="messageSentHandler">Consider an interface vs a delegate</param>
        /// <param name="messageReceivedHandler">Consider an interface vs a delegate</param>
        public PeerServer(string ip, int port) :
            base( string.IsNullOrWhiteSpace(ip) ? IPAddress.Any : IPAddress.Parse(ip), port, true)
        {
            OnAcceptEventHandler = new EventHandler<SocketAsyncEventArgs>(OnAccept);
        }

        public PeerServer(int port)
            : this(null, port)
        {
        }

        public Task ListenAsync(Func<ConnectedClient,Message,Task> messageHandler)
        {
            ListenToken listenToken = new ListenToken(messageHandler);
            try
            {
                ListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                ListeningSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true); // no NAGLE since we're acting like UDP

                IPEndPoint ipEndPoint = new IPEndPoint(IP, Port);
                ListeningSocket.Bind(ipEndPoint);
                ListeningSocket.Listen(MAX_CONNECTIONS);

                for (int i = 0; i < Environment.ProcessorCount * 2; i++)
                    CreateOutstandingAccept(listenToken);
            }
            catch (Exception ex) { listenToken.TaskCompletionSource.TrySetException(ex); }
            return listenToken.TaskCompletionSource.Task;
        }

        private void CreateOutstandingAccept(ListenToken lt)
        {
            TaskCompletionSource<object> tcs = lt.TaskCompletionSource;
            try
            {
                SocketAsyncEventArgs acceptEvent = EventArgsPool.TakeItem(lt);
                try
                {
                    acceptEvent.Completed += OnAcceptEventHandler;
                    acceptEvent.UserToken = lt;
                    if (!ListeningSocket.AcceptAsync(acceptEvent))
                        OnAccept(null, acceptEvent); //synchronous completion
                }
                catch { RecycleEventArgs(acceptEvent); throw; }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }

        private void OnAccept(object sender, SocketAsyncEventArgs acceptEvent)
        {
            ListenToken lt = (acceptEvent.UserToken as ListenToken);
            try
            {
                try
                {
                    CreateOutstandingAccept(lt); //replace the spent accept call immediately
                    if (acceptEvent.SocketError == SocketError.Success)
                        ContinouslyReceive(lt, new ConnectedClient(acceptEvent.AcceptSocket));
                    else
                        CloseClientSocket(new ConnectedClient(acceptEvent.AcceptSocket));
                }
                finally { RecycleEventArgs(acceptEvent); }
            }
            catch (Exception ex) { lt.TaskCompletionSource.TrySetException(ex); }
        }

        private void ContinouslyReceive(ListenToken lt, ConnectedClient clientSocket )
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
                                 Message message = x.Result;
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
            if (ListeningSocket != null)
            {
                using (ListeningSocket)
                {
                    ListeningSocket.Shutdown(SocketShutdown.Both);
                    ListeningSocket.Close();
                }
                ListeningSocket = null;
            }
        }
    }
}
