using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
namespace FHSocket.Package
{
    /// <summary>
    /// 缓存管理
    /// 管理各个socket连接的数据缓存
    /// </summary>
    public class BufferManager
    {
        Dictionary<int, SocketBuffer> Buffer = new Dictionary<int, SocketBuffer>();
        public BufferManager()
        {
            
        }

        private object dictLock = new object();

        public void SetBuffer(SocketAsyncEventArgs e,Action<PackegeData, SocketAuthorize> recieve, Action<Exception> recieveError=null)
        {
            int hashcode= e.GetHashCode();
            SocketBuffer buffer;
            lock (dictLock)
            {
                bool bl = Buffer.TryGetValue(hashcode, out buffer);
                if (!bl)
                {
                    buffer = new SocketBuffer(new byte[0]);
                    Buffer.Add(hashcode, buffer);
                }
            }
            buffer.Add(e.Buffer.Take(e.BytesTransferred));
            try
            {
                PackegeData result = buffer.Next();
                while (result != null)
                {
                    recieve?.Invoke(result, buffer.Authorize);
                    result = buffer.Next();
                }
            }
            catch (Exception ex) {
                recieveError?.Invoke(ex);
            }
            
        }

        public void DisposeBuffer(SocketAsyncEventArgs e)
        {
            int hashcode = e.GetHashCode();
            SocketBuffer buffer;
            lock (dictLock)
            {
                bool bl = Buffer.TryGetValue(hashcode, out buffer);
                if (bl)
                {
                    buffer.Clrear();
                    buffer.Cancel();
                }
            }
            
        }

        public SocketAuthorize GetAuthorize(SocketAsyncEventArgs e)
        {
            int hashcode = e.GetHashCode();
            SocketBuffer buffer;
            lock (dictLock)
            {
                bool bl = Buffer.TryGetValue(hashcode, out buffer);
                if (!bl)
                {
                    buffer = new SocketBuffer(new byte[0]);
                    Buffer.Add(hashcode, buffer);
                }
                return buffer.Authorize;
            }
        }


    }
}
