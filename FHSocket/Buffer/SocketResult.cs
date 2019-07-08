using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket.Buffer
{

    public interface ISocketResult
    {
        byte[] GetResultData();
    }

    public class FileResult : ISocketResult
    {
        public byte[] GetResultData()
        {
            throw new NotImplementedException();
        }

        public FileResult(string filepath)
        {

        }

        public static implicit operator FileResult(string value)
        {
            return new FileResult(value);
        }
    }

    public class NormalResult : ISocketResult
    {
        public byte[] GetResultData()
        {
            throw new NotImplementedException();
        }

        public NormalResult(string value)
        {

        }

        public NormalResult(byte[] value)
        {

        }

        public static implicit operator NormalResult(string value)
        {
            return new NormalResult(value);
        }

        public static implicit operator NormalResult(byte[] value)
        {
            return new NormalResult(value);
        }


    }

    public class NonResult : ISocketResult
    {
        public byte[] GetResultData()
        {
            throw new NotImplementedException();
        }
    }
}
