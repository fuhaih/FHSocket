using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;
using FHSocket.Buffer;
using FHSocket.TCPInteface;

namespace FHSocket.TCP
{
    /// <summary>
    /// 粘包处理
    /// </summary>
    public class SocketManager
    {
        ConcurrentDictionary<int, SocketBuffer> SocketBuffers = new ConcurrentDictionary<int, SocketBuffer>();

        private IBagConfig Config { get; set; }

        public SocketManager(IBagConfig config)
        {
            this.Config = config;
        }
          
        /// <summary>
        /// 读取消息，如果读取到一个完整的消息，返回true，没有获取完整消息，返回false
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public bool Read(SocketAsyncEventArgs e)
        {
            int token = ((AsyncUserToken)e.UserToken).Token;
            SocketBuffer buffer = SocketBuffers.GetOrAdd(token, t =>
            {
                return new SocketBuffer(Config.MsgHandle,new ClientOption { EndPoint=(IPEndPoint)e.AcceptSocket.RemoteEndPoint});
            });
            return buffer.Read(e);
        }

        /// <summary>
        /// 把响应数据写入到SocketAsyncEventArgs中
        /// 写入成功返回true，否则返回false，表示没有数据返回到客户端
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public bool Write(SocketAsyncEventArgs e)
        {
            int token = ((AsyncUserToken)e.UserToken).Token;
            SocketBuffer buffer = SocketBuffers.GetOrAdd(token, t =>
            {
                return new SocketBuffer(Config.MsgHandle, new ClientOption { EndPoint = (IPEndPoint)e.AcceptSocket.RemoteEndPoint });
            });
            return buffer.Write(e);
        }

        /// <summary>
        /// 移除
        /// </summary>
        /// <param name="e"></param>
        public void Clean(SocketAsyncEventArgs e)
        {
            SocketBuffer buffer;
            int token = ((AsyncUserToken)e.UserToken).Token;
            SocketBuffers.TryRemove(token, out buffer);
        }
    }
}
