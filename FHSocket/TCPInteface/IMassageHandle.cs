﻿using FHSocket.Buffer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket.TCPInteface
{
    public interface IMassageHandle
    {
        ISocketResult Handle(SocketPackage package,ClientOption option);
    }
}
