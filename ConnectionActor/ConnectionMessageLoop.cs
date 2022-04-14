using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

using NetMQ;
using NetMQ.Sockets;

namespace P2pCommunicationPoc
{
    internal class ConnectionMessageLoop
    {
        RouterSocket frontendSocket;
        int frontendSocketPort;
        string frontendSocketId = string.Empty;
        string frontendSocketAddress = string.Empty;

        DealerSocket backendSocket;
        string backendSocketId = string.Empty;

        string peerSocketAddress;
        string peerSocketId = string.Empty;

        bool isClientSocket;

        NetMQTimer receiveMessagePollinTimer;
        int retryEstablishConnectionInterval = 1000;
        int heartbeatInterval = 5000;

        NetMQPoller recieveMessagePoller;

        object sendMessageLock = new object();

        object connectedPeersLock = new object();
        Dictionary<string, ConnectionPeerInfo> connectedPeersInfo = new Dictionary<string, ConnectionPeerInfo>();

        Task receiveMessageLoopTask;

        public void Start(string frontendSocketId, string peerSocketId, bool isClientSocket)
        {
            this.frontendSocketId = frontendSocketId;
            this.peerSocketId = peerSocketId;

            this.isClientSocket = isClientSocket;

            //create and initialize the frontend socket
            InitializeFrontendSocket();

            //create and initialize the frontend socket
            InitializeBackendSocket();

            if (isClientSocket)
            {
                ConnectToPeerSocket();
            }

            //create a heartbeat timer
            receiveMessagePollinTimer = new NetMQTimer(isClientSocket ? retryEstablishConnectionInterval : heartbeatInterval);
            receiveMessagePollinTimer.Elapsed += ReceiveMessagePollinTimerElasped;

            //create the POLLER to process the heartbeat timer and frontend socket recv message events
            recieveMessagePoller = new NetMQPoller();
            recieveMessagePoller.Add(frontendSocket);
            recieveMessagePoller.Add(receiveMessagePollinTimer);

            //start the recieve message loop
            receiveMessageLoopTask = Task.Factory.StartNew(recieveMessagePoller.Run);

            //TODO: add a task exception handling using ContinueWith
        }

        void InitializeFrontendSocket()
        {
            frontendSocket = new RouterSocket();
            //TODO: set following socket options
            // ZMQ_IDENTITY
            frontendSocket.Options.Identity = Encoding.UTF8.GetBytes(frontendSocketId);
            //ZMQ_RECONNECT_IVL
            //ZMQ_RCVHWM
            //ZMQ_SNDHWM
            frontendSocket.ReceiveReady += ProcessReceivedMessage;

            if (isClientSocket == false)
            {
                //get a list of candidate address
                var myConnectionEndpointsInfo = EndpointsHelper.GetConnectionEndpointsInfoFromAppConfig(frontendSocketId);

                //bind the address to router socket
                frontendSocketAddress = $"tcp://{myConnectionEndpointsInfo.LocalNetworkAddress}:{myConnectionEndpointsInfo.LocalNetworkPort}";
                frontendSocketPort = myConnectionEndpointsInfo.LocalNetworkPort;
                frontendSocket.Bind(frontendSocketAddress);

                //TODO: update the registry with candidate addresses
            }
            else
            {
                frontendSocketAddress = $"inproc://{frontendSocketId}";
                frontendSocket.Bind(frontendSocketAddress);
            }

        }
        void InitializeBackendSocket()
        {
            backendSocket = new DealerSocket();
            backendSocketId = $"{frontendSocketId}:BACKEND";
            backendSocket.Options.Identity = Encoding.UTF8.GetBytes(backendSocketId);

            //connect the backend socket to the server socket
            //this enables sending messages in a thread-safe manner
            backendSocket.Connect((isClientSocket == false) ?
                $"tcp://localhost:{frontendSocketPort}" :
                frontendSocketAddress);
        }

