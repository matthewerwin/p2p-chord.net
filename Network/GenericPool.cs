using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    class GenericPool<T> where T : new()
    {
        private Stack<T> m_pool;
        public GenericPool(int capacity)
        {
            m_pool = new Stack<T>();
        }

        public void ReturnItem(T item)
        {
            lock (m_pool)
                m_pool.Push(item);
        }

        public T TakeItem()
        {
            lock (m_pool)
            {
                if (m_pool.Count == 0)
                    return new T();
                return m_pool.Pop();
            }
        }

        public int Count
        {
            get { return m_pool.Count; }
        }

    }
}
