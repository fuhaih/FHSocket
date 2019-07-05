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
    }

    public class NormalResult : ISocketResult
    {
        public byte[] GetResultData()
        {
            throw new NotImplementedException();
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
