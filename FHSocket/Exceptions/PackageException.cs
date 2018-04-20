using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHSocket.Exceptions
{
    public class PackageException : Exception
    {
        public PackageException(string msg) : base(msg)
        {

        }
    }
}
