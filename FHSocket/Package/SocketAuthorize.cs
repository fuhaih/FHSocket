using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
namespace FHSocket.Package
{
    /// <summary>
    /// socket授权信息
    /// </summary>
    public class SocketAuthorize
    {
        //private string key = "abcdeABCD,123456";
        private string key = "f838fabc69374401";

        private string iv = "f838fabc69374401";

        private string sequence = null;
        public string Key {
            get {
                return key;
            }
        }

        public string IV {
            get {
                return iv;
            }
        }
        public string Sequence {
            get {
                return sequence;
            }
        }

        public void Register(string key,string iv)
        {
            Interlocked.Exchange(ref this.key, key);
            Interlocked.Exchange(ref this.iv, iv); 
        }

        public void Cancel()
        {
            Interlocked.Exchange(ref this.key, null);
            Interlocked.Exchange(ref this.iv, null);
            Interlocked.Exchange(ref this.sequence, null);
        }

        public string CreateSequence()
        {
            sequence = Guid.NewGuid().ToString();
            return sequence;
        }
    }
}