        void ConnectToPeerSocket()
        {
            //TODO: retrieve from the registry the peer candidate addresses
            var peerEndpointsInfo = EndpointsHelper.GetConnectionEndpointsInfoFromAppConfig(peerSocketId);
            var peerAddress = $"tcp://{peerEndpointsInfo.LocalNetworkAddress}:{peerEndpointsInfo.LocalNetworkPort}";
            frontendSocket.Connect(peerAddress);

            //send a HELLO message to initiate connection
            var helloMessage = MessageHelper.CreateHelloMessage(frontendSocketId, peerSocketId, string.Empty);
            MessageHelper.WrapRoutingFrames(helloMessage, peerSocketId);
            frontendSocket.SendMultipartMessage(helloMessage);

            var connectedPeer = new ConnectionPeerInfo
            {
                PeerId = peerSocketId,
                IsConnected = false,
                Expiry = DateTime.UtcNow + TimeSpan.FromMilliseconds(heartbeatInterval * 3)
            };
            lock (connectedPeersLock)
            {
                connectedPeersInfo.Add(peerSocketId, connectedPeer);
            }

        }

        public void Stop()
        {
            //if (isClientSocket)
            //{
            //    //Send BYE Message
            //    //disconnect

            //    frontendSocket.Disconnect(peerSocketAddress);
            //}
            //else
            //{
            //    //Send BYE Message

            //}
            ////close the frontend socket
            //frontendSocket.Close();

            recieveMessagePoller.Stop();
            receiveMessageLoopTask.Wait();
        }

        public void SendMessage(byte[] data)
        {
            var dataMessage = MessageHelper.CreateDataMessage(frontendSocketId, peerSocketId, string.Empty, data);
            lock (sendMessageLock)
            {
                //MessageHelper.WrapRoutingFrames(dataMessage, frontendSocketId);
                backendSocket.SendMultipartMessage(dataMessage);
            }
        }

        public event EventHandler? OnConnected;
        void RaiseOnConnected(EventArgs args)
        {
            if (OnConnected != null)
                OnConnected(this, args);
        }

        public event EventHandler? OnDisconnected;
        void RaiseOnDisconnected(EventArgs args)
        {
            if (OnDisconnected != null)
                OnDisconnected(this, args);
        }

        public event OnReceiveMessageHandler? OnReceiveMessage;

        void RaiseOnRecieveMessage(ReceiveMessageEventArgs args)
        {
            if (OnReceiveMessage != null)
                OnReceiveMessage(this, args);
        }

