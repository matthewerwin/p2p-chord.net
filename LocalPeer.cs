using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Chordian
{
    public class LocalPeer : ChordLocalPeer, IDisposable
    {
        private PeerServer Server { get; set; }
        private ConcurrentDictionary<string, PeerClient> Clients = new ConcurrentDictionary<string, PeerClient>();

        public LocalPeer(int port) : this(null, port) { }

        public LocalPeer(string ip, int port)
            : base(ip, port)
        {
            //we always bind to all interfaces -- IP provided is purely for remote peers
            Server = new PeerServer(null, port);

            Task serverExit = Server.ListenAsync(MessageHandler).ContinueWith(
                //cleanup lambda when server is shutting down or has an Accept exception
                    listenTask =>
                    {
                        if (listenTask.IsFaulted)
                            Console.WriteLine(listenTask.Exception.Message);
                    }
                );
        }

        /// <summary>
        /// Join consists of initial FindSuccessor and FindSuccessorReply
        /// </summary>
        /// <param name="seed"></param>
        /// <returns></returns>
        public async Task Join( ChordLocalPeer seed = null ) 
        {
            if (seed == null)
                throw new ArgumentNullException("seed");

            using (PeerClient peerClient = new PeerClient(seed.IP, seed.Port)) //open a connection to the seed
            {
                Console.WriteLine("Client start...Join");
                await peerClient.ConnectAsync(new MessageChordFindSuccessor(this.Key)); //find successor
                MessageChordFindSuccessorReply reply = await peerClient.ReceiveAsync() as MessageChordFindSuccessorReply;
                Successor = reply.Successor; //update successor
                await peerClient.DisconnectAsync(); //cleanly disconnect
                Console.WriteLine("Client end...Join");
            }
        }

        private async Task MessageHandler(ConnectedClient clientSocket, Message message)
        {
            Message responseMessage = null;
            if (message is MessageChord)
                responseMessage = await MessageChordHandler(message as MessageChord);

            if (responseMessage != null)
            {
                Task sendMessage = Server.SendMessageAsync(clientSocket, responseMessage);
            }
            // Server isn't responsible for shutting down connections -- assume the client
            // knows when to close the connection (i.e. when no more data is being received/sent)
            else
            {
                Task disconnectTask = Server.DisconnectAsync(clientSocket);
            }
        }



        public new void Dispose()
        {
            base.Dispose();
            if (Server != null)
            {
                Server.Dispose();
                Server = null;
            }
        }
    }
}
