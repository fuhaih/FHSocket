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
        public void Handle(SocketPackage package, ClientOption option)
        {
            throw new NotImplementedException();
        }
    }
}
