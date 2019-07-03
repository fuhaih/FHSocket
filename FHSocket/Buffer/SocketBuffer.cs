using FHSocket.TCPInteface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket.Buffer
{

    /// <summary>
    /// 当缓存过大时，每2M存一次磁盘。
    /// </summary>
    public class SocketBuffer
    {
        /// <summary>
        /// 可以优化，扩展内存。
        /// </summary>
        private byte[] buffer=new byte[0];

        SocketPackage package = null;

        IMassageHandle msgHandle;

        ClientOption option;

        public SocketBuffer(IMassageHandle msghandle,ClientOption option)
        {
            this.msgHandle = msghandle;
            this.option = option;
        }

        public void Add(IEnumerable<byte> data)
        {
            buffer = buffer.Concat(data).ToArray();
            SplitPackage();
        }

        public void Add(SocketAsyncEventArgs e)
        {
            byte[] newbuffer = new byte[buffer.Length+ e.BytesTransferred];
            Array.Copy(buffer, 0, newbuffer, 0, buffer.Length);
            Array.Copy(e.Buffer, e.Offset, newbuffer, buffer.Length, e.BytesTransferred);
            buffer = newbuffer;
            SplitPackage();
        }

        public void SplitPackage()
        {
            if (package == null)
            {
                package = PackageManager.Create(ref buffer);
            }
            if (package != null)
            {
                bool writecompleted = package.Write(ref buffer);
                if (writecompleted)
                {
                    msgHandle?.Handle(package,option);
                    package = null;
                    SplitPackage();
                } 
            }
        }

        public void Grow()
        {

        }

    }
}
