using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket
{
    public class ClientOption
    {
        /// <summary>
        /// 客户端IPEndPoint
        /// </summary>
        public IPEndPoint EndPoint { get; set; }
        /// <summary>
        /// 用户自定义的信息
        /// </summary>
        public object UserToken { get; set; }
    }
}
