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
        /// 接收数据缓存,只存入头文件信息
        /// </summary>
        private byte[] receivedBuffer = new byte[256];

        /// <summary>
        /// 接收数据长度
        /// </summary>
        private int receivedLength = 0;
        //设置一个writer

        SocketPackage package = null;

        IMassageHandle msgHandle;

        ClientOption option;

        /// <summary>
        /// 发送数据缓存
        /// </summary>
        private byte[] sendBuffer = new byte[256];

        /// <summary>
        /// 当前已发送位置
        /// </summary>
        private int currentIndex = 0;

        /// <summary>
        /// 发送数据长度
        /// </summary>
        private int sendlength = 0;

        public SocketBuffer(IMassageHandle msghandle,ClientOption option)
        {
            this.msgHandle = msghandle;
            this.option = option;
        }

        public void Add(IEnumerable<byte> data)
        {
            receivedBuffer = receivedBuffer.Concat(data).ToArray();
            SplitPackage();
        }

        public bool Read(SocketAsyncEventArgs e)
        {
            while (receivedLength + e.BytesTransferred > receivedBuffer.Length)
            {
                GrowReceivedBuffer();
            }
            Array.Copy(e.Buffer, e.Offset, receivedBuffer, receivedLength, e.BytesTransferred);
            receivedLength += e.BytesTransferred;
            return SplitPackage();
        }

        public bool Write(SocketAsyncEventArgs e)
        {
            if (sendlength == 0) return false;
            if (currentIndex >= sendlength)
            {
                sendBuffer = new byte[0];
                return false;
            }
            int postlenth = 0;
            if (sendlength - currentIndex > e.Count)
            {
                postlenth = e.Count;
            }
            else {
                postlenth = sendlength - currentIndex;
            }
            Array.Copy(sendBuffer, currentIndex, e.Buffer, e.Offset, postlenth);
            e.SetBuffer(e.Offset, postlenth);
            currentIndex = currentIndex + postlenth;
            if (sendlength == currentIndex)
            {
                sendlength = 0;
                currentIndex = 0;
            }
            return true;
        }

        public bool SplitPackage()
        {
            if (package == null)
            {
                package = PackageManager.Create(ref receivedBuffer);
            }
            if (package != null)
            {
                bool writecompleted = package.Write(ref receivedBuffer);
                if (writecompleted)
                {
                    ISocketResult result = msgHandle?.Handle(package, option);
                    AddSendBuffer(result);
                    package = null;
                    SplitPackage();
                    return true;
                }
                return false;
            }
            else {
                return false;
            }
        }

        private void AddSendBuffer(ISocketResult result)
        {
            byte[] data = result.GetResultData();
            while (sendlength + data.Length > sendBuffer.Length)
            {
                GrowSendBuffer();
            }
            Array.Copy(data, 0, sendBuffer, sendlength, data.Length);
            sendlength += data.Length;
        }

        /// <summary>
        /// 缓存扩容
        /// </summary>
        private void GrowSendBuffer()
        {
            int length = sendBuffer.Length;
            int newlength = length > (20 * 1024 * 1024) ? (int)(length * 1.5) : length * 2;
            byte[] newbuffer = new byte[newlength];
            Array.Copy(sendBuffer, 0, newbuffer, 0, sendlength);
            sendBuffer = newbuffer;
        }

        /// <summary>
        /// 缓存扩容
        /// </summary>f
        public void GrowReceivedBuffer()
        {
            int length = receivedBuffer.Length;
            /**数据缓存的长度大于20M时，每次扩容1.5倍
             * 数据缓存的长度小于20M时，每次扩容2倍
             */
            int newlength = length > (20 * 1024 * 1024) ? (int)(length * 1.5) : length * 2;
            byte[] newbuffer = new byte[newlength];
            Array.Copy(receivedBuffer, 0, newbuffer, 0, sendlength);
            receivedBuffer = newbuffer;
        }
    }
}
