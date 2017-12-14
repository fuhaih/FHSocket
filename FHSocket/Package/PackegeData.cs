using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FHSocket.Package
{
    public class PackegeData
    {
        public byte[] Head { get; set; }
        public byte Type { get; set; }
        public int Length { get; set; }
        public byte[] Data { get; set; }

        /// <summary>
        /// 打包数据
        /// </summary>
        /// <returns></returns>
        public byte[] Packege()
        {
            if (Data == null || Data.Length == 0)
            {
                throw new NullReferenceException("包数据Data不能为空");
            }
            if (Head == null || Head.Length != 2)
            {
                throw new NullReferenceException("数据报头信息需为两位字节数组不能为空");
            }
            byte[] result = new byte[7];
            result[0] = Head[0];
            result[1] = Head[1];
            result[2] = Type;
            byte[] lenBytes = BitConverter.GetBytes(Length);
            lenBytes = lenBytes.Reverse().ToArray();
            result[3] = lenBytes[0];
            result[4] = lenBytes[1];
            result[5] = lenBytes[2];
            result[6] = lenBytes[3];
            result = result.Concat(Data).ToArray();
            return result;

        }
    }
}
