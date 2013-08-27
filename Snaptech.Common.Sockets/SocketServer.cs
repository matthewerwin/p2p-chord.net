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

namespace Snaptech.Common.Sockets
{
    public class SocketServer : SocketBase
    {
        private Socket ListeningSocket { get; set; }

        /// <summary>
        /// Create a new listening server on IP:Port and hook into delegates for send/receive
        /// </summary>
        /// <param name="ip">Address to bind to</param>
        /// <param name="port">Port to bind to</param>
        /// <param name="messageSentHandler">Consider an interface vs a delegate</param>
        /// <param name="messageReceivedHandler">Consider an interface vs a delegate</param>
        public SocketServer(string ip, int port, IMessage message) :
            base( string.IsNullOrWhiteSpace(ip) ? IPAddress.Any : IPAddress.Parse(ip), port, true, message)
        {
            OnAcceptEventHandler = new EventHandler<SocketAsyncEventArgs>(OnAccept);
        }

        public SocketServer(int port, IMessage message)
            : this(null, port, message)
        {
        }

        public Task ListenAsync(Func<ConnectedClient,IMessage,Task> messageHandler)
        {
            TaskToken listenToken = new TaskToken(messageHandler);
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

        private void CreateOutstandingAccept(TaskToken lt)
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
            TaskToken lt = (acceptEvent.UserToken as TaskToken);
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

        private void ContinouslyReceive(TaskToken lt, ConnectedClient clientSocket )
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
