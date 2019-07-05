using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket.TCP
{
    public class AsyncUserToken
    {
        public Socket Socket { get; set; }
        /// <summary>
        /// 用来标记SocketAsyncEventArgs,
        /// 不使用hashcode，因为可能会重复，虽然概率小
        /// </summary>
        public int Token { get; set; }
    }
}
