using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FHSocket;
using FHSocket.Buffer;
using FHSocket.TCPInteface;
namespace ServerDemo
{
    public class MassageHandle : IMassageHandle
    {

        ISocketResult IMassageHandle.Handle(SocketPackage package, ClientOption option)
        {
            NormalResult result = "";
            return result;
        }
    }
}
