using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;

namespace FHSocket.TCP
{
    public class SocketAsyncEventArgsPool
    {
        ConcurrentStack<SocketAsyncEventArgs> Pool = new ConcurrentStack<SocketAsyncEventArgs>();
        SemaphoreSlim maxlength;
        public SocketAsyncEventArgsPool(int numConnections)
        {
            maxlength = new SemaphoreSlim(numConnections,numConnections);
        }

        public void Push(SocketAsyncEventArgs e)
        {
            bool add= maxlength.Wait(10000);
            if (!add) throw new Exception("队列池已满");
            Pool.Push(e);
        }

        public SocketAsyncEventArgs Pop()
        {
            SocketAsyncEventArgs result = null;
            if (Pool.TryPop(out result))
            {
                maxlength.Release();
                return result;
            }
            else {
                throw new Exception("队列为空");
            }
        }
    }
}
