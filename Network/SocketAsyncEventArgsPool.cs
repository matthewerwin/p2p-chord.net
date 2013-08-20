using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Chordian
{
    public class SocketAsyncEventArgsPool : IDisposable
    {
        Stack<SocketAsyncEventArgs> eventArgsPool;
        public SocketAsyncEventArgsPool(int capacity)
        {
            eventArgsPool = new Stack<SocketAsyncEventArgs>();
        }

        public int TotalCreated = 0;
        public int TotalReturned = 0;
        public int TotalOutstanding = 0;

        public void ReturnItem(SocketAsyncEventArgs item)
        {
            if (item == null)
                throw new ArgumentNullException("Items added to a SocketAsyncEventArgsPool cannot be null");
            if (item.AcceptSocket != null)
                throw new ArgumentException("AcceptSocket is not null on return to pool");
            if (item.Buffer != null)
                throw new ArgumentException("Buffer is not null on return to pool.");
            if (item.UserToken != null)
                throw new ArgumentException("UserToken is not null on return to pool");


            lock (this)
            {
                if (eventArgsPool == null)
                    item.Dispose();
                else
                {
                    Interlocked.Decrement(ref TotalOutstanding);
                    TotalReturned++;
                    eventArgsPool.Push(item);
                }
            }
        }

        public SocketAsyncEventArgs TakeItem(object userToken)
        {
            lock (this)
            {
                SocketAsyncEventArgs eventArgs;
                if (eventArgsPool == null || eventArgsPool.Count == 0)
                {
                    eventArgs = new SocketAsyncEventArgs();
                    TotalCreated++;
                }
                else
                    eventArgs = eventArgsPool.Pop();

                Interlocked.Increment(ref TotalOutstanding);
                eventArgs.UserToken = userToken;
                return eventArgs;
            }
        }

        public int Count
        {
            get 
            { 
                lock(this) 
                    return eventArgsPool != null ? eventArgsPool.Count : 0; 
            }
        }

        public void Clear()
        {
            lock (this)
            {
                if (eventArgsPool == null) return;

                foreach (var item in eventArgsPool)
                    item.Dispose();
                eventArgsPool.Clear();
            }
        }

        public void Dispose()
        {
            lock(this)
            {
                Clear();
                eventArgsPool = null;
            }
        }
    }
}
