using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TCPServer.TCP;

namespace FHSocket.TCPInteface
{
    /// <summary>
    /// 数据缓存对象，每个连接会生成一个数据缓存对象来缓存对应的数据。
    /// </summary>
    public interface ISocketBuffer
    {
        UserAgent UserInfo { get; set; }
        /// <summary>
        /// 缓存数据
        /// </summary>
        /// <param name="buffer"></param>
        void Cache(IEnumerable<byte> buffer);
        /// <summary>
        /// 从缓存数据中，根据规则获取完整的tcp数据
        /// </summary>
        /// <returns></returns>
        byte[] Next();
        /// <summary>
        /// 清空缓存和其他数据
        /// </summary>
        void Clear();
    }
}
