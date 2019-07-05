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
        //设置一个writer

        SocketPackage package = null;

        IMassageHandle msgHandle;

        ClientOption option;

        private byte[] sendBuffer = new byte[0];

        private int currentIndex = 0;

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

        public bool Read(SocketAsyncEventArgs e)
        {
            byte[] newbuffer = new byte[buffer.Length+ e.BytesTransferred];
            Array.Copy(buffer, 0, newbuffer, 0, buffer.Length);
            Array.Copy(e.Buffer, e.Offset, newbuffer, buffer.Length, e.BytesTransferred);
            buffer = newbuffer;
            return SplitPackage();
        }

        public bool Write(SocketAsyncEventArgs e)
        {
            if (sendBuffer.Length == 0) return false;
            if (currentIndex >= sendBuffer.Length)
            {
                sendBuffer = new byte[0];
                return false;
            }
            int postlenth = 0;
            if (sendBuffer.Length - currentIndex > e.Count)
            {
                postlenth = e.Count;
            }
            else {
                postlenth = sendBuffer.Length - currentIndex;
            }
            Array.Copy(sendBuffer, currentIndex, e.Buffer, e.Offset, postlenth);
            e.SetBuffer(e.Offset, postlenth);
            currentIndex = currentIndex + postlenth;
            return true;
        }

        public bool SplitPackage()
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
                    ISocketResult result = msgHandle?.Handle(package, option);
                    sendBuffer = result.GetResultData();
                    currentIndex = 0;
                    package = null;
                    //SplitPackage();
                    //return true;
                }
                return false;
            }
            else {
                return false;
            }
        }

        public void Grow()
        {

        }

    }
}
