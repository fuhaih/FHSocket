using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.Collections.Concurrent;

namespace FHSocket.TCP
{
    /// <summary>
    /// 作用是用来缓存SocketAsyncEventArgs，不用每次都新建SocketAsyncEventArgs对象
    /// </summary>
    public class SocketAsyncEventArgsPool
    {
        private ConcurrentQueue<SocketAsyncEventArgs> Pool;

        private SocketManager Manager = new SocketManager();


        public SocketAsyncEventArgsPool(int numConnections)
        {
            //初始化栈的空间分配
            Pool = new ConcurrentQueue<SocketAsyncEventArgs>();
        }

        public void Push(SocketAsyncEventArgs e)
        {
            //先释放活跃socket
            Pool.Enqueue(e);

        }

        public SocketAsyncEventArgs Pop()
        {
            SocketAsyncEventArgs result = null;
            Pool.TryDequeue(out result);
            return result;
        }

        public int Count()
        {
            return Pool.Count;
        }

    }

    public class SocketManager
    {
        private int threshold = 10;
        public int Threshold
        {
            get
            {
                return threshold;
            }
            set
            {
                threshold = value;
            }
        }
        private ConcurrentDictionary<string, ActiveSocketCollection> ClientSockets = new ConcurrentDictionary<string, ActiveSocketCollection>();
        /// <summary>
        /// 移除指定ip的一个SocketAsyncEventArgs，
        /// 如果该SocketAsyncEventArgs不存在，返回false
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="e"></param>
        public bool Deregister(IPEndPoint clientEnp, IPEndPoint serverEnp, SocketAsyncEventArgs e)
        {
            if (clientEnp == null) return false;
            ActiveSocketCollection col = ClientSockets.GetOrAdd(clientEnp.Address.ToString(), (ip) =>
            {
                return new ActiveSocketCollection(Threshold);
            });
            return col.Remove(e);
        }
        /// <summary>
        /// 给指定ip注册一个SocketAsyncEventArgs，
        /// 超出阈值会返回false
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="e"></param>
        public bool Enregister(IPEndPoint clientEnp, IPEndPoint serverEnp, SocketAsyncEventArgs e, out List<SocketAsyncEventArgs> es)
        {
            es = new List<SocketAsyncEventArgs>();
            if (clientEnp == null) return false;
            ActiveSocketCollection col = ClientSockets.GetOrAdd(clientEnp.Address.ToString(), (ip) =>
            {
                return new ActiveSocketCollection(Threshold);
            });
            return col.Add(e, out es);

        }
    }

    public class ActiveSocketCollection
    {
        /// <summary>
        /// 每个客户端的阈值
        /// </summary>
        private int Threshold = 10;
        private object dic_lock = new object();
        public ActiveSocketCollection(int threshold)
        {
            this.Threshold = threshold;
        }

        private Dictionary<int, ActiveSocket> Sockets = new Dictionary<int, ActiveSocket>();
        /// <summary>
        /// 新增
        /// </summary>
        /// <returns></returns>
        public bool Add(SocketAsyncEventArgs e, out List<SocketAsyncEventArgs> es)
        {
            //, out List<SocketAsyncEventArgs> es
            es = new List<SocketAsyncEventArgs>();
            bool result = true;
            lock (dic_lock)
            {
                if (Sockets.Count >= Threshold)
                {
                    //当连接满阈值时，先销毁最新进来的连接。
                    var asocket = Sockets.OrderBy(m => m.Value.CreateTime).FirstOrDefault();
                    //foreach (var item in Sockets)
                    //{
                    //    es.Add(item.Value.Args);
                    //}
                    es.Add(asocket.Value.Args);
                    //Sockets.Clear();
                    //Sockets.Add(e.GetHashCode(), e);
                    result = false;
                }
                Sockets.Add(e.GetHashCode(), new ActiveSocket(e, DateTime.Now));
            }
            return result;
        }
        /// <summary>
        /// 移除
        /// </summary>
        /// <returns></returns>
        public bool Remove(SocketAsyncEventArgs e)
        {
            lock (dic_lock)
            {
                return Sockets.Remove(e.GetHashCode());
            }
        }
    }

    public class ActiveSocket
    {
        public SocketAsyncEventArgs Args { get; set; }
        public DateTime CreateTime { get; set; }

        public ActiveSocket(SocketAsyncEventArgs args, DateTime time)
        {
            this.Args = args;
            this.CreateTime = time;
        }
    }
}
