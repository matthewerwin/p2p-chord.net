using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    public class SocketAsyncEventArgsPool
    {
        Stack<SocketAsyncEventArgs> eventArgsPool;
        public SocketAsyncEventArgsPool(int capacity)
        {
            eventArgsPool = new Stack<SocketAsyncEventArgs>();
        }

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

            lock (eventArgsPool)
                eventArgsPool.Push(item);
        }

        public SocketAsyncEventArgs TakeItem(object userToken)
        {
            lock (eventArgsPool)
            {
                SocketAsyncEventArgs eventArgs;
                if (eventArgsPool.Count == 0)
                    eventArgs = new SocketAsyncEventArgs();
                else
                    eventArgs = eventArgsPool.Pop();

                eventArgs.UserToken = userToken;
                return eventArgs;
            }
        }

        public int Count
        {
            get 
            { 
                lock(eventArgsPool) 
                    return eventArgsPool.Count; 
            }
        }

        public void Clear()
        {
            lock (eventArgsPool)
                eventArgsPool.Clear();
        }
    }
}