        void ReceiveMessagePollinTimerElasped(object? sender, NetMQTimerEventArgs e)
        {
            //find all peer that are not connect yet and resend a Hello Message
            var notConnectedPeers = connectedPeersInfo
                .Where(pi => pi.Value.IsConnected == false)
                .Select(pi => pi.Key).ToList();

            foreach(var peer in notConnectedPeers)
            {
                //TODO: check if this peer has "expired" i.e. no response for the Hello Message within the allotted time
                //if yes raise a unable to connect exception
                var helloMessage = MessageHelper.CreateHelloMessage(frontendSocketId, peerSocketId, string.Empty);
                MessageHelper.WrapRoutingFrames(helloMessage, peerSocketId);
                frontendSocket.SendMultipartMessage(helloMessage);
            }

            //find all peers that are connected  
            var currentDateTime = DateTime.UtcNow;
            var expiredPeers = connectedPeersInfo
                .Where(pi => pi.Value.Expiry < currentDateTime)
                .Select(pi => pi.Key).ToList();

           //send the heartbeat message to connected peers
            foreach (var peerId in connectedPeersInfo.Keys)
            {
                var heartbeatMessage = MessageHelper.CreateHeartbeatMessage(frontendSocketId, peerId, string.Empty);
                MessageHelper.WrapRoutingFrames(heartbeatMessage, peerId);

                frontendSocket.SendMultipartMessage(heartbeatMessage);
            }

            //TODO: track an sendHeartbeat and recvHeartbeat expiry 
            //remove or raise a disconnected message for peers whose recvHeartbeat has expired
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <remarks>
        /// Incoming message formats from the Peers
        /// [sender id][e][destination ID][source ID][auth token][message_type][message_data]
        /// </remarks>
        void ProcessReceivedMessage(object? sender, NetMQSocketEventArgs e)
        {
            var receivedMessage = new NetMQMessage();
            if (e.Socket.TryReceiveMultipartMessage(ref receivedMessage))
            {
                //receivedMessage = [sender id][e][destination ID][source ID][auth token][message type][data]
                var senderId = MessageHelper.UnwrapRoutingFrames(receivedMessage);
                //receivedMessage = [e][destination ID][source ID][auth token][message type][data]

                //check to see the auto added sender ID is from a backend socket
                if (senderId.Equals(backendSocketId))
                {
                    ProcessSendMessage(receivedMessage);
                }
                else
                {
                    MessageHelper.ReadMessageEnvelope(receivedMessage,
                        out string sourceId,
                        out string destinationId,
                        out string authorizationToken,
                        out MessageType messageType);
                    //receivedMessage = [data]

                    switch (messageType)
                    {
                        case MessageType.HELLO:
                            ProcessHelloMessage(receivedMessage, senderId, sourceId, destinationId, authorizationToken);
                            break;
                        case MessageType.HEARTBEAT:
                            ProcessHeartbeatMessage(receivedMessage, senderId, sourceId, destinationId, authorizationToken);
                            break;
                        case MessageType.DATA:
                            ProcessDataMessage(receivedMessage, senderId, sourceId, destinationId, authorizationToken);
                            break;
                        case MessageType.BYE:
                            ProcessByeMessage(receivedMessage);
                            break;
                        default:
                            break;
                    }
                }
                //TODO:
                //HEADER
                //Protocol
                //Version
            }
        }

        void ProcessSendMessage(NetMQMessage message)
        {
            //send this message 
            //[e][destination ID][source ID][auth token][message type][data]
            if (message.FrameCount > 0)
            {
                var destinationId = message[1].ConvertToString();

                MessageHelper.WrapRoutingFrames(message, destinationId);
                //[destination ID][e][destination ID][source ID][auth token][message type][data]

                frontendSocket.SendMultipartMessage(message);
            }
        }

        void ProcessHelloMessage(NetMQMessage recvMessage,
            string recvSenderId,
            string recvSourceId,
            string recvDestinationId,
            string recvAuthorizationToken)
        {
            //TODO: REMOVE, DEBUG ONLY
            RaiseOnRecieveMessage(new ReceiveMessageEventArgs(recvSenderId, MessageType.HELLO, null));

            //Store SENDER in connected peers 
            var connectedPeer = new ConnectionPeerInfo
            {
                PeerId = recvSenderId,
                IsConnected = true,
                Expiry = DateTime.UtcNow + TimeSpan.FromMilliseconds(heartbeatInterval * 3)
            };
            lock (connectedPeersLock)
            {
                connectedPeersInfo[recvSenderId] = connectedPeer;
            }

            //Check if DESTINTATION equals SELF
            if (frontendSocketId == recvDestinationId)
            {
                if (isClientSocket == false)
                {
                    //reply back with Hello
                    //generate the default HELLO message for replying back to the sender
                    //[empty][destination ID][source ID][auth token][HELLO][empty]
                    //source ID = frontendSocketId
                    //destination ID = recvSenderId
                    var replyHelloMessage = MessageHelper.CreateHelloMessage(frontendSocketId, recvSenderId, string.Empty);

                    //append the routing information
                    //[destination ID][empty][destination ID][source ID][auth token][HELLO][empty]
                    //destination ID = recvSenderId
                    MessageHelper.WrapRoutingFrames(replyHelloMessage, recvSenderId);
                    frontendSocket.SendMultipartMessage(replyHelloMessage);
                }

                if (receiveMessagePollinTimer.Interval != heartbeatInterval)
                    receiveMessagePollinTimer.Interval = heartbeatInterval;

                //raise the OnConnected Event
                RaiseOnConnected(new EventArgs());
            }
            else
            {
                //generate the default HELLO message for forwarding to the actual destination
                //[empty][destination ID][source ID][auth token][HELLO][empty]
                //source ID = recvSenderId
                //destination ID = recvDestinationId
                var forwardHelloMessage = MessageHelper.CreateHelloMessage(recvSourceId, recvDestinationId, string.Empty);

                //append the routing information
                //[destination ID][empty][destination ID][source ID][auth token][HELLO][empty]
                //destination ID = recvDestinationId
                MessageHelper.WrapRoutingFrames(forwardHelloMessage, recvDestinationId);

                //forwared to other correpondsing peer
                frontendSocket.SendMultipartMessage(forwardHelloMessage);
            }
        }

        void ProcessByeMessage(NetMQMessage receivedMessage)
        {
            //Check is SENDER if is among connected peers 
            //if no - discard 

            //Check if DESTINTATION equals self
            //if yes 
            //if yes
            //  - remove sender from connected peers
            //  - raise the OnDisconnected Event
            //if no
            //  - remove sender from connected peers
            //  - route to other correponsing peer
        }

        void ProcessHeartbeatMessage(NetMQMessage receivedMessage,
            string recvSenderId,
            string recvSourceId,
            string recvDestinationId,
            string recvAuthorizationToken)
        {
            //TODO: REMOVE, DEBUG ONLY
            RaiseOnRecieveMessage(new ReceiveMessageEventArgs(recvSenderId, MessageType.HEARTBEAT, null));

            //Check is SENDER if is among connected peers 
            bool isValidSender = false;
            lock (connectedPeersLock)
            {
                //Update connection status of peer
                if (connectedPeersInfo.ContainsKey(recvSenderId))
                {
                    var connectedPeer = connectedPeersInfo[recvSenderId];
                    connectedPeer.IsConnected = true;
                    connectedPeer.Expiry = DateTime.UtcNow + TimeSpan.FromMilliseconds(heartbeatInterval * 3);
                    connectedPeersInfo[recvSenderId] = connectedPeer;
                    isValidSender = true;
                }
            }

            //Check if DESTINTATION equals self
            //if not route/forward it to other correponsing peer
            if ((isValidSender == true) &&
                (frontendSocketId != recvDestinationId))
            {
                //receivedMessage = [empty]

                //generate the default HEARTBEAT message
                //[empty][destination ID][source ID][auth token][HEARTBEAT][empty]
                //source ID = recvSenderId
                //destination ID = recvDestinationId
                var forwardHeartbeatMessage = MessageHelper.CreateHeartbeatMessage(recvSenderId, recvDestinationId, string.Empty);

                //append the destinationId for forwarding
                //receivedMessage = [destination ID][empty][destination ID][source ID][auth token][HEARTBEAT][empty]
                //destination ID = recvDestinationId
                MessageHelper.WrapRoutingFrames(forwardHeartbeatMessage, recvDestinationId);

                //forwared to other correpondsing peer
                frontendSocket.SendMultipartMessage(forwardHeartbeatMessage);
            }
        }

        void ProcessDataMessage(NetMQMessage recvMessage,
            string recvSenderId,
            string recvSourceId,
            string recvDestinationId,
            string recvAuthorizationToken)
        {
            //Check is SENDER if is among connected peers 
            bool isValidSender = false;
            lock (connectedPeersLock)
            {
                //Update connection status of peer
                if (connectedPeersInfo.ContainsKey(recvSenderId))
                {
                    var connectedPeer = connectedPeersInfo[recvSenderId];
                    connectedPeer.IsConnected = true;
                    connectedPeer.Expiry = DateTime.UtcNow + TimeSpan.FromMilliseconds(heartbeatInterval * 3);
                    connectedPeersInfo[recvSenderId] = connectedPeer;
                    isValidSender = true;
                }
            }

            if (isValidSender)
            {
                //receivedMessage = [data]
                var data = (recvMessage.FrameCount > 0) ? recvMessage.Pop().ToByteArray() : null;


                //Check if DESTINTATION equals SELF
                if (frontendSocketId == recvDestinationId)
                {
                    //raise the RaiseOnRecieveMessage Event
                    RaiseOnRecieveMessage(new ReceiveMessageEventArgs(recvSenderId, MessageType.DATA, data));
                }
                else
                {
                    //forward it to other correponsing peer

                    //generate the default DATA message for forwarding to the actual destination
                    //[empty][destination ID][source ID][auth token][DATA][data]
                    //source ID = recvSenderId
                    //destination ID = recvDestinationId
                    var forwardDataMessage = MessageHelper.CreateDataMessage(recvSenderId, recvDestinationId, string.Empty, data);

                    //append the routing information
                    //[destination ID][empty][destination ID][source ID][auth token][HELLO][empty]
                    //destination ID = recvDestinationId
                    MessageHelper.WrapRoutingFrames(forwardDataMessage, recvDestinationId);

                    //forwared to other correpondsing peer
                    frontendSocket.SendMultipartMessage(forwardDataMessage);
                }
            }
        }


    }
}
