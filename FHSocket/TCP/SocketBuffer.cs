using FHSocket.TCPInteface;
using System;
using System.Collections.Generic;
using System.Linq;
namespace FHSocket.TCP
{
    /// <summary>
    /// 用来存储socket获取到的报文数据,还用来标记socket连接是否合法
    /// </summary>
    public class SocketBuffer
    {
        private byte[] Head = new byte[] { 0x1f, 0x1f };
        public object BufferLock = new object();

        private byte[] Buffer=new byte[0];

        /// <summary>
        /// socket授权信息
        /// </summary>
        public SocketAuthorize Authorize = new SocketAuthorize();

        public SocketBuffer()
        {
            Buffer = new byte[0];
        }

        public void Add(IEnumerable<byte> buffer)
        {
            lock (BufferLock)
            {
                Buffer = Buffer.Concat(buffer).ToArray();
            }
        }

        public void Clrear()
        {
            lock (BufferLock)
            {
                Buffer=new byte[0];
            }

        }

        //public PackegeData Next()
        //{
        //    lock (BufferLock)
        //    {
        //        if (Buffer.Length == 0) return null;
        //        byte[] head = new byte[] {Buffer[0],Buffer[1]};
        //        if (head[0]!=this.Head[0]||head[1]!=this.Head[1] )
        //        {
        //            Buffer= new byte[0];
        //            throw new PackageException("报文头异常");
        //        }
        //        byte type = Buffer[2];
        //        byte[] len = new byte[] { Buffer[3], Buffer[4], Buffer[5], Buffer[6] };
        //        //buffer超过一定数量的时候清空
        //        byte[] lenBytes = len.Reverse().ToArray();
        //        int length = BitConverter.ToInt32(lenBytes,0);
        //        if (Buffer.Length >= length + 7)
        //        {
        //            byte[] package = Buffer.Take(length + 7).ToArray();
        //            byte[] data = package.Skip(7).ToArray();
        //            Buffer = Buffer.Skip(length + 7).ToArray();
        //            PackegeData result = new PackegeData()
        //            {
        //                Head = new byte[] { 31, 31 },
        //                Length = length,
        //                Type = type,
        //                Data = data
        //            };
        //            return result;
        //        }
        //        else {
        //            return null;
        //        } 
        //    }
        //}

        public string NextByReg<T>()  where T : class,ISocketBuffer
        {
            T result= Activator.CreateInstance(typeof(T)) as T;
            return null;
        }

        public void Cancel()
        {
            Authorize.Cancel();
        }
    }
}
