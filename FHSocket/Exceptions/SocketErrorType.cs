using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket.Exceptions
{
    public enum SocketErrorType
    {
        AcceptError,
        OperationError,
        CloseError,
        FullPoolError
    }
}
