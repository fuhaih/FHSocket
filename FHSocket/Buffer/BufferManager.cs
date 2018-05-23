using FHSocket.TCPInteface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;

namespace FHSocket.Buffer
{
    /// <summary>
    /// 缓存管理
    /// 管理各个socket连接的数据缓存
    /// </summary>
    public class BufferManager
    { 
        Dictionary<int, ISocketBuffer> Buffer = new Dictionary<int, ISocketBuffer>();

        private Func<ISocketBuffer> getSocketBuffer;
        public BufferManager(Func<ISocketBuffer> getSocketBuffer)
        {
            this.getSocketBuffer = getSocketBuffer==null? () => { return new DefaultSocketBuffer(); }: getSocketBuffer;
        }

        private object dictLock = new object();

        /// <summary>
        /// 缓存数据
        /// </summary>
        /// <param name="e"></param>
        /// <param name="recieve"></param>
        /// <param name="recieveError"></param>
        public void SetBuffer(SocketAsyncEventArgs e,Action<byte[],UserAgent> recieve, Action<Exception> recieveError=null)
        {
            int hashcode= e.GetHashCode();
            ISocketBuffer buffer;
            lock (dictLock)
            {
                bool bl = Buffer.TryGetValue(hashcode, out buffer);
                if (!bl)
                {
                    buffer =  getSocketBuffer();
                    Buffer.Add(hashcode, buffer);
                }
            }
            buffer.Cache(e.Buffer.Take(e.BytesTransferred));
            try
            {
                byte[] result = buffer.Next();
                while (result != null&&result.Length>0)
                {
                    recieve?.Invoke(result,buffer.UserInfo);
                    result = buffer.Next();
                }
            }
            catch (Exception ex) {
                recieveError?.Invoke(ex);
            }
            
        }
        /// <summary>
        /// 销毁缓存区
        /// </summary>
        /// <param name="e"></param>
        public void DisposeBuffer(SocketAsyncEventArgs e)
        {
            int hashcode = e.GetHashCode();
            ISocketBuffer buffer;
            lock (dictLock)
            {
                bool bl = Buffer.TryGetValue(hashcode, out buffer);
                if (bl)
                {
                    buffer.Clear();
                }
            }
            
        }
        /// <summary>
        /// 获取用户自定义数据
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public void GetUserStored(SocketAsyncEventArgs e)
        {
            int hashcode = e.GetHashCode();
            ISocketBuffer buffer;
            lock (dictLock)
            {
                bool bl = Buffer.TryGetValue(hashcode, out buffer);
            }
        }


    }
}
