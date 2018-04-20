using FHSocket.TCPInteface;
using System.Collections.Generic;
using System.Linq;
namespace FHSocket.TCP
{
    class DefaultSocketBuffer:ISocketBuffer
    {
        public object BufferLock = new object();
        public UserAgent UserInfo { get; set; }
        private byte[] Buffer = new byte[0];
        public DefaultSocketBuffer()
        {
            Buffer = new byte[0];
        }
        public void Cache(IEnumerable<byte> buffer)
        {
            lock (BufferLock)
            {
                Buffer = Buffer.Concat(buffer).ToArray();
            }
        }

        public byte[] Next()
        {
            lock (BufferLock)
            {
                if (Buffer.Length > 0)
                {
                    byte[] result = Buffer.Take(Buffer.Length).ToArray();
                    return result;
                }
                else {
                    return null;
                }
            }

        }

        public void Clear()
        {
            lock (BufferLock)
            {
                Buffer = new byte[0];
            }
        }

    }
}
