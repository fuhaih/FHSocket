using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IOCPSocket
{
    class SocketListener
    {
        //Buffers for sockets are unmanaged by .NET.
        //So memory used for buffers gets "pinned", which makes the
        //.NET garbage collector work around it, fragmenting the memory.
        //Circumvent this problem by putting all buffers together
        //in one block in memory. Then we will assign a part of that space
        //to each SocketAsyncEventArgs object, and
        //reuse that buffer space each time we reuse the SocketAsyncEventArgs object.
        //Create a large reusable set of buffers for all socket operations.
        BufferManager theBufferManager;

        // the socket used to listen for incoming connection requests
        Socket listenSocket;

        //A Semaphore has two parameters, the initial number of available slots
        // and the maximum number of slots. We'll make them the same.
        //This Semaphore is used to keep from going over max connection #.
        //(It is not about controlling threading really here.)
        Semaphore theMaxConnectionsEnforcer;

        //an object that we pass in and which has all the settings the listener needs
        SocketListenerSettings socketListenerSettings;

        PrefixHandler prefixHandler;
        MessageHandler messageHandler;

        // pool of reusable SocketAsyncEventArgs objects for accept operations
        SocketAsyncEventArgsPool poolOfAcceptEventArgs;
        // pool of reusable SocketAsyncEventArgs objects for
        //receive and send socket operations
        SocketAsyncEventArgsPool poolOfRecSendEventArgs;

        //_______________________________________________________________________________
        // Constructor.
        public SocketListener(SocketListenerSettings theSocketListenerSettings)
        {
            this.socketListenerSettings = theSocketListenerSettings;
            this.prefixHandler = new PrefixHandler();
            this.messageHandler = new MessageHandler();

            //Allocate memory for buffers. We are using a separate buffer space for
            //receive and send, instead of sharing the buffer space, like the Microsoft
            //example does.
            this.theBufferManager = new BufferManager(this.socketListenerSettings.BufferSize
                * this.socketListenerSettings.NumberOfSaeaForRecSend
                * this.socketListenerSettings.OpsToPreAllocate,
                    this.socketListenerSettings.BufferSize
                * this.socketListenerSettings.OpsToPreAllocate);

            this.poolOfRecSendEventArgs = new
                SocketAsyncEventArgsPool(this.socketListenerSettings.NumberOfSaeaForRecSend);

            this.poolOfAcceptEventArgs = new
                SocketAsyncEventArgsPool(this.socketListenerSettings.MaxAcceptOps);

            // Create connections count enforcer
            this.theMaxConnectionsEnforcer = new
                Semaphore(this.socketListenerSettings.MaxConnections,
                this.socketListenerSettings.MaxConnections);

            //Microsoft's example called these from Main method, which you
            //can easily do if you wish.
            Init();
            StartListen();
        }

        //____________________________________________________________________________
        // initializes the server by preallocating reusable buffers and
        // context objects (SocketAsyncEventArgs objects).
        //It is NOT mandatory that you preallocate them or reuse them. But, but it is
        //done this way to illustrate how the API can
        // easily be used to create reusable objects to increase server performance.

        internal void Init()
        {
            // Allocate one large byte buffer block, which all I/O operations will
            //use a piece of. This guards against memory fragmentation.
            this.theBufferManager.InitBuffer();

            // preallocate pool of SocketAsyncEventArgs objects for accept operations
            for (Int32 i = 0; i < this.socketListenerSettings.MaxAcceptOps; i++)
            {
                // add SocketAsyncEventArg to the pool
                this.poolOfAcceptEventArgs.Push(
                    CreateNewSaeaForAccept(poolOfAcceptEventArgs));
            }

            //The pool that we built ABOVE is for SocketAsyncEventArgs objects that do
            // accept operations.
            //Now we will build a separate pool for SAEAs objects
            //that do receive/send operations. One reason to separate them is that accept
            //operations do NOT need a buffer, but receive/send operations do.
            //ReceiveAsync and SendAsync require
            //a parameter for buffer size in SocketAsyncEventArgs.Buffer.
            // So, create pool of SAEA objects for receive/send operations.
            SocketAsyncEventArgs eventArgObjectForPool;

            Int32 tokenId;

            for (Int32 i = 0; i < this.socketListenerSettings.NumberOfSaeaForRecSend; i++)
            {
                //Allocate the SocketAsyncEventArgs object for this loop,
                //to go in its place in the stack which will be the pool
                //for receive/send operation context objects.
                eventArgObjectForPool = new SocketAsyncEventArgs();

                // assign a byte buffer from the buffer block to
                //this particular SocketAsyncEventArg object
                this.theBufferManager.SetBuffer(eventArgObjectForPool);

                tokenId = poolOfRecSendEventArgs.AssignTokenId() + 1000000;

                //Attach the SocketAsyncEventArgs object
                //to its event handler. Since this SocketAsyncEventArgs object is
                //used for both receive and send operations, whenever either of those
                //completes, the IO_Completed method will be called.
                eventArgObjectForPool.Completed += new
                        EventHandler<SocketAsyncEventArgs>(IO_Completed);

                //We can store data in the UserToken property of SAEA object.
                DataHoldingUserToken theTempReceiveSendUserToken = new
                    DataHoldingUserToken(eventArgObjectForPool, eventArgObjectForPool.Offset,
                    eventArgObjectForPool.Offset + this.socketListenerSettings.BufferSize,
                    this.socketListenerSettings.ReceivePrefixLength,
                    this.socketListenerSettings.SendPrefixLength, tokenId);

                //We'll have an object that we call DataHolder, that we can remove from
                //the UserToken when we are finished with it. So, we can hang on to the
                //DataHolder, pass it to an app, serialize it, or whatever.
                theTempReceiveSendUserToken.CreateNewDataHolder();

                eventArgObjectForPool.UserToken = theTempReceiveSendUserToken;

                // add this SocketAsyncEventArg object to the pool.
                this.poolOfRecSendEventArgs.Push(eventArgObjectForPool);
            }

    //____________________________________________________________________________
    // This method is called when we need to create a new SAEA object to do
    //accept operations. The reason to put it in a separate method is so that
    //we can easily add more objects to the pool if we need to.
    //You can do that if you do NOT use a buffer in the SAEA object that does
    //the accept operations.
        internal SocketAsyncEventArgs CreateNewSaeaForAccept(SocketAsyncEventArgsPool pool)
        {
            //Allocate the SocketAsyncEventArgs object.
            SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs();

            //SocketAsyncEventArgs.Completed is an event, (the only event,)
            //declared in the SocketAsyncEventArgs class.
            //See http://msdn.microsoft.com/en-us/library/
            //       system.net.sockets.socketasynceventargs.completed.aspx.
            //An event handler should be attached to the event within
            //a SocketAsyncEventArgs instance when an asynchronous socket
            //operation is initiated, otherwise the application will not be able
            //to determine when the operation completes.
            //Attach the event handler, which causes the calling of the
            //AcceptEventArg_Completed object when the accept op completes.
            acceptEventArg.Completed +=
                    new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);

            AcceptOpUserToken theAcceptOpToken = new
                        AcceptOpUserToken(pool.AssignTokenId() + 10000);

            acceptEventArg.UserToken = theAcceptOpToken;

            return acceptEventArg;

            // accept operations do NOT need a buffer.
            //You can see that is true by looking at the
            //methods in the .NET Socket class on the Microsoft website. AcceptAsync does
            //not require a parameter for buffer size.
        }

        //____________________________________________________________________________
        // This method starts the socket server such that it is listening for
        // incoming connection requests.
        internal void StartListen()
        {
            // create the socket which listens for incoming connections
            listenSocket = new
                    Socket(this.socketListenerSettings.LocalEndPoint.AddressFamily,
                    SocketType.Stream, ProtocolType.Tcp);

            //bind it to the port
            listenSocket.Bind(this.socketListenerSettings.LocalEndPoint);

            // Start the listener with a backlog of however many connections.
            //"backlog" means pending connections.
            //The backlog number is the number of clients that can wait for a
            //SocketAsyncEventArg object that will do an accept operation.
            //The listening socket keeps the backlog as a queue. The backlog allows
            //for a certain # of excess clients waiting to be connected.
            //If the backlog is maxed out, then the client will receive an error when
            //trying to connect.
            //max # for backlog can be limited by the operating system.
            listenSocket.Listen(this.socketListenerSettings.Backlog);

            //Server is listening now****

            // Calls the method which will post accepts on the listening socket.
            //This call just occurs one time from this StartListen method.
            //After that the StartAccept method will be called in a loop.
            StartAccept();
        }

        //____________________________________________________________________________
        // Begins an operation to accept a connection request from the client
        internal void StartAccept()
        {
            //Get a SocketAsyncEventArgs object to accept the connection.
            SocketAsyncEventArgs acceptEventArg;
            //Get it from the pool if there is more than one in the pool.
            //We could use zero as bottom, but one is a little safer.
            if (this.poolOfAcceptEventArgs.Count > 1)
            {
                try
                {
                    acceptEventArg = this.poolOfAcceptEventArgs.Pop();
                }
                //or make a new one.
                catch
                {
                    acceptEventArg = CreateNewSaeaForAccept(poolOfAcceptEventArgs);
                }
            }
            //or make a new one.
            else
            {
                acceptEventArg = CreateNewSaeaForAccept(poolOfAcceptEventArgs);
            }

            // Semaphore class is used to control access to a resource or pool of
            // resources. Enter the semaphore by calling the WaitOne method, which is
            // inherited from the WaitHandle class, and release the semaphore
            // by calling the Release method. This is a mechanism to prevent exceeding
            // the max # of connections we specified. We'll do this before
            // doing AcceptAsync. If maxConnections value has been reached,
            // then the thread will pause here until the Semaphore gets released,
            // which happens in the CloseClientSocket method.
            this.theMaxConnectionsEnforcer.WaitOne();

            // Socket.AcceptAsync begins asynchronous operation to accept the connection.
            // Note the listening socket will pass info to the SocketAsyncEventArgs
            // object that has the Socket that does the accept operation.
            // If you do not create a Socket object and put it in the SAEA object
            // before calling AcceptAsync and use the AcceptSocket property to get it,
            // then a new Socket object will be created for you by .NET.
            bool willRaiseEvent = listenSocket.AcceptAsync(acceptEventArg);

            // Socket.AcceptAsync returns true if the I/O operation is pending, i.e. is
            // working asynchronously. The
            // SocketAsyncEventArgs.Completed event on the acceptEventArg parameter
            // will be raised upon completion of accept op.
            // AcceptAsync will call the AcceptEventArg_Completed
            // method when it completes, because when we created this SocketAsyncEventArgs
            // object before putting it in the pool, we set the event handler to do it.
            // AcceptAsync returns false if the I/O operation completed synchronously.
            // The SocketAsyncEventArgs.Completed event on the acceptEventArg parameter
            // will NOT be raised when AcceptAsync returns false.
            if (!willRaiseEvent)
            {
                // The code in this if (!willRaiseEvent) statement only runs
                // when the operation was completed synchronously. It is needed because
                // when Socket.AcceptAsync returns false,
                // it does NOT raise the SocketAsyncEventArgs.Completed event.
                // And we need to call ProcessAccept and pass it the SAEA object.
                // This is only when a new connection is being accepted.
                // Probably only relevant in the case of a socket error.
                ProcessAccept(acceptEventArg);
            }
        }

        //____________________________________________________________________________
        // This method is the callback method associated with Socket.AcceptAsync
        // operations and is invoked when an async accept operation completes.
        //This is only when a new connection is being accepted.
        //Notice that Socket.AcceptAsync is returning a value of true, and
        //raising the Completed event when the AcceptAsync method completes.
        private void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            //Any code that you put in this method will NOT be called if
            //the operation completes synchronously, which will probably happen when
            //there is some kind of socket error. It might be better to put the code
            //in the ProcessAccept method.
            ProcessAccept(e);
        }

        //____________________________________________________________________________
        //The e parameter passed from the AcceptEventArg_Completed method
        //represents the SocketAsyncEventArgs object that did
        //the accept operation. in this method we'll do the handoff from it to the
        //SocketAsyncEventArgs object that will do receive/send.
        private void ProcessAccept(SocketAsyncEventArgs acceptEventArgs)
        {
            // This is when there was an error with the accept op. That should NOT
            // be happening often. It could indicate that there is a problem with
            // that socket. If there is a problem, then we would have an infinite
            // loop here, if we tried to reuse that same socket.
            if (acceptEventArgs.SocketError != SocketError.Success)
            {
                // Loop back to post another accept op. Notice that we are NOT
                // passing the SAEA object here.
                LoopToStartAccept();

                AcceptOpUserToken theAcceptOpToken =
            (AcceptOpUserToken)acceptEventArgs.UserToken;

                //Let's destroy this socket, since it could be bad.
                HandleBadAccept(acceptEventArgs);

                //Jump out of the method.
                return;
            }

            //Now that the accept operation completed, we can start another
            //accept operation, which will do the same. Notice that we are NOT
            //passing the SAEA object here.
            LoopToStartAccept();

            // Get a SocketAsyncEventArgs object from the pool of receive/send op
            //SocketAsyncEventArgs objects
            SocketAsyncEventArgs receiveSendEventArgs = this.poolOfRecSendEventArgs.Pop();

            //Create sessionId in UserToken.
            ((DataHoldingUserToken)receiveSendEventArgs.UserToken).CreateSessionId();

            //A new socket was created by the AcceptAsync method. The
            //SocketAsyncEventArgs object which did the accept operation has that
            //socket info in its AcceptSocket property. Now we will give
            //a reference for that socket to the SocketAsyncEventArgs
            //object which will do receive/send.
            receiveSendEventArgs.AcceptSocket = acceptEventArgs.AcceptSocket;

            //We have handed off the connection info from the
            //accepting socket to the receiving socket. So, now we can
            //put the SocketAsyncEventArgs object that did the accept operation
            //back in the pool for them. But first we will clear
            //the socket info from that object, so it will be
            //ready for a new socket when it comes out of the pool.
            acceptEventArgs.AcceptSocket = null;
            this.poolOfAcceptEventArgs.Push(acceptEventArgs);

            StartReceive(receiveSendEventArgs);
        }

        //____________________________________________________________________________
        //LoopToStartAccept method just sends us back to the beginning of the
        //StartAccept method, to start the next accept operation on the next
        //connection request that this listening socket will pass of to an
        //accepting socket. We do NOT actually need this method. You could
        //just call StartAccept() in ProcessAccept() where we called LoopToStartAccept().
        //This method is just here to help you visualize the program flow.
        private void LoopToStartAccept()
        {
            StartAccept();
        }

        //____________________________________________________________________________
        // Set the receive buffer and post a receive op.
        private void StartReceive(SocketAsyncEventArgs receiveSendEventArgs)
        {
            //Set the buffer for the receive operation.
            receiveSendEventArgs.SetBuffer(receiveSendToken.bufferOffsetReceive,
                 this.socketListenerSettings.BufferSize);

            // Post async receive operation on the socket.
            bool willRaiseEvent =
                 receiveSendEventArgs.AcceptSocket.ReceiveAsync(receiveSendEventArgs);

            //Socket.ReceiveAsync returns true if the I/O operation is pending. The
            //SocketAsyncEventArgs.Completed event on the e parameter will be raised
            //upon completion of the operation. So, true will cause the IO_Completed
            //method to be called when the receive operation completes.
            //That's because of the event handler we created when building
            //the pool of SocketAsyncEventArgs objects that perform receive/send.
            //It was the line that said
            //eventArgObjectForPool.Completed +=
            //     new EventHandler<SocketAsyncEventArgs>(IO_Completed);

            //Socket.ReceiveAsync returns false if I/O operation completed synchronously.
            //In that case, the SocketAsyncEventArgs.Completed event on the e parameter
            //will not be raised and the e object passed as a parameter may be
            //examined immediately after the method call
            //returns to retrieve the result of the operation.
            // It may be false in the case of a socket error.
            if (!willRaiseEvent)
            {
                //If the op completed synchronously, we need to call ProcessReceive
                //method directly. This will probably be used rarely, as you will
                //see in testing.
                ProcessReceive(receiveSendEventArgs);
            }
        }

        //____________________________________________________________________________
        // This method is called whenever a receive or send operation completes.
        // Here "e" represents the SocketAsyncEventArgs object associated
        //with the completed receive or send operation
        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            //Any code that you put in this method will NOT be called if
            //the operation completes synchronously, which will probably happen when
            //there is some kind of socket error.

            // determine which type of operation just
            // completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;

                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;

                default:
                    //This exception will occur if you code the Completed event of some
                    //operation to come to this method, by mistake.
                    throw new ArgumentException("The last operation completed on


                           the socket was not a receive or send");
            }
        }

        //____________________________________________________________________________
        // This method is invoked by the IO_Completed method
        // when an asynchronous receive operation completes.
        // If the remote host closed the connection, then the socket is closed.
        // Otherwise, we process the received data. And if a complete message was
        // received, then we do some additional processing, to
        // respond to the client.
        private void ProcessReceive(SocketAsyncEventArgs receiveSendEventArgs)
        {
            DataHoldingUserToken receiveSendToken =
                         (DataHoldingUserToken)receiveSendEventArgs.UserToken;

            // If there was a socket error, close the connection. This is NOT a normal
            // situation, if you get an error here.
            // In the Microsoft example code they had this error situation handled
            // at the end of ProcessReceive. Putting it here improves readability
            // by reducing nesting some.
            if (receiveSendEventArgs.SocketError != SocketError.Success)
            {
                receiveSendToken.Reset();
                CloseClientSocket(receiveSendEventArgs);

                //Jump out of the ProcessReceive method.
                return;
            }

            // If no data was received, close the connection. This is a NORMAL
            // situation that shows when the client has finished sending data.
            if (receiveSendEventArgs.BytesTransferred == 0)
            {
                receiveSendToken.Reset();
                CloseClientSocket(receiveSendEventArgs);
                return;
            }

            //The BytesTransferred property tells us how many bytes
            //we need to process.
            Int32 remainingBytesToProcess = receiveSendEventArgs.BytesTransferred;

            //If we have not got all of the prefix already,
            //then we need to work on it here.
            if (receiveSendToken.receivedPrefixBytesDoneCount <
                               this.socketListenerSettings.ReceivePrefixLength)
            {
                remainingBytesToProcess = prefixHandler.HandlePrefix(receiveSendEventArgs,
                          receiveSendToken, remainingBytesToProcess);

                if (remainingBytesToProcess == 0)
                {
                    // We need to do another receive op, since we do not have
                    // the message yet, but remainingBytesToProcess == 0.
                    StartReceive(receiveSendEventArgs);
                    //Jump out of the method.
                    return;
                }
            }

            // If we have processed the prefix, we can work on the message now.
            // We'll arrive here when we have received enough bytes to read
            // the first byte after the prefix.
            bool incomingTcpMessageIsReady = messageHandler
                      .HandleMessage(receiveSendEventArgs,
                      receiveSendToken, remainingBytesToProcess);

            if (incomingTcpMessageIsReady == true)
            {
                // Pass the DataHolder object to the Mediator here. The data in
                // this DataHolder can be used for all kinds of things that an
                // intelligent and creative person like you might think of.
                receiveSendToken.theMediator.HandleData(receiveSendToken.theDataHolder);

                // Create a new DataHolder for next message.
                receiveSendToken.CreateNewDataHolder();

                //Reset the variables in the UserToken, to be ready for the
                //next message that will be received on the socket in this
                //SAEA object.
                receiveSendToken.Reset();

                receiveSendToken.theMediator.PrepareOutgoingData();
                StartSend(receiveSendToken.theMediator.GiveBack());
            }
            else
            {
                // Since we have NOT gotten enough bytes for the whole message,
                // we need to do another receive op. Reset some variables first.

                // All of the data that we receive in the next receive op will be
                // message. None of it will be prefix. So, we need to move the
                // receiveSendToken.receiveMessageOffset to the beginning of the
                // receive buffer space for this SAEA.
                receiveSendToken.receiveMessageOffset = receiveSendToken.bufferOffsetReceive;

                // Do NOT reset receiveSendToken.receivedPrefixBytesDoneCount here.
                // Just reset recPrefixBytesDoneThisOp.
                receiveSendToken.recPrefixBytesDoneThisOp = 0;

                // Since we have not gotten enough bytes for the whole message,
                // we need to do another receive op.
                StartReceive(receiveSendEventArgs);
            }
        }

        //____________________________________________________________________________
        //Post a send op.
        private void StartSend(SocketAsyncEventArgs receiveSendEventArgs)
        {
            DataHoldingUserToken receiveSendToken =
                           (DataHoldingUserToken)receiveSendEventArgs.UserToken;

            //Set the buffer. You can see on Microsoft's page at
            //http://msdn.microsoft.com/en-us/library/
            //         system.net.sockets.socketasynceventargs.setbuffer.aspx
            //that there are two overloads. One of the overloads has 3 parameters.
            //When setting the buffer, you need 3 parameters the first time you set it,
            //which we did in the Init method. The first of the three parameters
            //tells what byte array to use as the buffer. After we tell what byte array
            //to use we do not need to use the overload with 3 parameters any more.
            //(That is the whole reason for using the buffer block. You keep the same
            //byte array as buffer always, and keep it all in one block.)
            //Now we use the overload with two parameters. We tell
            // (1) the offset and
            // (2) the number of bytes to use, starting at the offset.

            //The number of bytes to send depends on whether the message is larger than
            //the buffer or not. If it is larger than the buffer, then we will have
            //to post more than one send operation. If it is less than or equal to the
            //size of the send buffer, then we can accomplish it in one send op.
            if (receiveSendToken.sendBytesRemainingCount
                           <= this.socketListenerSettings.BufferSize)
            {
                receiveSendEventArgs.SetBuffer(receiveSendToken.bufferOffsetSend,
                           receiveSendToken.sendBytesRemainingCount);
                //Copy the bytes to the buffer associated with this SAEA object.
                Buffer.BlockCopy(receiveSendToken.dataToSend,
                           receiveSendToken.bytesSentAlreadyCount,
                      receiveSendEventArgs.Buffer, receiveSendToken.bufferOffsetSend,
                      receiveSendToken.sendBytesRemainingCount);
            }
            else
            {
                //We cannot try to set the buffer any larger than its size.
                //So since receiveSendToken.sendBytesRemainingCount > BufferSize, we just
                //set it to the maximum size, to send the most data possible.
                receiveSendEventArgs.SetBuffer(receiveSendToken.bufferOffsetSend,
                            this.socketListenerSettings.BufferSize);
                //Copy the bytes to the buffer associated with this SAEA object.
                Buffer.BlockCopy(receiveSendToken.dataToSend,
                           receiveSendToken.bytesSentAlreadyCount,
                      receiveSendEventArgs.Buffer, receiveSendToken.bufferOffsetSend,
                      this.socketListenerSettings.BufferSize);

                //We'll change the value of sendUserToken.sendBytesRemainingCount
                //in the ProcessSend method.
            }

            //post asynchronous send operation
            bool willRaiseEvent =
                 receiveSendEventArgs.AcceptSocket.SendAsync(receiveSendEventArgs);

            if (!willRaiseEvent)
            {
                ProcessSend(receiveSendEventArgs);
            }
        }

        //____________________________________________________________________________
        // This method is called by I/O Completed() when an asynchronous send completes.
        // If all of the data has been sent, then this method calls StartReceive
        //to start another receive op on the socket to read any additional
        // data sent from the client. If all of the data has NOT been sent, then it
        //calls StartSend to send more data.
        private void ProcessSend(SocketAsyncEventArgs receiveSendEventArgs)
        {
            DataHoldingUserToken receiveSendToken =
                           (DataHoldingUserToken)receiveSendEventArgs.UserToken;

            receiveSendToken.sendBytesRemainingCount =
                             receiveSendToken.sendBytesRemainingCount
                           - receiveSendEventArgs.BytesTransferred;
            receiveSendToken.bytesSentAlreadyCount +=
                           receiveSendEventArgs.BytesTransferred;

            if (receiveSendEventArgs.SocketError == SocketError.Success)
            {
                if (receiveSendToken.sendBytesRemainingCount == 0)
                {
                    StartReceive(receiveSendEventArgs);
                }
                else
                {
                    //If some of the bytes in the message have NOT been sent,
                    //then we will need to post another send operation.
                    //So let's loop back to StartSend().
                    StartSend(receiveSendEventArgs);
                }
            }
            else
            {
                //If we are in this else-statement, there was a socket error.
                //In this example we'll just close the socket if there was a socket error
                //when receiving data from the client.
                receiveSendToken.Reset();
                CloseClientSocket(receiveSendEventArgs);
            }
        }

        //_______________________________________________________________________
        // Does the normal destroying of sockets after
        // we finish receiving and sending on a connection.
        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            var receiveSendToken = (e.UserToken as DataHoldingUserToken);

            // do a shutdown before you close the socket
            try
            {
                e.AcceptSocket.Shutdown(SocketShutdown.Both);
            }
            // throws if socket was already closed
            catch (Exception)
            {
            }

            //This method closes the socket and releases all resources, both
            //managed and unmanaged. It internally calls Dispose.
            e.AcceptSocket.Close();

            //Make sure the new DataHolder has been created for the next connection.
            //If it has, then dataMessageReceived should be null.
            if (receiveSendToken.theDataHolder.dataMessageReceived != null)
            {
                receiveSendToken.CreateNewDataHolder();
            }

            // Put the SocketAsyncEventArg back into the pool,
            // to be used by another client. This
            this.poolOfRecSendEventArgs.Push(e);

            // decrement the counter keeping track of the total number of clients
            //connected to the server, for testing
            Interlocked.Decrement(ref this.numberOfAcceptedSockets);

            //Release Semaphore so that its connection counter will be decremented.
            //This must be done AFTER putting the SocketAsyncEventArg back into the pool,
            //or you can run into problems.
            this.theMaxConnectionsEnforcer.Release();
        }

        //____________________________________________________________________________
        private void HandleBadAccept(SocketAsyncEventArgs acceptEventArgs)
        {
            var acceptOpToken = (acceptEventArgs.UserToken as AcceptOpUserToken);

            //This method closes the socket and releases all resources, both
            //managed and unmanaged. It internally calls Dispose.
            acceptEventArgs.AcceptSocket.Close();

            //Put the SAEA back in the pool.
            poolOfAcceptEventArgs.Push(acceptEventArgs);
        }
    }

    class PrefixHandler
    {
        public Int32 HandlePrefix(SocketAsyncEventArgs e,
               DataHoldingUserToken receiveSendToken,
               Int32 remainingBytesToProcess)
        {
            //receivedPrefixBytesDoneCount tells us how many prefix bytes were
            //processed during previous receive ops which contained data for
            //this message. Usually there will NOT have been any previous
            //receive ops here. So in that case,
            //receiveSendToken.receivedPrefixBytesDoneCount would equal 0.
            //Create a byte array to put the new prefix in, if we have not
            //already done it in a previous loop.
            if (receiveSendToken.receivedPrefixBytesDoneCount == 0)
            {
                receiveSendToken.byteArrayForPrefix = new
                                 Byte[receiveSendToken.receivePrefixLength];
            }

            //If this next if-statement is true, then we have received at
            //least enough bytes to have the prefix. So we can determine the
            //length of the message that we are working on.
            if (remainingBytesToProcess >= receiveSendToken.receivePrefixLength
                                    - receiveSendToken.receivedPrefixBytesDoneCount)
            {
                //Now copy that many bytes to byteArrayForPrefix.
                //We can use the variable receiveMessageOffset as our main
                //index to show which index to get data from in the TCP
                //buffer.
                Buffer.BlockCopy(e.Buffer, receiveSendToken.receiveMessageOffset
                          - receiveSendToken.receivePrefixLength
                          + receiveSendToken.receivedPrefixBytesDoneCount,
                    receiveSendToken.byteArrayForPrefix,
                    receiveSendToken.receivedPrefixBytesDoneCount,
                    receiveSendToken.receivePrefixLength
                          - receiveSendToken.receivedPrefixBytesDoneCount);

                remainingBytesToProcess = remainingBytesToProcess
                          - receiveSendToken.receivePrefixLength
                          + receiveSendToken.receivedPrefixBytesDoneCount;

                receiveSendToken.recPrefixBytesDoneThisOp =
                    receiveSendToken.receivePrefixLength
                          - receiveSendToken.receivedPrefixBytesDoneCount;

                receiveSendToken.receivedPrefixBytesDoneCount =
                    receiveSendToken.receivePrefixLength;

                receiveSendToken.lengthOfCurrentIncomingMessage =
                    BitConverter.ToInt32(receiveSendToken.byteArrayForPrefix, 0);

                return remainingBytesToProcess;
            }

            //This next else-statement deals with the situation
            //where we have some bytes
            //of this prefix in this receive operation, but not all.
            else
            {
                //Write the bytes to the array where we are putting the
                //prefix data, to save for the next loop.
                Buffer.BlockCopy(e.Buffer, receiveSendToken.receiveMessageOffset
                            - receiveSendToken.receivePrefixLength
                            + receiveSendToken.receivedPrefixBytesDoneCount,
                        receiveSendToken.byteArrayForPrefix,
                        receiveSendToken.receivedPrefixBytesDoneCount,
                        remainingBytesToProcess);

                receiveSendToken.recPrefixBytesDoneThisOp = remainingBytesToProcess;
                receiveSendToken.receivedPrefixBytesDoneCount += remainingBytesToProcess;
                remainingBytesToProcess = 0;
            }

            // This section is needed when we have received
            // an amount of data exactly equal to the amount needed for the prefix,
            // but no more. And also needed with the situation where we have received
            // less than the amount of data needed for prefix.
            if (remainingBytesToProcess == 0)
            {
                receiveSendToken.receiveMessageOffset =
                    receiveSendToken.receiveMessageOffset -
            receiveSendToken.recPrefixBytesDoneThisOp;
                receiveSendToken.recPrefixBytesDoneThisOp = 0;
            }
            return remainingBytesToProcess;
        }
    }

    class MessageHandler
    {
        public bool HandleMessage(SocketAsyncEventArgs receiveSendEventArgs,
                    DataHoldingUserToken receiveSendToken,
                    Int32 remainingBytesToProcess)
        {
            bool incomingTcpMessageIsReady = false;

            //Create the array where we'll store the complete message,
            //if it has not been created on a previous receive op.
            if (receiveSendToken.receivedMessageBytesDoneCount == 0)
            {
                receiveSendToken.theDataHolder.dataMessageReceived =
                       new Byte[receiveSendToken.lengthOfCurrentIncomingMessage];
            }

            // Remember there is a receiveSendToken.receivedPrefixBytesDoneCount
            // variable, which allowed us to handle the prefix even when it
            // requires multiple receive ops. In the same way, we have a
            // receiveSendToken.receivedMessageBytesDoneCount variable, which
            // helps us handle message data, whether it requires one receive
            // operation or many.
            if (remainingBytesToProcess + receiveSendToken.receivedMessageBytesDoneCount
                       == receiveSendToken.lengthOfCurrentIncomingMessage)
            {
                // If we are inside this if-statement, then we got
                // the end of the message. In other words,
                // the total number of bytes we received for this message matched the
                // message length value that we got from the prefix.

                // Write/append the bytes received to the byte array in the
                // DataHolder object that we are using to store our data.
                Buffer.BlockCopy(receiveSendEventArgs.Buffer,
                    receiveSendToken.receiveMessageOffset,
                    receiveSendToken.theDataHolder.dataMessageReceived,
                    receiveSendToken.receivedMessageBytesDoneCount,
                    remainingBytesToProcess);

                incomingTcpMessageIsReady = true;
            }
            else
            {
                // If we are inside this else-statement, then that means that we
                // need another receive op. We still haven't got the whole message,
                // even though we have examined all the data that was received.
                // Not a problem. In SocketListener.ProcessReceive we will just call
                // StartReceive to do another receive op to receive more data.

                Buffer.BlockCopy(receiveSendEventArgs.Buffer,
                        receiveSendToken.receiveMessageOffset,
                        receiveSendToken.theDataHolder.dataMessageReceived,
                        receiveSendToken.receivedMessageBytesDoneCount,
                        remainingBytesToProcess);

                receiveSendToken.receiveMessageOffset =
                        receiveSendToken.receiveMessageOffset -
                        receiveSendToken.recPrefixBytesDoneThisOp;

                receiveSendToken.receivedMessageBytesDoneCount += remainingBytesToProcess;
            }
            return incomingTcpMessageIsReady;
        }
    }

    class BufferManager
    {
        // This class creates a single large buffer which can be divided up
        // and assigned to SocketAsyncEventArgs objects for use with each
        // socket I/O operation.
        // This enables buffers to be easily reused and guards against
        // fragmenting heap memory.
        //
        //This buffer is a byte array which the Windows TCP buffer can copy its data to.

        // the total number of bytes controlled by the buffer pool
        Int32 totalBytesInBufferBlock;

        // Byte array maintained by the Buffer Manager.
        byte[] bufferBlock;
        Stack<int> freeIndexPool;
        Int32 currentIndex;
        Int32 bufferBytesAllocatedForEachSaea;

        public BufferManager(Int32 totalBytes, Int32 totalBufferBytesInEachSaeaObject)
        {
            totalBytesInBufferBlock = totalBytes;
            this.currentIndex = 0;
            this.bufferBytesAllocatedForEachSaea = totalBufferBytesInEachSaeaObject;
            this.freeIndexPool = new Stack<int>();
        }

        // Allocates buffer space used by the buffer pool
        internal void InitBuffer()
        {
            // Create one large buffer block.
            this.bufferBlock = new byte[totalBytesInBufferBlock];
        }

        // Divide that one large buffer block out to each SocketAsyncEventArg object.
        // Assign a buffer space from the buffer block to the
        // specified SocketAsyncEventArgs object.
        //
        // returns true if the buffer was successfully set, else false
        internal bool SetBuffer(SocketAsyncEventArgs args)
        {
            if (this.freeIndexPool.Count > 0)
            {
                //This if-statement is only true if you have called the FreeBuffer
                //method previously, which would put an offset for a buffer space
                //back into this stack.
                args.SetBuffer(this.bufferBlock, this.freeIndexPool.Pop(),
                           this.bufferBytesAllocatedForEachSaea);
            }
            else
            {
                //Inside this else-statement is the code that is used to set the
                //buffer for each SAEA object when the pool of SAEA objects is built
                //in the Init method.
                if ((totalBytesInBufferBlock - this.bufferBytesAllocatedForEachSaea) <
                           this.currentIndex)
                {
                    return false;
                }
                args.SetBuffer(this.bufferBlock, this.currentIndex,
                                 this.bufferBytesAllocatedForEachSaea);
                this.currentIndex += this.bufferBytesAllocatedForEachSaea;
            }
            return true;
        }

        // Removes the buffer from a SocketAsyncEventArg object. This frees the
        // buffer back to the buffer pool. Try NOT to use the FreeBuffer method,
        // unless you need to destroy the SAEA object, or maybe in the case
        // of some exception handling. Instead, on the server
        // keep the same buffer space assigned to one SAEA object for the duration of
        // this app's running.
        internal void FreeBuffer(SocketAsyncEventArgs args)
        {
            this.freeIndexPool.Push(args.Offset);
            args.SetBuffer(null, 0, 0);
        }
    }

    class DataHoldingUserToken
    {
        internal Mediator theMediator;
        internal DataHolder theDataHolder;
        internal readonly Int32 bufferOffsetReceive;
        internal readonly Int32 permanentReceiveMessageOffset;
        internal readonly Int32 bufferOffsetSend;
        private Int32 idOfThisObject;

        internal Int32 lengthOfCurrentIncomingMessage;

        //receiveMessageOffset is used to mark the byte position where the message
        //begins in the receive buffer. This value can sometimes be out of
        //bounds for the data stream just received. But, if it is out of bounds, the
        //code will not access it.
        internal Int32 receiveMessageOffset;
        internal Byte[] byteArrayForPrefix;
        internal readonly Int32 receivePrefixLength;
        internal Int32 receivedPrefixBytesDoneCount = 0;
        internal Int32 receivedMessageBytesDoneCount = 0;
        //This variable will be needed to calculate the value of the
        //receiveMessageOffset variable in one situation. Notice that the
        //name is similar but the usage is different from the variable
        //receiveSendToken.receivePrefixBytesDone.
        internal Int32 recPrefixBytesDoneThisOp = 0;

        internal Int32 sendBytesRemainingCount;
        internal readonly Int32 sendPrefixLength;
        internal Byte[] dataToSend;
        internal Int32 bytesSentAlreadyCount;

        //The session ID correlates with all the data sent in a connected session.
        //It is different from the transmission ID in the DataHolder, which relates
        //to one TCP message. A connected session could have many messages, if you
        //set up your app to allow it.
        private Int32 sessionId;

        public DataHoldingUserToken(SocketAsyncEventArgs e, Int32 rOffset, Int32 sOffset,
               Int32 receivePrefixLength, Int32 sendPrefixLength, Int32 identifier)
        {
            this.idOfThisObject = identifier;

            //Create a Mediator that has a reference to the SAEA object.
            this.theMediator = new Mediator(e);
            this.bufferOffsetReceive = rOffset;
            this.bufferOffsetSend = sOffset;
            this.receivePrefixLength = receivePrefixLength;
            this.sendPrefixLength = sendPrefixLength;
            this.receiveMessageOffset = rOffset + receivePrefixLength;
            this.permanentReceiveMessageOffset = this.receiveMessageOffset;
        }

        //Let's use an ID for this object during testing, just so we can see what
        //is happening better if we want to.
        public Int32 TokenId
        {
            get
            {
                return this.idOfThisObject;
            }
        }

        internal void CreateNewDataHolder()
        {
            theDataHolder = new DataHolder();
        }

        //Used to create sessionId variable in DataHoldingUserToken.
        //Called in ProcessAccept().
        internal void CreateSessionId()
        {
            sessionId = Interlocked.Increment(ref Program.mainSessionId);
        }

        public Int32 SessionId
        {
            get
            {
                return this.sessionId;
            }
        }

        public void Reset()
        {
            this.receivedPrefixBytesDoneCount = 0;
            this.receivedMessageBytesDoneCount = 0;
            this.recPrefixBytesDoneThisOp = 0;
            this.receiveMessageOffset = this.permanentReceiveMessageOffset;
        }
    }

    class Mediator
    {
        private IncomingDataPreparer theIncomingDataPreparer;
        private OutgoingDataPreparer theOutgoingDataPreparer;
        private DataHolder theDataHolder;
        private SocketAsyncEventArgs saeaObject;

        public Mediator(SocketAsyncEventArgs e)
        {
            this.saeaObject = e;
            this.theIncomingDataPreparer = new IncomingDataPreparer(saeaObject);
            this.theOutgoingDataPreparer = new OutgoingDataPreparer();
        }

        internal void HandleData(DataHolder incomingDataHolder)
        {
            theDataHolder = theIncomingDataPreparer.HandleReceivedData
                           (incomingDataHolder, this.saeaObject);
        }

        internal void PrepareOutgoingData()
        {
            theOutgoingDataPreparer.PrepareOutgoingData(saeaObject, theDataHolder);
        }

        internal SocketAsyncEventArgs GiveBack()
        {
            return saeaObject;
        }
    }

    class IncomingDataPreparer
    {
        private DataHolder theDataHolder;
        private SocketAsyncEventArgs theSaeaObject;

        public IncomingDataPreparer(SocketAsyncEventArgs e)
        {
            this.theSaeaObject = e;
        }

        private Int32 ReceivedTransMissionIdGetter()
        {
            Int32 receivedTransMissionId =
                  Interlocked.Increment(ref Program.mainTransMissionId);
            return receivedTransMissionId;
        }

        private EndPoint GetRemoteEndpoint()
        {
            return this.theSaeaObject.AcceptSocket.RemoteEndPoint;
        }

        internal DataHolder HandleReceivedData(DataHolder incomingDataHolder,
                            SocketAsyncEventArgs theSaeaObject)
        {
            DataHoldingUserToken receiveToken =
                           (DataHoldingUserToken)theSaeaObject.UserToken;
            theDataHolder = incomingDataHolder;
            theDataHolder.sessionId = receiveToken.SessionId;
            theDataHolder.receivedTransMissionId =
                          this.ReceivedTransMissionIdGetter();
            theDataHolder.remoteEndpoint = this.GetRemoteEndpoint();
            this.AddDataHolder();
            return theDataHolder;
        }

        private void AddDataHolder()
        {
            lock (Program.lockerForList)
            {
                Program.listOfDataHolders.Add(theDataHolder);
            }
        }
    }

    class OutgoingDataPreparer
    {
        private DataHolder theDataHolder;

        internal void PrepareOutgoingData(SocketAsyncEventArgs e,
                           DataHolder handledDataHolder)
        {
            DataHoldingUserToken theUserToken = (DataHoldingUserToken)e.UserToken;
            theDataHolder = handledDataHolder;

            //In this example code, we will send back the receivedTransMissionId,
            // followed by the
            //message that the client sent to the server. And we must
            //prefix it with the length of the message. So we put 3
            //things into the array.
            // 1) prefix,
            // 2) receivedTransMissionId,
            // 3) the message that we received from the client, which
            // we stored in our DataHolder until we needed it.
            //That is our communication protocol. The client must know the protocol.

            //Convert the receivedTransMissionId to byte array.
            Byte[] idByteArray = BitConverter.GetBytes
                           (theDataHolder.receivedTransMissionId);

            //Determine the length of all the data that we will send back.
            Int32 lengthOfCurrentOutgoingMessage = idByteArray.Length
                           + theDataHolder.dataMessageReceived.Length;

            //So, now we convert the length integer into a byte array.
            //Aren't byte arrays wonderful? Maybe you'll dream about byte arrays tonight!
            Byte[] arrayOfBytesInPrefix = BitConverter.GetBytes
                           (lengthOfCurrentOutgoingMessage);

            //Create the byte array to send.
            theUserToken.dataToSend = new Byte[theUserToken.sendPrefixLength
                           + lengthOfCurrentOutgoingMessage];

            //Now copy the 3 things to the theUserToken.dataToSend.
            Buffer.BlockCopy(arrayOfBytesInPrefix, 0, theUserToken.dataToSend,
                           0, theUserToken.sendPrefixLength);
            Buffer.BlockCopy(idByteArray, 0, theUserToken.dataToSend,
                           theUserToken.sendPrefixLength, idByteArray.Length);
            //The message that the client sent is already in a byte array, in DataHolder.
            Buffer.BlockCopy(theDataHolder.dataMessageReceived, 0,
                   theUserToken.dataToSend, theUserToken.sendPrefixLength
                   + idByteArray.Length, theDataHolder.dataMessageReceived.Length);

            theUserToken.sendBytesRemainingCount =
                    theUserToken.sendPrefixLength + lengthOfCurrentOutgoingMessage;
            theUserToken.bytesSentAlreadyCount = 0;
        }
    }

    class DataHolder
    {
        //Remember, if a socket uses a byte array for its buffer, that byte array is
        //unmanaged in .NET and can cause memory fragmentation. So, first write to the
        //buffer block used by the SAEA object. Then, you can copy that data to another
        //byte array, if you need to keep it or work on it, and want to be able to put
        //the SAEA object back in the pool quickly, or continue with the data
        //transmission quickly.
        //DataHolder has this byte array to which you can copy the data.
        internal Byte[] dataMessageReceived;

        internal Int32 receivedTransMissionId;

        internal Int32 sessionId;

        //for testing. With a packet analyzer this can help you see specific connections.
        internal EndPoint remoteEndpoint;
    }

    internal sealed class SocketAsyncEventArgsPool
    {
        //just for assigning an ID so we can watch our objects while testing.
        private Int32 nextTokenId = 0;

        // Pool of reusable SocketAsyncEventArgs objects.
        Stack<socketasynceventargs /> pool;

        // initializes the object pool to the specified size.
        // "capacity" = Maximum number of SocketAsyncEventArgs objects
        internal SocketAsyncEventArgsPool(Int32 capacity)
        {
            this.pool = new Stack<socketasynceventargs /> (capacity);
        }

        // The number of SocketAsyncEventArgs instances in the pool.
        internal Int32 Count
        {
            get { return this.pool.Count; }
        }

        internal Int32 AssignTokenId()
        {
            Int32 tokenId = Interlocked.Increment(ref nextTokenId);
            return tokenId;
        }

        // Removes a SocketAsyncEventArgs instance from the pool.
        // returns SocketAsyncEventArgs removed from the pool.
        internal SocketAsyncEventArgs Pop()
        {
            lock (this.pool)
            {
                return this.pool.Pop();
            }
        }

        // Add a SocketAsyncEventArg instance to the pool.
        // "item" = SocketAsyncEventArgs instance to add to the pool.
        internal void Push(SocketAsyncEventArgs item)
        {
            if (item == null)
            {
                throw new ArgumentNullException("Items added to a SocketAsyncEventArgsPool cannot be null");
            }
            lock (this.pool)
            {
                this.pool.Push(item);
            }
        }
    }

    class SocketListenerSettings
    {
        // the maximum number of connections the sample is designed to handle simultaneously
        private Int32 maxConnections;

        // this variable allows us to create some extra SAEA objects for the pool,
        // if we wish.
        private Int32 numberOfSaeaForRecSend;

        // max # of pending connections the listener can hold in queue
        private Int32 backlog;

        // tells us how many objects to put in pool for accept operations
        private Int32 maxSimultaneousAcceptOps;

        // buffer size to use for each socket receive operation
        private Int32 receiveBufferSize;

        // length of message prefix for receive ops
        private Int32 receivePrefixLength;

        // length of message prefix for send ops
        private Int32 sendPrefixLength;

        // See comments in buffer manager.
        private Int32 opsToPreAllocate;

        // Endpoint for the listener.
        private IPEndPoint localEndPoint;

        public SocketListenerSettings(Int32 maxConnections,
        Int32 excessSaeaObjectsInPool, Int32 backlog, Int32 maxSimultaneousAcceptOps,
        Int32 receivePrefixLength, Int32 receiveBufferSize, Int32 sendPrefixLength,
        Int32 opsToPreAlloc, IPEndPoint theLocalEndPoint)
        {
            this.maxConnections = maxConnections;
            this.numberOfSaeaForRecSend = maxConnections + excessSaeaObjectsInPool;
            this.backlog = backlog;
            this.maxSimultaneousAcceptOps = maxSimultaneousAcceptOps;
            this.receivePrefixLength = receivePrefixLength;
            this.receiveBufferSize = receiveBufferSize;
            this.sendPrefixLength = sendPrefixLength;
            this.opsToPreAllocate = opsToPreAlloc;
            this.localEndPoint = theLocalEndPoint;
        }

        public Int32 MaxConnections
        {
            get
            {
                return this.maxConnections;
            }
        }
        public Int32 NumberOfSaeaForRecSend
        {
            get
            {
                return this.numberOfSaeaForRecSend;
            }
        }
        public Int32 Backlog
        {
            get
            {
                return this.backlog;
            }
        }
        public Int32 MaxAcceptOps
        {
            get
            {
                return this.maxSimultaneousAcceptOps;
            }
        }
        public Int32 ReceivePrefixLength
        {
            get
            {
                return this.receivePrefixLength;
            }
        }
        public Int32 BufferSize
        {
            get
            {
                return this.receiveBufferSize;
            }
        }
        public Int32 SendPrefixLength
        {
            get
            {
                return this.sendPrefixLength;
            }
        }
        public Int32 OpsToPreAllocate
        {
            get
            {
                return this.opsToPreAllocate;
            }
        }
        public IPEndPoint LocalEndPoint
        {
            get
            {
                return this.localEndPoint;
            }
        }
    }
}
