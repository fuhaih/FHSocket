using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using FHSocket.TCPInteface;
using FHSocket.Exceptions;
using System.Collections.Generic;

namespace FHSocket.TCP
{

    //由官网SocketAsyncEventArgs例子改编

    // Implements the connection logic for the socket server.  
    // After accepting a connection, all data read from the client 
    // is sent back to the client. The read and echo back to the client pattern 
    // is continued until the client disconnects.
    public class SocketServer
    {
        public IPEndPoint ServerEndPoint { get; set; }
        private int receiveTimeout = 60;
        private int sendTimeout = 60;
        public int ReceiveTimeout
        {
            get { return receiveTimeout; }
            set { receiveTimeout = value; }
        }
        public int SendTimeout
        {
            get { return sendTimeout; }
            set { sendTimeout = value; }
        }
        private Func<ISocketBuffer> getSocketBuffer = null;
        //private ILog _log = LogManager.GetLogger(typeof(SocketServer));
        private object socketLock = new object();
        private int m_numConnections;   // 服务端最大连接数量
        BufferManager m_bufferManager;  // 缓存管理
        const int opsToPreAlloc = 2;    //暂时不知道干啥用 read, write (don't alloc buffer space for accepts)
        Socket listenSocket;            // socket监听器
        SocketAsyncEventArgsPool m_readWritePool;//SocketAsyncEventArgs对象池
        SocketManager s_manager = new SocketManager();//socket对象管理器,管理活跃的socket对象
        int m_numConnectedSockets;      //服务端当前连接数量
        int m_numData = 0; //获取数据的次数
        Semaphore m_maxNumberAcceptedClients;//信号量，大小为服务端最大连接数量，当连接数量到达最大值时，阻塞连接方法      
        /// <summary>
        /// 创建一个socket服务端对象
        /// </summary>
        /// <param name="numConnections">socket服务端最大连接数量</param>
        public SocketServer(int numConnections, IPEndPoint serverEndPoint)
        {
            this.ServerEndPoint = serverEndPoint;
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
        public void Start()
        {
            m_bufferManager = new BufferManager(getSocketBuffer);
            // create the socket which listens for incoming connections
            listenSocket = new Socket(ServerEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(ServerEndPoint);
            // start the server with a listen backlog of 100 connections
            listenSocket.Listen(5);

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
            if (m_readWritePool.Count() == 0)
            {
                Exception ex = new Exception("连接池已经被占满");
                Error(ex, SocketErrorType.FullPoolError, null);
            }
            bool wait = m_maxNumberAcceptedClients.WaitOne(20000);//等待一个型号量
            if (!wait)
            {
                Exception ex = new Exception("等待信号量超时");
                Error(ex, SocketErrorType.FullPoolError, null);
                StartAccept(acceptEventArg);
            }
            bool willRaiseEvent = listenSocket.AcceptAsync(acceptEventArg);
            //在高io下有可能同步接收，接收后不会触发Completed方法，同时返回值willRaiseEvent为false
            if (!willRaiseEvent)
            {
                try
                {
                    ProcessAccept(acceptEventArg);
                }
                catch (Exception ex)
                {
                    acceptEventArg.AcceptSocket.Close();
                    Error(ex, SocketErrorType.AcceptError, acceptEventArg.AcceptSocket.RemoteEndPoint);
                }
                finally
                {
                    StartAccept(acceptEventArg);
                }
            }
        }
        // This method is the callback method associated with Socket.AcceptAsync 
        // operations and is invoked when an accept operation is complete
        //
        void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                try
                {
                    ProcessAccept(e);
                }
                catch (Exception ex)
                {
                    Error(ex, SocketErrorType.AcceptError, e.RemoteEndPoint);
                }
            }
            else
            {

                Exception ex = new Exception("socket连接异常" + e.SocketError.ToString());
                Error(ex, SocketErrorType.AcceptError, null);
                e.AcceptSocket.Close();
                m_maxNumberAcceptedClients.Release();
            }
            StartAccept(e);
        }
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            // Accept the next connection request
            //var readEvent = new AutoResetEvent(false);

            IPEndPoint remoteEnp = (IPEndPoint)e.AcceptSocket.RemoteEndPoint;
            //remoteEnp为空的时候，可能远程终端已经断开连接了，所以不必继续操作，会浪费连接池资源
            //同时也不必区调用shutdown方法，由于没有远程地址，shutdown方法会报错
            if (remoteEnp == null)
            {
                Exception ex = new Exception("终端异常,接收到终端为null");
                Error(ex, SocketErrorType.AcceptError, e.AcceptSocket.RemoteEndPoint);
                e.AcceptSocket.LingerState = new LingerOption(true, 0);
                e.AcceptSocket.Close();
                m_maxNumberAcceptedClients.Release();
                //StartAccept(e);
                return;
            }

            // Get the socket for the accepted client connection and put it into the 
            //ReadEventArg object user token
            SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
            ((AsyncUserToken)readEventArgs.UserToken).Socket = e.AcceptSocket;
            ((AsyncUserToken)readEventArgs.UserToken).LastActiveTime = DateTime.Now;

