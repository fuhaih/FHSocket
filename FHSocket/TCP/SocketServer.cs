using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using FHSocket.TCPInteface;

namespace FHSocket.TCP
{

    //由官网SocketAsyncEventArgs例子改编

    // Implements the connection logic for the socket server.  
    // After accepting a connection, all data read from the client 
    // is sent back to the client. The read and echo back to the client pattern 
    // is continued until the client disconnects.
    public class SocketServer
    {
        private Func<ISocketBuffer> getSocketBuffer = null;
        //private ILog _log = LogManager.GetLogger(typeof(SocketServer));

        private object socketLock = new object();
        private int m_numConnections;   // 服务端最大连接数量
        BufferManager m_bufferManager;  // 缓存管理
        const int opsToPreAlloc = 2;    //暂时不知道干啥用 read, write (don't alloc buffer space for accepts)
        Socket listenSocket;            // socket监听器
        SocketAsyncEventArgsPool m_readWritePool;//SocketAsyncEventArgs对象池
        int m_numConnectedSockets;      //服务端当前连接数量
        int m_numData = 0; //获取数据的次数
        Semaphore m_maxNumberAcceptedClients;//信号量，大小为服务端最大连接数量，当连接数量到达最大值时，阻塞连接方法

        /// <summary>
        /// 创建一个socket服务端对象
        /// </summary>
        /// <param name="numConnections">socket服务端最大连接数量</param>
        public SocketServer(int numConnections)
        {
            m_numConnectedSockets = 0;
            m_numConnections = numConnections;
            m_readWritePool = new SocketAsyncEventArgsPool(numConnections);
            m_maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);
        }

        // Initializes the server by preallocating reusable buffers and 
        // context objects.  These objects do not need to be preallocated 
        // or reused, but it is done this way to illustrate how the API can 
        // easily be used to create reusable objects to increase server performance.
        //
        public void Init()
        {
            SocketAsyncEventArgs readWriteEventArg;
            //给SocketAsyncEventArgs对象池添加对象
            //对象池中的对象数量是最大连接数
            //对象池中的对象可重复使用
            for (int i = 0; i < m_numConnections; i++)
            {
                readWriteEventArg = new SocketAsyncEventArgs();
                readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(IO_Completed);
                readWriteEventArg.UserToken = new AsyncUserToken();
                m_readWritePool.Push(readWriteEventArg);
            }

        }

        // Starts the server such that it is listening for 
        // incoming connection requests.    
        //
        // <param name="localEndPoint">The endpoint which the server will listening 
        // for connection requests on</param>
        public void Start(IPEndPoint localEndPoint)
        {
            m_bufferManager = new BufferManager(getSocketBuffer);
            // create the socket which listens for incoming connections
            listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(localEndPoint);
            // start the server with a listen backlog of 100 connections
            listenSocket.Listen(m_numConnections);

            // post accepts on the listening socket
            StartAccept(null);
        }

        // Begins an operation to accept a connection request from the client 
        //
        // <param name="acceptEventArg">The context object to use when issuing 
        // the accept operation on the server's listening socket</param>
        public void StartAccept(SocketAsyncEventArgs acceptEventArg)
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

            m_maxNumberAcceptedClients.WaitOne();//等待一个型号量
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
            // Get the socket for the accepted client connection and put it into the 
            //ReadEventArg object user token
            SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
            //设置发送和接收数据的超时时间
            e.AcceptSocket.ReceiveTimeout = 60;
            e.AcceptSocket.SendTimeout = 60;
            ((AsyncUserToken)readEventArgs.UserToken).Socket = e.AcceptSocket;

            byte[] buffer = new byte[e.AcceptSocket.ReceiveBufferSize];
            readEventArgs.SetBuffer(buffer, 0, buffer.Length);
            //readEventArgs.BufferList = new List<ArraySegment<byte>>();
            //readEventArgs.BufferList.Add(new ArraySegment<byte>());
            // As soon as the client is connected, post a receive to the connection
            bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
            if (!willRaiseEvent)
            {
                ProcessReceive(readEventArgs);
            }

            // Accept the next connection request
            StartAccept(e);
            Interlocked.Increment(ref m_numConnectedSockets);
            ClientAccepted(m_numConnectedSockets,e);
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
                    throw new ArgumentException("socket 最后的操作不是发送或者接收操作");
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
                
                //回复信息数据缓存
                //每当解析出一条完成数据时，就给该缓存添加一段回复数据
                //每当解析一条完成数据失败时，也给该缓存添加一段回复数据
                byte[] resultBuffer = new byte[0];
                m_bufferManager.SetBuffer(e,(pachage, userinfo)=> {
                    Interlocked.Increment(ref m_numData);
                    byte[] sendBytes = Reeived(pachage,ref userinfo);// PackegeParser.Parser(pachage, authorize);
                    if (sendBytes != null)
                    {
                        resultBuffer = resultBuffer.Concat(sendBytes).ToArray();
                    }
                },error=> {
                    byte[] sendBytes = ReeivedError(error);// PackegeParser.Parser(pachage, authorize);
                    if (sendBytes != null)
                    {
                        resultBuffer = resultBuffer.Concat(sendBytes).ToArray();
                    }
                });
                //当没有回复数据时，服务端暂时不向客户端发送回传数据
                if (resultBuffer.Length == 0)
                {
                    ProcessSend(e);
                }
                else
                {
                    e.SetBuffer(resultBuffer, 0, resultBuffer.Length);
                    bool willRaiseEvent = token.Socket.SendAsync(e);
                    if (!willRaiseEvent)
                    {
                        ProcessSend(e);
                    }
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
                // done echoing data back to the client
                AsyncUserToken token = (AsyncUserToken)e.UserToken;
                // read the next block of data send from the client
                bool willRaiseEvent = token.Socket.ReceiveAsync(e);
                if (!willRaiseEvent)
                {
                    ProcessReceive(e);
                }
            }
            else
            {
                CloseClientSocket(e);
            }
        }
        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            
            AsyncUserToken token = e.UserToken as AsyncUserToken;
            // close the socket associated with the client
            try
            {
                token.Socket.Shutdown(SocketShutdown.Send);
                token.Socket.Close();
                // decrement the counter keeping track of the total number of clients connected to the server
                m_maxNumberAcceptedClients.Release();//释放一个信号量
            }
            // throws if client process has already closed
            catch (Exception ex) { }     
            m_bufferManager.DisposeBuffer(e);    
            // Free the SocketAsyncEventArg so they can be reused by another client
            m_readWritePool.Push(e);
            Interlocked.Decrement(ref m_numConnectedSockets);
            ClientClosed(m_numConnectedSockets,e);
        }
        public virtual byte[] Reeived(byte[] pachage,ref UserAgent socket)
        {
            return null;
        }
        public virtual void ClientAccepted(int clientNum, SocketAsyncEventArgs e)
        {
            
        }
        public virtual void ClientClosed(int clientNum, SocketAsyncEventArgs e)
        {
            
        }
        public virtual byte[] ReeivedError(Exception ex)
        {
            return null;
        }

        public void RegisterSocketBuffer<Tbuffer>() where Tbuffer :ISocketBuffer,new()
        {
            getSocketBuffer = () => { return new Tbuffer(); };
        }
    }

}
