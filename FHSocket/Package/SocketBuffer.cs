using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
namespace FHSocket.Package
{
    /// <summary>
    /// 用来存储socket获取到的报文数据,还用来标记socket连接是否合法
    /// </summary>
    public class SocketBuffer
    {

        private byte[] Head = new byte[] { 0x1f, 0x1f };
        public object BufferLock = new object();

        public byte[] Buffer=new byte[0];
        /// <summary>
        /// socket授权信息
        /// </summary>
        public SocketAuthorize Authorize = new SocketAuthorize();


        public SocketBuffer(IEnumerable<byte> buffer)
        {
            Buffer = buffer.ToArray();
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

        public PackegeData Next()
        {
            lock (BufferLock)
            {
                if (Buffer.Length == 0) return null;
                byte[] head = new byte[] {Buffer[0],Buffer[1]};
                if (head[0]!=this.Head[0]||head[1]!=this.Head[1] )
                {
                    Buffer= new byte[0];
                    throw new Exception("报文头异常");
                }
                byte type = Buffer[2];
                byte[] len = new byte[] { Buffer[3], Buffer[4], Buffer[5], Buffer[6] };
                //buffer超过一定数量的时候清空
                byte[] lenBytes = len.Reverse().ToArray();
                int length = BitConverter.ToInt32(lenBytes,0);
                if (Buffer.Length >= length + 7)
                {
                    byte[] package = Buffer.Take(length + 7).ToArray();
                    byte[] data = package.Skip(7).ToArray();
                    Buffer = Buffer.Skip(length + 7).ToArray();
                    PackegeData result = new PackegeData()
                    {
                        Head = new byte[] { 31, 31 },
                        Length = length,
                        Type = type,
                        Data = data
                    };
                    return result;
                }
                else {
                    return null;
                } 
            }
        }

        public string NextByReg()
        {
            lock (BufferLock)
            {
                //buffer超过一定数量的时候清空

                //根据正则表达式匹配完整的xml文档信息
                string data = Encoding.UTF8.GetString(Buffer);
                Regex reg = new Regex(@"(<\?xml .*?/?>)?<(root)>(.|\s)*?</\2>");
                Match match=reg.Match(data);
                if (!match.Success) return null;
                string result = match.Value;
                if (result.Length > 225)
                {

                }
                byte[] bytes = Encoding.UTF8.GetBytes(result);
                Buffer = Buffer.Skip(bytes.Length).ToArray();
                return result;
            }
        }

        public void Cancel()
        {
            Authorize.Cancel();
        }
    }
}
