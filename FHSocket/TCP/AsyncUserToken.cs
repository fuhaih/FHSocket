﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
namespace FHSocket.TCP
{
    public class AsyncUserToken
    {
        public DateTime LastActiveTime { get; internal set; }
        public Socket Socket { get; set; }
    }
}
