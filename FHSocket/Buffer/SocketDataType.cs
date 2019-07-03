using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket.Buffer
{
    public enum SocketDataType
    {
        File = 0x1,
        Heart = 0x2,
        Auth = 0x3,
        NormalData = 0x4
    }
}
