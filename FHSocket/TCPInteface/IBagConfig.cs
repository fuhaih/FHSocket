using FHSocket.TCPInteface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket.TCPInteface
{
    public interface IBagConfig
    {
        IMassageHandle MsgHandle { get; }
    }
}
