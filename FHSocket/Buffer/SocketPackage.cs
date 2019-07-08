using FHSocket.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket.Buffer
{
    /**定义数据结构
     * ------------------------------------------------------------------------------------
     * |  （0x1f）  |  datatype  |  0 headlength head  | ........ |  1 datalenght data  |                   
     * ------------------------------------------------------------------------------------
     *   1-2字节分隔符  1字节两位数据类型
     *   
     *   head : 头信息,1位标志位(0) 15位长度 共2字节  head信息
     *   data : 数据,1位标志位(1) 63位长度 8字节 data数据 
     *   data这样设计是为了能扩展更大的数据。
     */

    public class PackageManager
    {
        public static SocketPackage Create(ref byte[] buffer)
        {
            if (buffer.Length == 0) return null;
            if (buffer[0] != 0x1f || buffer[1] != 0x1f)
            {
                throw new PackageException("报文头异常");
            }
            SocketDataType type = (SocketDataType)buffer[2];
            SocketPackage package;
            switch (type)
            {
                case SocketDataType.File:package = new FileSocketPackage();break;
                default:package = new NormalSocketPackage();break;
            }
            return package.FillHead(ref buffer) ? package : null;
        }

        public static byte[] GetNormalSocketPackageHead(long length)
        {
            byte[] result = new byte[11];
            result[0] = 0x1f;
            result[1] = 0x1f;
            result[2] = (byte)SocketDataType.NormalData;
            byte[] lengthbytes = BitConverter.GetBytes(length);
            Array.Copy(lengthbytes, 0, result, 3, lengthbytes.Length);
            return result;
        }

        public static byte[] GetFileSocketPackageHead(long length, string filename)
        {
            byte[] result = new byte[15];
            result[0] = 0x1f;
            result[1] = 0x1f;
            result[2] = (byte)SocketDataType.File;
            byte[] lengthbytes = BitConverter.GetBytes(length);
            Array.Copy(lengthbytes, 0, result, 3, lengthbytes.Length);
            byte[] fbyte = Encoding.Default.GetBytes(filename);
            byte[] flengbyte = BitConverter.GetBytes(fbyte.Length);
            Array.Copy(flengbyte, 0, result, 11, flengbyte.Length);
            result = result.Concat(fbyte).ToArray();
            return result;
        }
    }

    public abstract class SocketPackage
    {
        /// <summary>
        /// 分割代码，用来处理粘包问题。
        /// </summary>
        public static byte[] SegmentCode = new byte[] { 0x1f, 0x1f };

        /// <summary>
        /// 数据类型
        /// </summary>
        public SocketDataType Type { get; set;  }

        /// <summary>
        /// 数据长度
        /// </summary>
        public long Length { get; set; }

        public SocketPackage()
        {

        }

        public abstract bool FillHead(ref byte[] buffer);

        public abstract bool Write(ref byte[] buffer);

    }

    /// <summary>
    /// |2(boundary)|1(type)|8(datalength)|data|
    /// </summary>
    public class NormalSocketPackage : SocketPackage
    {
        public byte[] Data = new byte[0];
        public override bool FillHead(ref byte[] buffer)
        {
            if (buffer.Length < 11) return false;
            Type = (SocketDataType)buffer[2];
            byte[] lengthbytes = new byte[8];
            Array.Copy(buffer, 3, lengthbytes, 0, lengthbytes.Length);
            Length = BitConverter.ToInt64(lengthbytes, 0);
            buffer = buffer.Skip(11).ToArray();
            return true;
        }

        public override bool Write(ref byte[] buffer)
        {
            return true;
        }
    }

    /// <summary>
    /// |2(boundary)|1(type)|8(datalength)|4(filenamelength)|filename|data|
    /// </summary>
    public class FileSocketPackage : SocketPackage
    {

        public string FileName { get; set; }

        public string LocalPath { get; set; }

        private long size = 0;

        public override bool FillHead(ref byte[] buffer)
        {
            if (buffer.Length < 15) return false;
            byte[] flengthbyte = new byte[4];
            Array.Copy(buffer, 11, flengthbyte, 0, flengthbyte.Length);
            int flength = BitConverter.ToInt32(flengthbyte,0);
            if (buffer.Length < 15 + flength) return false;

            Type = (SocketDataType)buffer[2];
            byte[] lengthbytes = new byte[8];
            Array.Copy(buffer, 3, lengthbytes, 0, lengthbytes.Length);
            Length = BitConverter.ToInt64(lengthbytes, 0);

            byte[] fbyte = new byte[flength];
            Array.Copy(buffer, 15, fbyte, 0, fbyte.Length);
            FileName = Encoding.Default.GetString(fbyte);

            string templepath = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "temple");
            if (!Directory.Exists(templepath))
            {
                Directory.CreateDirectory(templepath);
            }
            LocalPath = Path.Combine(templepath, Guid.NewGuid().ToString());
            buffer = buffer.Skip(15 + flength).ToArray();
            return true;
        }

        public override bool Write(ref byte[] buffer)
        {
            if (buffer.Length >= 2 * 1024 * 1024 || buffer.Length + size >= this.Length)
            {
                int write = (int)Math.Min(buffer.Length, this.Length - size);
                size = size + write;
                using (FileStream stream = new FileStream(LocalPath, FileMode.OpenOrCreate))
                {
                    stream.Seek(0, SeekOrigin.End);
                    stream.Write(buffer, 0, write);
                }
                buffer = buffer.Skip(write).ToArray();
                if (size==this.Length)
                {
                    return true;
                }
            }
            return false;
        }

    }
}
