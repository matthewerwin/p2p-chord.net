using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace Chordian
{
    public abstract class ChordLocalPeer : ChordPeerAddress, IDisposable
    {
        //key-size determines maximum size of network (2^64 is a world-encompassing 18 quintillion)
        public const int KEY_SIZE = 64;
        public const int MAINTENANCE_INTERVAL = 150;

        public ChordPeerAddress Local { get; private set; } //for simplicity
        public ChordPeerAddress Successor 
        { 
            get { return FingerTable[0] ?? Local; }
            set { FingerTable[0] = value; } 
        }
        public ChordPeerAddress Predecessor 
        { 
            get { return FingerTable[FingerTable.Length-1] ?? Local; }
            set { FingerTable[FingerTable.Length - 1] = value; } 
        }
        private ChordPeerAddress[] FingerTable { get; set; }

        private Timer MaintenanceTimer { get; set; }
        protected ChordLocalPeer(string ip, int port) : base(ip, port)
        {
            Local = new ChordPeerAddress(ip, port);
            FingerTable = new ChordPeerAddress[KEY_SIZE];
            for (int i = 0; i < FingerTable.Length; i++)
                FingerTable[i] = Local;

            MaintenanceTimer = new Timer(MAINTENANCE_INTERVAL);
            MaintenanceTimer.Elapsed += RunMaintenance;
            MaintenanceTimer.Start();
        }

        protected async Task<Message> MessageChordHandler(MessageChord message)
        {
            if (message is MessageChordFindSuccessor)
            {
                ChordPeerAddress successor = await FindSuccessor(message as MessageChordFindSuccessor);
                return new MessageChordFindSuccessorReply(successor);
            }
            else if (message is MessageChordFindSuccessorReply) 
            {
                MessageChordFindSuccessorReply msg = message as MessageChordFindSuccessorReply;
                Successor = msg.Successor;
            }
            else if (message is MessageChordGetPredecessor) //effectively stabilize()
            {
                MessageChordGetPredecessor msg = message as MessageChordGetPredecessor;
                return new MessageChordGetPredecessorReply(Predecessor ?? Local);
            }
            else if (message is MessageChordNotify) //effectively notify()
            {
                MessageChordNotify msg = message as MessageChordNotify;
                if (Predecessor == Local || Utilities.IsKeyInRange(msg.Peer.Key, Predecessor.Key, Local.Key))
                    Predecessor = msg.Peer;
            }
            else if (message is MessageChordPing) //effectively check_predecessor()
            {
                MessageChordPing msg = message as MessageChordPing;
                return new MessageChordPingReply();
            }

            return null;
        }

        protected async Task<ChordPeerAddress> FindSuccessor(MessageChordFindSuccessor message)
        {

            if (Utilities.IsKeyInRange(message.Key, Local.Key, Successor.Key))
                return Successor;
            else
            {
                ChordPeerAddress successor = FindClosestPrecedingFinger(message);
                if (successor != Local) //forward if necessary
                {
                    using (PeerClient peerClient = new PeerClient(successor.IP, successor.Port)) //open a connection to responsible party
                    {
                        Trace.WriteLine("Client start...FindSuccessor");
                        await peerClient.ConnectAsync(message);
                        MessageChordFindSuccessorReply reply = await peerClient.ReceiveAsync() as MessageChordFindSuccessorReply;
                        await peerClient.DisconnectAsync(); //cleanly disconnect
                        Trace.WriteLine("Client done...FindSuccessor");
                        return reply.Successor;
                    }
                }
            }
            return Local;
        }

        private ChordPeerAddress FindClosestPrecedingFinger(MessageChordFindSuccessor message)
        {
            foreach (ChordPeerAddress successor in FingerTable.Where(x => x != Local).Reverse())
            {
                if (Utilities.IsFingerInRange(successor.Key, Local.Key, message.Key))
                    return successor;
            }
            return Local;
        }

        private bool MaintenanceInProgress = false;
        /// <summary>
        /// Maintenance routines kicked off at regular intervals triggered by a timer thread
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RunMaintenance(object sender, ElapsedEventArgs e)
        {
            try
            {
                lock (this)
                {
                    if (MaintenanceInProgress == true)
                    {
                        Trace.WriteLine("Maintenance is already in progress.");
                        return;
                    }
                    else MaintenanceInProgress = true;
                }
                Trace.WriteLine(DateTime.Now.ToShortTimeString() + ": Starting Maintenance...");
                Task stabilize = Stabilize();
                Task fixFingers = FixFingers();
                Task checkPredecessor = CheckPredecessor();
                Task.WhenAll(stabilize, fixFingers).ContinueWith(x => MaintenanceInProgress = false);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// TO DO: Figure out why the connections are not shutting down the sockets properly -- we are leaking sockets with every
        ///        single maintainence iteration.
        /// </summary>
        /// <returns></returns>

        private async Task Stabilize()
        {
            try
            {
                using (PeerClient peerClient = new PeerClient(Successor.IP, Successor.Port)) //open a connection to our Successor
                {
                    //Trace.WriteLine("Client start...Stabilize");
                    //x = successor.predecessor
                    await peerClient.ConnectAsync(new MessageChordGetPredecessor(this.Key));
                    MessageChordGetPredecessorReply reply = await peerClient.ReceiveAsync() as MessageChordGetPredecessorReply;

                    //if( x in range(n,successor) )
                    if (Utilities.IsKeyInRange(reply.Predecessor.Key, Local.Key, Successor.Key)) //closer?
                        Successor = reply.Predecessor; //update our successor to the closer peer

                    //successor.notify(n)
                    await peerClient.SendAsync(new MessageChordNotify(Local)); 
                    await peerClient.DisconnectAsync(); //cleanly disconnect
                    Trace.WriteLine("Client end...Stabilize");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message); 
            }
        }

        private int _fingerToVerify = 0;
        private async Task FixFingers()
        {
            try
            {
                //finger[next] = find_successor(n + 2^(next-1))
                //Trace.WriteLine("Fix Finger: " + _fingerToVerify);
                UInt64 targetKey = this.Key + (UInt64)Math.Pow(2, _fingerToVerify);
                await FindSuccessor(new MessageChordFindSuccessor(targetKey));
            }
            catch (Exception ex) 
            {
                Trace.WriteLine(ex.Message);
            }
            finally { _fingerToVerify = (_fingerToVerify + 1) % FingerTable.Length; }
        }

        private async Task CheckPredecessor()
        {
            try
            {
                using (PeerClient peerClient = new PeerClient(Predecessor.IP, Predecessor.Port)) //open a connection to our Predecessor
                {
                    //Trace.WriteLine("Client start...CheckPredecessor");
                    await peerClient.ConnectAsync(new MessageChordPing());
                    MessageChordPingReply reply = await peerClient.ReceiveAsync() as MessageChordPingReply;
                    await peerClient.DisconnectAsync();
                    //Trace.WriteLine("Client end...CheckPredecessor");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.Message);
            }
        }

        public void Dispose()
        {
            using (MaintenanceTimer) { MaintenanceTimer.Stop(); }
            MaintenanceTimer = null;
        }
    }
}
