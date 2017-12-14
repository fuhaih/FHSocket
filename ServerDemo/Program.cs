using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using FHSocket.Server;
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

            FHSocketServer server = new FHSocketServer(1000);
            server.Init();
            server.Start(ipe);
            while (true)
            {
                Thread.Sleep(100);
                //Application.DoEvents();
            }
        }
    }
}
