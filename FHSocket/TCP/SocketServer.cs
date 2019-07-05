﻿using FHSocket.Buffer;
using FHSocket.TCPInteface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FHSocket.TCP
{
    // Implements the connection logic for the socket server.  
    // After accepting a connection, all data read from the client 
    // is sent back to the client. The read and echo back to the client pattern 
    // is continued until the client disconnects.
    public class SocketServer:IBagConfig
    {
        private int m_numConnections;   // the maximum number of connections the sample is designed to handle simultaneously 
        //private int m_receiveBufferSize;// buffer size to use for each socket I/O operation 
        BufferManager m_bufferManager;  // represents a large reusable set of buffers for all socket operations

        SocketManager m_socketManager;

        const int opsToPreAlloc = 2;    // read, write (don't alloc buffer space for accepts)
        Socket listenSocket;            // the socket used to listen for incoming connection requests
                                        // pool of reusable SocketAsyncEventArgs objects for write, read and accept socket operations
        SocketAsyncEventArgsPool m_readWritePool;
        int m_totalBytesRead;           // counter of the total # bytes received by the server
        int m_numConnectedSockets;      // the total number of clients connected to the server 
        SemaphoreSlim m_maxNumberAcceptedClients;

        int m_listen_port;
        IPAddress m_ipaddress;

        private IMassageHandle msgHandle;
        public IMassageHandle MsgHandle { get { return msgHandle; } }

        public SocketServer(SocketServerBuilder builder)
        {
            this.msgHandle = builder.MsgHandle;
            this.m_numConnections = builder.MaxConnections;
            //this.m_receiveBufferSize = builder.ReceiveBufferSize;
            this.m_listen_port = builder.Port;
            this.m_ipaddress = builder.Address;

            m_totalBytesRead = 0;
            m_numConnectedSockets = 0;

            // allocate buffers such that the maximum number of sockets can have one outstanding read and 
            //write posted to the socket simultaneously  
            m_bufferManager = new BufferManager(builder.ReceiveBufferSize * builder.MaxConnections * opsToPreAlloc,
                builder.ReceiveBufferSize);
            //m_bufferManager = new BufferManager();


            m_socketManager = new SocketManager(this);

            m_readWritePool = new SocketAsyncEventArgsPool(builder.MaxConnections);
            m_maxNumberAcceptedClients = new SemaphoreSlim(builder.MaxConnections, builder.MaxConnections);
        }

        private SocketServer(int numConnections):this(numConnections,32*1024)
        {

        }

        // Create an uninitialized server instance.  
        // To start the server listening for connection requests
        // call the Init method followed by Start method 
        //
        // <param name="numConnections">the maximum number of connections the sample is designed to handle simultaneously</param>
        // <param name="receiveBufferSize">buffer size to use for each socket I/O operation</param>
        private SocketServer(int numConnections, int receiveBufferSize)
        {

        }

        // Initializes the server by preallocating reusable buffers and 
        // context objects.  These objects do not need to be preallocated 
        // or reused, but it is done this way to illustrate how the API can 
        // easily be used to create reusable objects to increase server performance.
        //
        private void Init()
        {
            // Allocates one large byte buffer which all I/O operations use a piece of.  This gaurds 
            // against memory fragmentation
            m_bufferManager.InitBuffer();

            // preallocate pool of SocketAsyncEventArgs objects
            SocketAsyncEventArgs readWriteEventArg;

            for (int i = 0; i < m_numConnections; i++)
            {
                //Pre-allocate a set of reusable SocketAsyncEventArgs
                readWriteEventArg = new SocketAsyncEventArgs();
                readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);
                readWriteEventArg.UserToken = new AsyncUserToken() { Token=i};

                // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
                m_bufferManager.SetBuffer(readWriteEventArg);

                // add SocketAsyncEventArg to the pool
                m_readWritePool.Push(readWriteEventArg);
            }

        }

        // Starts the server such that it is listening for 
        // incoming connection requests.    
        //
        // <param name="localEndPoint">The endpoint which the server will listening 
        // for connection requests on</param>
        public void StartListen()
        {
            Init();
            IPEndPoint localEndPoint = new IPEndPoint(m_ipaddress, m_listen_port);
            // create the socket which listens for incoming connections
            listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(localEndPoint);
            // start the server with a listen backlog of 100 connections
            listenSocket.Listen(100);

            // post accepts on the listening socket
            StartAccept(null);

            //Console.WriteLine("{0} connected sockets with one outstanding receive posted to each....press any key", m_outstandingReadCount);
            //Console.WriteLine("Press any key to terminate the server process....");
            //Console.ReadKey();
        }

        // Begins an operation to accept a connection request from the client 
        //
        // <param name="acceptEventArg">The context object to use when issuing 
        // the accept operation on the server's listening socket</param>
        private void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
            }
            else
            {
                // socket must be cleared since the context object is being reused
                acceptEventArg.AcceptSocket = null;
            }

            m_maxNumberAcceptedClients.Wait();
            bool willRaiseEvent = listenSocket.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
            {
                ProcessAccept(acceptEventArg);
            }
        }

        // This method is the callback method associated with Socket.AcceptAsync 
        // operations and is invoked when an accept operation is complete
        //
        void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            Interlocked.Increment(ref m_numConnectedSockets);
            Console.WriteLine("Client connection accepted. There are {0} clients connected to the server",
                m_numConnectedSockets);

            // Get the socket for the accepted client connection and put it into the 
            //ReadEventArg object user token
            SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
            ((AsyncUserToken)readEventArgs.UserToken).Socket = e.AcceptSocket;
            // As soon as the client is connected, post a receive to the connection
            bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
            if (!willRaiseEvent)
            {
                ProcessReceive(readEventArgs);
            }

            // Accept the next connection request
            StartAccept(e);
        }

        // This method is called whenever a receive or send operation is completed on a socket 
        //
        // <param name="e">SocketAsyncEventArg associated with the completed receive operation</param>
        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }

        }

        // This method is invoked when an asynchronous receive operation completes. 
        // If the remote host closed the connection, then the socket is closed.  
        // If data was received then the data is echoed back to the client.
        //
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            // check if the remote host closed the connection
            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                //increment the count of the total bytes receive by the serve  r
                Interlocked.Add(ref m_totalBytesRead, e.BytesTransferred);
                Console.WriteLine("The server has read a total of {0} bytes", m_totalBytesRead);

                if (m_socketManager.Read(e)&& m_socketManager.Write(e))
                {
                    //读取到一个完整消息时，发送一个回复给客户端
                    //如果没有需要回复的消息，就继续接收
                    //否则要把回复消息发送回客户端才能继续接收

                    //echo the data received back to the client
                    bool willRaiseEvent = token.Socket.SendAsync(e);
                    if (!willRaiseEvent)
                    {
                        ProcessSend(e);
                    }
                }
                else {
                    /**没有获取完整的消息时，再继续读取socket的缓存*/
                    Receive(e);
                }
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        // This method is invoked when an asynchronous send operation completes.  
        // The method issues another receive on the socket to read any additional 
        // data sent from the client
        //
        // <param name="e"></param>
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                /**如果数据过大，需要多次发送，这里判断还有没有需要发送的数据
                 */
                // done echoing data back to the client
                AsyncUserToken token = (AsyncUserToken)e.UserToken;
                if (m_socketManager.Write(e))
                {
                    Send(e);
                }
                else {
                    //设置buffer
                    // read the next block of data send from the client
                    Receive(e);
                }

            }
            else
            {
                CloseClientSocket(e);
            }
        }

        private void Receive(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            m_bufferManager.ResetBuffer(e);
            bool willRaiseEvent = token.Socket.ReceiveAsync(e);
            if (!willRaiseEvent)
            {
                ProcessReceive(e);
            }
        }

        private void Send(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            bool willRaiseEvent = token.Socket.SendAsync(e);
            if (!willRaiseEvent)
            {
                ProcessSend(e);
            }
        }

        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = e.UserToken as AsyncUserToken;

            // close the socket associated with the client
            try
            {
                token.Socket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch (Exception) { }
            token.Socket.Close();

            // decrement the counter keeping track of the total number of clients connected to the server
            Interlocked.Decrement(ref m_numConnectedSockets);

            m_socketManager.Clean(e);
            // Free the SocketAsyncEventArg so they can be reused by another client
            m_readWritePool.Push(e);

            m_maxNumberAcceptedClients.Release();
            Console.WriteLine("A client has been disconnected from the server. There are {0} clients connected to the server", m_numConnectedSockets);
        }
    }

    /// <summary>
    /// 建造者模式
    /// 使用Builder类来构造SocketServer
    /// </summary>
    public class SocketServerBuilder
    {
        private IMassageHandle msgHandle;
        public IMassageHandle MsgHandle { get { return msgHandle; } }

        private int maxConnections = 1000;
        public int MaxConnections { get { return maxConnections; } }

        private int receiveBufferSize = 16 * 1024;
        public int ReceiveBufferSize { get { return receiveBufferSize; } }

        private IPAddress address = IPAddress.Parse("0.0.0.0");
        public IPAddress Address { get { return address; } }

        private int port = 6606;
        public int Port { get { return port; } }

        /// <summary>
        /// 设置数据处理类，该类型需要实现IMassageHandle接口
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public SocketServerBuilder WithMassageHandle<T>() where T : IMassageHandle
        {
            msgHandle = Activator.CreateInstance<T>();
            return this;
        }

        /// <summary>
        /// 设置host;127.0.0.1只能本地访问，要设置远程访问需要0.0.0.0
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        public SocketServerBuilder WithHost(string host)
        {
            address = IPAddress.Parse(host);
            return this;
        }

        /// <summary>
        /// 设置客户端最大连接数
        /// </summary>
        /// <param name="maxconnections"></param>
        /// <returns></returns>
        public SocketServerBuilder SetMaxConnections(int maxconnections)
        {
            this.maxConnections = maxconnections;
            return this;
        }

        /// <summary>
        /// 设置缓冲区大小
        /// </summary>
        /// <param name="receiveBufferSize"></param>
        /// <returns></returns>
        public SocketServerBuilder SetReceiveBufferSize(int receiveBufferSize)
        {
            this.receiveBufferSize = receiveBufferSize;
            return this;
        }

        /// <summary>
        /// 设置监听端口
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public SocketServerBuilder SetPort(int port)
        {
            this.port = port;
            return this;
        }

        /// <summary>
        /// 构建SocketServe
        /// </summary>
        /// <returns></returns>
        public SocketServer Build()
        {
            return new SocketServer(this);
        }

    }
}
