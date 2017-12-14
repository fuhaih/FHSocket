using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
namespace FHSocket.Package
{
    /// <summary>
    /// 作用是用来缓存SocketAsyncEventArgs，不用每次都新建SocketAsyncEventArgs对象
    /// 用生产者-消费者模式
    /// </summary>
    public class SocketAsyncEventArgsPool
    {
        private object poolLock = new object();

        private Stack<SocketAsyncEventArgs> Pool;
        public SocketAsyncEventArgsPool(int numConnections)
        {
            //初始化栈的空间分配
            Pool = new Stack<SocketAsyncEventArgs>(numConnections);
        }

        public void Push(SocketAsyncEventArgs e)
        {
            lock (poolLock)
            {
                Pool.Push(e);
            }          
        }

        public SocketAsyncEventArgs Pop()
        {
            lock (poolLock)
            {
                return Pool.Pop();
            }
        }
    }
}
