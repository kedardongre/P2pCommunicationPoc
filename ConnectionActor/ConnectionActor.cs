using NetMQ;
using NetMQ.Sockets;

namespace P2pCommunicationPoc
{
    public class ConnectionActor
    {
        ConnectionMessageLoop serverLoop = new ConnectionMessageLoop();
        
        ConnectionMessageLoop clientLoop = new ConnectionMessageLoop();

        bool isConnected = false;
        readonly object lockObject = new ();
        
        delegate void SendMessageDelegate(byte[] data);
        SendMessageDelegate? activeSendMessageFuncPtr = null;

        //TODO: Need to implement a Start function for router/forwarder use-cases i.e. no peer id(s) for initialization and multiple connected peers
        public void Start(string selfId, string peerId)
        {
            //setup event handler the and start the server message loop
            serverLoop.OnConnected += ServerLoopConnected;
            serverLoop.OnDisconnected += ServerLoopDisconnected;
            serverLoop.OnReceiveMessage += ServerLoopRecievedMessage;
            serverLoop.Start(selfId, peerId, false);

            //setup event handler the and start the client message loop
            clientLoop.OnConnected += ClientLoopConnected;
            clientLoop.OnDisconnected += ClientLoopDisconnected;
            clientLoop.OnReceiveMessage += ClientLoopReceivedMessage;
            clientLoop.Start(selfId, peerId, true);
        }

        public void Stop()
        {
            serverLoop.Stop();
            clientLoop.Stop();
        }

        public void SendMessage(byte[] data)
        {
            if(activeSendMessageFuncPtr != null)
                activeSendMessageFuncPtr(data);
        }

        //TODO: Need to implement a OnConnected handler for router/forwarder use-cases i.e. multiple connected peers
        void ServerLoopConnected(object? sender, EventArgs e)
        {
            lock (lockObject)
            {
                isConnected = true;
                if (activeSendMessageFuncPtr == null)
                    activeSendMessageFuncPtr = serverLoop.SendMessage;
            }
        }

        //TODO: Need to implement a OnDisconnected handler for router/forwarder use-cases i.e. multiple connected peers
        void ServerLoopDisconnected(object? sender, EventArgs e)
        {
            lock (lockObject)
            {
                if (isConnected)
                {
                    //TODO: should isConnected be reset ?
                    isConnected = false;
                    if (activeSendMessageFuncPtr == serverLoop.SendMessage)
                        activeSendMessageFuncPtr = null;
                }
            }
        }

        //TODO: Need to implement a OnConnected handler for router/forwarder use-cases i.e. multiple connected peers
        void ClientLoopConnected(object? sender, EventArgs e)
        {
            lock (lockObject)
            {
                isConnected = true;
                activeSendMessageFuncPtr = clientLoop.SendMessage;
            }
        }

        //TODO: Need to implement a OnDisconnected handler for router/forwarder use-cases i.e. multiple connected peers
        void ClientLoopDisconnected(object? sender, EventArgs e)
        {
            lock (lockObject)
            {
                if (isConnected)
                {
                    //TODO: should isConnected be reset ?
                    isConnected = false;
                    activeSendMessageFuncPtr = null;
                }
            }
        }

        void ServerLoopRecievedMessage(object s, ReceiveMessageEventArgs e)
        {
            RaiseOnReceiveMessage(this, e);
        }

        void ClientLoopReceivedMessage(object s, ReceiveMessageEventArgs e)
        {
            RaiseOnReceiveMessage(this, e);
        }

        public event OnReceiveMessageHandler OnReceiveMessage;

        void RaiseOnReceiveMessage(object s, ReceiveMessageEventArgs args)
        {
            OnReceiveMessage(s, args);
        }

    }
}