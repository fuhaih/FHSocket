using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using FHSocket.TCP;
using System.Net;
namespace ServerDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            string host = "127.0.0.1";
            int port = 6000;
            IPAddress ip = IPAddress.Parse(host);
            IPEndPoint ipe = new IPEndPoint(ip, port);

            SocketServer server = new SocketServerBuilder()
                .WithMassageHandle<MassageHandle>()
                .WithHost("0.0.0.0")
                .SetMaxConnections(1000)
                .SetReceiveBufferSize(32 * 1024)
                .SetPort(6606)
                .Build();
            server.StartListen();
            while (true)
            {
                Thread.Sleep(100);
                //Application.DoEvents();
            }
        }
    }
}
