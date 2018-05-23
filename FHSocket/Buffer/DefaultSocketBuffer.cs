﻿using FHSocket.TCPInteface;
using System.Collections.Generic;
using System.Linq;
using System;

namespace FHSocket.Buffer
{
    class DefaultSocketBuffer:ISocketBuffer
    {
        public object BufferLock = new object();
        private UserAgent userinfo = new UserAgent();
        public UserAgent UserInfo
        {
            get { return userinfo; }
            set { userinfo = value; }
        }
        private byte[] Buffer = new byte[0];
        public DefaultSocketBuffer()
        {
            Buffer = new byte[0];
        }
        public void Cache(IEnumerable<byte> buffer)
        {
            lock (BufferLock)
            {
                Buffer = Buffer.Concat(buffer).ToArray();
            }
        }

        public byte[] Next()
        {
            lock (BufferLock)
            {
                if (Buffer.Length > 0)
                {
                    byte[] result = Buffer.Take(Buffer.Length).ToArray();
                    return result;
                }
                else
                {
                    return null;
                }
            }

        }

        public void Clear()
        {
            lock (BufferLock)
            {
                Buffer = new byte[0];
            }
        }

    }
}
