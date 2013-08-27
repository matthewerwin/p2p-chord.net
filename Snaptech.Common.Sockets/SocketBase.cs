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
    /// <summary>
    /// TO DO: In order to check that everything closes/recovers/etc we should do a test with a state variable containing all
    ///        connected clients and try to shut down everything gracefully and demonstrate that connected client count truly goes
    ///        to zero meaning all were received as disconnected and we can exit.
    /// </summary>
    public abstract class SocketBase : IDisposable
    {
        protected const int LTF_BYTE_COUNT = 2;
        protected const int MAX_CONNECTIONS = int.MaxValue / 2;

        //to avoid fragmenting memory we utilize same size for all operations        
        protected readonly int POOL_BUFFER_SIZE = (int) Math.Pow(2, LTF_BYTE_COUNT * 8);

        protected SocketAsyncEventArgsPool EventArgsPool { get; set; }
        private BufferManager BufferPool { get; set; }

        protected EventHandler<SocketAsyncEventArgs> OnAcceptEventHandler { get; set; }
        protected EventHandler<SocketAsyncEventArgs> OnConnectEventHandler { get; set; }
        protected EventHandler<SocketAsyncEventArgs> OnDisconnectEventHandler { get; set; }
        private EventHandler<SocketAsyncEventArgs> OnReceiveLTFEventHandler { get; set; }
        private EventHandler<SocketAsyncEventArgs> OnReceiveEventHandler { get; set; }
        private EventHandler<SocketAsyncEventArgs> OnSendEventHandler { get; set; }

        public IPAddress IP { get; private set; }
        public int Port { get; private set; }
		private IMessage Message;

        /// <summary>
        /// Create a new listening server on IP:Port and hook into delegates for send/receive
        /// </summary>
        /// <param name="ip">Address to bind to</param>
        /// <param name="port">Port to bind to</param>
        /// <param name="messageSentHandler">Consider an interface vs a delegate</param>
        /// <param name="messageReceivedHandler">Consider an interface vs a delegate</param>
        public SocketBase(IPAddress ip, int port, bool listen, IMessage message, int bufferSize = 0)
        {
            IP = ip;
            Port = port;
			Message = message;

			if (bufferSize > 0)
				POOL_BUFFER_SIZE = bufferSize;

            BufferPool = BufferManager.CreateBufferManager(listen ? MAX_CONNECTIONS * 2 : 100, POOL_BUFFER_SIZE);
            EventArgsPool = new SocketAsyncEventArgsPool(listen ? MAX_CONNECTIONS * 2 : 100);

            OnReceiveLTFEventHandler = new EventHandler<SocketAsyncEventArgs>(OnReceiveLTF);
            OnReceiveEventHandler = new EventHandler<SocketAsyncEventArgs>(OnReceive);
            OnSendEventHandler = new EventHandler<SocketAsyncEventArgs>(OnSend);
            OnDisconnectEventHandler = new EventHandler<SocketAsyncEventArgs>(OnDisconnect);
        }


        /// <summary>
        /// Provides the ability for the caller to look for a response after calling SendMessage()
        /// </summary>
        /// <param name="clientSocket"></param>
		protected Task<IMessage> ReceiveMessageAsync(ConnectedClient clientSocket)
        {
            TaskCompletionSource<IMessage> tcs = new TaskCompletionSource<IMessage>(clientSocket);
            try
            {
                SocketAsyncEventArgs receiveEvent = EventArgsPool.TakeItem(tcs);
                try
                {
                    receiveEvent.AcceptSocket = clientSocket.Socket;
                    receiveEvent.Completed += OnReceiveLTFEventHandler;
                    receiveEvent.SetBuffer(BufferPool.TakeBuffer(POOL_BUFFER_SIZE), 0, LTF_BYTE_COUNT);
                    if (!receiveEvent.AcceptSocket.ReceiveAsync(receiveEvent))
                        OnReceiveLTFEventHandler(null, receiveEvent);
                }
                catch { RecycleEventArgs(receiveEvent); throw; }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            return tcs.Task;
        }

        public Task SendMessageAsync(ConnectedClient clientSocket, IMessage message)
        {
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>(clientSocket);
            try
            {
                SocketAsyncEventArgs sendEvent = EventArgsPool.TakeItem(tcs);
                try
                {
                    sendEvent.AcceptSocket = clientSocket.Socket;
                    sendEvent.Completed += OnSendEventHandler;
                    SerializeMessage(sendEvent, message);

                    if (!sendEvent.AcceptSocket.SendAsync(sendEvent))
                        OnSendEventHandler(null, sendEvent);
                }
                catch { RecycleEventArgs(sendEvent); throw; }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            return tcs.Task;
        }

        protected void SerializeMessage(SocketAsyncEventArgs eventArgs, IMessage message)
        {
            using (MemoryStream ms = message.Serialize())
            {
                byte[] ltf = new byte[LTF_BYTE_COUNT];
                for (int i = 0; i < LTF_BYTE_COUNT; i++)
                    ltf[i] = (byte)(((int)ms.Length) >> (byte)Math.BigMul(8, LTF_BYTE_COUNT - i - 1));

                if (ms.Length > (POOL_BUFFER_SIZE - LTF_BYTE_COUNT)) //use unpooled buffer
                {
                    eventArgs.BufferList = new List<ArraySegment<byte>>()
                        {
                            new ArraySegment<byte>(ltf),
                            new ArraySegment<byte>(ms.GetBuffer())
                        };
                }
                else //use pinned buffer
                {
                    byte[] buffer = BufferPool.TakeBuffer(POOL_BUFFER_SIZE);
                    Buffer.BlockCopy(ltf, 0, buffer, 0, LTF_BYTE_COUNT);
                    ms.Read(buffer, LTF_BYTE_COUNT, (int)ms.Length);
                    eventArgs.SetBuffer(buffer, 0, (int)ms.Length + LTF_BYTE_COUNT);
                }
                ms.Close();
            }
        }

        /// <summary>
        /// We initially request LTF bytes before kicking off the main receive for the body of the message.
        /// The messages themselves can be of any size but once they get bigger than the default buffer size
        /// we are best off using GC buffers rather than pinned buffers.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="receiveEvent"></param>
        private void OnReceiveLTF(object sender, SocketAsyncEventArgs receiveEvent)
        {
            TaskCompletionSource<IMessage> tcs = receiveEvent.UserToken as TaskCompletionSource<IMessage>;
            try
            {
                if (receiveEvent.SocketError != SocketError.Success //error condition
                    || receiveEvent.BytesTransferred <= 0) //remote side closed the connection
                {
					tcs.TrySetException(new Exception(receiveEvent.SocketError.ToString()));
                    try { CloseClientSocket(tcs.Task.AsyncState as ConnectedClient); }
                    finally { RecycleEventArgs(receiveEvent); }
                }
                else
                {
                    try
                    {
                        if (receiveEvent.BytesTransferred < receiveEvent.Count)
                        {
                            receiveEvent.SetBuffer(receiveEvent.Offset + receiveEvent.BytesTransferred, receiveEvent.Count - receiveEvent.BytesTransferred);
                            if (!receiveEvent.AcceptSocket.ReceiveAsync(receiveEvent))
                                OnReceiveLTFEventHandler(null, receiveEvent);
                        }
                        else
                        {
                            receiveEvent.Completed -= OnReceiveLTFEventHandler;
                            receiveEvent.Completed += OnReceiveEventHandler;

                            int ltf = 0;
                            for (int i = 0; i < LTF_BYTE_COUNT; i++)
                                ltf |= receiveEvent.Buffer[i] << (byte)Math.BigMul(8, LTF_BYTE_COUNT - i - 1);

							//// 0 LTF is an empty message so we just ignore it
							//if (ltf == 0)
							//{
							//	tcs.TrySetResult(null);
							//	RecycleEventArgs(receiveEvent);
							//	return;
							//}
                            if (ltf > POOL_BUFFER_SIZE) //for large messages unmanaged buffers are recommended by M$ (64K+)
                            {
                                BufferPool.ReturnBuffer(receiveEvent.Buffer);
                                receiveEvent.SetBuffer(new byte[ltf], 0, ltf); //unmanaged buffer
                            }
                            else
                                receiveEvent.SetBuffer(0, ltf);

                            if(!receiveEvent.AcceptSocket.ReceiveAsync(receiveEvent))
                                OnReceiveEventHandler(null, receiveEvent);
                        }
                    }
                    catch { RecycleEventArgs(receiveEvent); throw; }
                }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }

        private void OnReceive(object sender, SocketAsyncEventArgs receiveEvent)
        {
            TaskCompletionSource<IMessage> tcs = receiveEvent.UserToken as TaskCompletionSource<IMessage>;
            try
            {
                if (receiveEvent.SocketError != SocketError.Success //error condition
                    || receiveEvent.BytesTransferred <= 0) //remote side closed the connection
                {
                    try 
                    { 
                        tcs.TrySetResult(null); 
                        CloseClientSocket(tcs.Task.AsyncState as ConnectedClient); 
                    }
                    finally { RecycleEventArgs(receiveEvent); }
                }
                else
                {
                    if (receiveEvent.BytesTransferred < receiveEvent.Count)
                    {
                        try
                        {
                            receiveEvent.SetBuffer(receiveEvent.Offset + receiveEvent.BytesTransferred, receiveEvent.Count - receiveEvent.BytesTransferred);
                            if (!receiveEvent.AcceptSocket.ReceiveAsync(receiveEvent))
                                OnReceiveEventHandler(null, receiveEvent);
                        }
                        catch { RecycleEventArgs(receiveEvent); throw; }
                    }
                    else
                    {
                        try { tcs.TrySetResult(Message.Deserialize(receiveEvent)); }
                        finally { RecycleEventArgs(receiveEvent); }
                    }
                }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }


        private void OnSend(object sender, SocketAsyncEventArgs sendEvent)
        {
            TaskCompletionSource<object> tcs = sendEvent.UserToken as TaskCompletionSource<object>;
            try
            {
                if (sendEvent.SocketError != SocketError.Success)
                    CloseClientSocket(tcs.Task.AsyncState as ConnectedClient);
                else
                {
                    if (sendEvent.BytesTransferred < sendEvent.Count)
                        throw new Exception("This should never happen - send only calls back once. When all data is confirmed sent or socket error");

                    tcs.TrySetResult(null);
                }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            finally
            {
                try { RecycleEventArgs(sendEvent); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }
        }

        public Task<SocketError> DisconnectAsync(ConnectedClient clientSocket)
        {
            TaskCompletionSource<SocketError> tcs = new TaskCompletionSource<SocketError>(clientSocket);
            try
            {
                SocketAsyncEventArgs disconnectEvent = EventArgsPool.TakeItem(tcs);
                try
                {
                    disconnectEvent.AcceptSocket = clientSocket.Socket;
                    // REMARK: This is for high-velocity testing scenarios where machine may run out of
                    //         available sockets due to TIME_WAIT
                    //disconnectEvent.DisconnectReuseSocket = true;

                    disconnectEvent.Completed += OnDisconnectEventHandler;
                    if (!disconnectEvent.AcceptSocket.DisconnectAsync(disconnectEvent))
                        OnDisconnectEventHandler(null, disconnectEvent);
                }
                catch { RecycleEventArgs(disconnectEvent); throw; }
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            return tcs.Task;
        }

        public void OnDisconnect(object sender, SocketAsyncEventArgs disconnectEvent)
        {
            TaskCompletionSource<SocketError> tcs = disconnectEvent.UserToken as TaskCompletionSource<SocketError>;
            try
            {
                CloseClientSocket(tcs.Task.AsyncState as ConnectedClient);
                tcs.TrySetResult(disconnectEvent.SocketError);
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
            finally
            {
                try { RecycleEventArgs(disconnectEvent); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }
        }

        protected void CloseClientSocket(ConnectedClient client)
        {
            if (client != null)
                client.Dispose();

            if( this is SocketServer )
                Trace.WriteLine("Server Closed socket.");
            else
                Trace.WriteLine("Client Closed socket.");
        }

        protected void RecycleEventArgs(SocketAsyncEventArgs eventArgs)
        {
            try
            {
                if (OnAcceptEventHandler != null)
                    eventArgs.Completed -= OnAcceptEventHandler;
                if (OnConnectEventHandler != null)
                    eventArgs.Completed -= OnConnectEventHandler;
                if (OnDisconnectEventHandler != null)
                    eventArgs.Completed -= OnDisconnectEventHandler;

                if (OnReceiveLTFEventHandler != null)
                    eventArgs.Completed -= OnReceiveLTFEventHandler; //in case it was recycled during LTF
                if (OnReceiveEventHandler != null)
                    eventArgs.Completed -= OnReceiveEventHandler;
                if (OnSendEventHandler != null)
                    eventArgs.Completed -= OnSendEventHandler;

                //release any managed buffers to the pool
                if (eventArgs.Buffer != null && eventArgs.Buffer.Length == POOL_BUFFER_SIZE)
                    BufferPool.ReturnBuffer(eventArgs.Buffer);

                if (eventArgs.UserToken != null)
                    eventArgs.UserToken = null;

                eventArgs.AcceptSocket = null;
                eventArgs.SetBuffer(null, 0, 0);
                eventArgs.BufferList = null;

            }
            finally { EventArgsPool.ReturnItem(eventArgs); }
        }

        public void Dispose()
        {
            BufferPool.Clear();
            EventArgsPool.Dispose();
        }
    }
}