            Interlocked.Increment(ref m_numConnectedSockets);
            //IPEndPoint remoteEnp = null;
            List<SocketAsyncEventArgs> es;
            ClientAccepted(m_numConnectedSockets, remoteEnp);
            bool sucess = s_manager.Enregister(remoteEnp, ServerEndPoint, readEventArgs, out es);
            if (!sucess)
            {
                for (int i = es.Count - 1; i >= 0; i--)
                {
                    var item = es[i];
                    CloseClientSocket(item, SocketCloseType.OverThreshold);
                }
                //CloseClientSocket(readEventArgs, SocketCloseType.OverThreshold);
                //StartAccept(e);
                return;
            }
            //设置发送和接收数据的超时时间
            byte[] buffer = new byte[e.AcceptSocket.ReceiveBufferSize];
            readEventArgs.SetBuffer(buffer, 0, buffer.Length);
            // As soon as the client is connected, post a receive to the connection
            bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
            //bool read = readEvent.WaitOne();
            //StartAccept(e);
            if (!willRaiseEvent)
            {
                ProcessReceive(readEventArgs);
            }
        }
        // This method is called whenever a receive or send operation is completed on a socket 
        //
        // <param name="e">SocketAsyncEventArg associated with the completed receive operation</param>
        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            token.LastActiveTime = DateTime.Now;
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    {
                        try
                        {
                            ProcessReceive(e);
                        }
                        catch (Exception ex)
                        {
                            Error(ex, SocketErrorType.OperationError, e.RemoteEndPoint);
                        }

                    }

                    break;
                case SocketAsyncOperation.Send:
                    {
                        try
                        {
                            ProcessSend(e);
                        }
                        catch (Exception ex)
                        {
                            Error(ex, SocketErrorType.OperationError, e.RemoteEndPoint);
                        }
                    }
                    break;
                case SocketAsyncOperation.Disconnect:
                    {

                    }
                    break;
                default:
                    {
                        Exception ex = new ArgumentException("socket 最后的操作不是发送或者接收操作");
                        Error(ex, SocketErrorType.OperationError, e.RemoteEndPoint);
                        //throw new 
                        //CloseClientSocket(e, SocketCloseType.OperationError);
                    }
                    break;

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
                m_bufferManager.SetBuffer(e, (pachage, userinfo) => {
                    Interlocked.Increment(ref m_numData);
                    byte[] sendBytes = Reeived(pachage, userinfo, token.Socket.RemoteEndPoint);// PackegeParser.Parser(pachage, authorize);
                    if (sendBytes != null)
                    {
                        resultBuffer = resultBuffer.Concat(sendBytes).ToArray();
                    }
                }, error => {
                    byte[] sendBytes = ReeivedError(error, token.Socket.RemoteEndPoint);// PackegeParser.Parser(pachage, authorize);
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
                CloseClientSocket(e, SocketCloseType.SocketAction);
            }
        }
        // This method is invoked when an asynchronous send operation completes.  
        // The method issues another receive on the socket to read any additional 
        // data sent from the client
        //
        // <param name="e"></param>
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            if (e.SocketError == SocketError.Success)
            {
                // done echoing data back to the client

                // read the next block of data send from the client
                bool willRaiseEvent = token.Socket.ReceiveAsync(e);
                if (!willRaiseEvent)
                {
                    ProcessReceive(e);
                }

            }
            else
            {
                CloseClientSocket(e, SocketCloseType.SocketAction);
            }
        }
        private void CloseClientSocket(SocketAsyncEventArgs e, SocketCloseType type)
        {
            AsyncUserToken token = e.UserToken as AsyncUserToken;
            IPEndPoint remoteEnp = null; ;
            // close the socket associated with the client
            try
            {
                remoteEnp = (IPEndPoint)token.Socket.RemoteEndPoint;
                token.Socket.LingerState = new LingerOption(true, 0);
                //处理短链接TIME_WAIT问题。
                token.Socket.Shutdown(SocketShutdown.Both);
                bool bl = s_manager.Deregister(remoteEnp, ServerEndPoint, e);//释放活跃socket
                if (bl)
                {
                    m_maxNumberAcceptedClients.Release();//释放一个信号量
                    m_bufferManager.DisposeBuffer(e);//释放缓存
                    // Free the SocketAsyncEventArg so they can be reused by another client
                    m_readWritePool.Push(e);//把socket添加回缓存池
                    // decrement the counter keeping track of the total number of clients connected to the server
                    Interlocked.Decrement(ref m_numConnectedSockets);
                    ClientClosed(m_numConnectedSockets, remoteEnp, type);
                }
            }
            // throws if client process has already closed
            catch (Exception ex)
            {
                Error(ex, SocketErrorType.CloseError, remoteEnp);
            }
            finally
            {
                token.Socket.Close();
            }


        }
        public virtual byte[] Reeived(byte[] pachage, UserAgent socket, EndPoint enp)
        {
            return null;
        }
        public virtual void ClientAccepted(int clientNum, EndPoint remoteEnp)
        {

        }
        public virtual void ClientClosed(int clientNum, EndPoint remoteEnp, SocketCloseType type)
        {

        }
        public virtual byte[] ReeivedError(Exception ex, EndPoint enp)
        {
            return null;
        }
        public virtual void Error(Exception ex, SocketErrorType errortype, EndPoint enp)
        {

        }
        public void RegisterSocketBuffer<Tbuffer>() where Tbuffer : ISocketBuffer, new()
        {
            getSocketBuffer = () => { return new Tbuffer(); };
        }
        public void SetThreshold(int threshold)
        {
            s_manager.Threshold = threshold;
        }
        private void StartSocketCollect()
        {
            //直接用Thread创建后台线程原因：
            //该线程是定时回收socket的线程，需要保持最高优先级，已确保定时器的准确
            //Thread可以控制优先级
            //ThreadStart start = new ThreadStart(SocketCollect);
            //Thread collect = new Thread(start);
            //collect.Priority = ThreadPriority.Highest;//设置线程的优先级
            //collect.Start();
            //**先用timer实现，看看效果**

            //TM.Timer timer = new TM.Timer();
            //timer.Interval = 60000;
            //timer.AutoReset = true;
            //timer.Elapsed += SocketCollect;
            //timer.Start();

        }
    }

}
