using System;
using System.Net;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;

using NetMQ;
using NetMQ.Sockets;

namespace P2pCommunicationPoc
{
    internal class ServerPersona 
    {
        string selfId = string.Empty;
        string peerId = string.Empty;  
        string backendSelfId = string.Empty;

        private int serverPortNumber;
        private DealerSocket serverBackendSocket;
        private RouterSocket serverFrontendSocket;
        private object sendMessageLock = new object();

        int heartbeatInterval = 5000;
        private NetMQPoller recieveMessagePoller;

        object connectedPeersLock = new object();
        Dictionary<string, PeerConnectionInfo> connectedPeersInfo = new Dictionary<string, PeerConnectionInfo>();
        
        private Task serverPersonaTask;

        public void Start(string selfId, string peerId)
        {
            this.selfId = selfId;
            this.peerId = peerId;

            serverFrontendSocket = new RouterSocket();
            //TODO: set following socket options
            // ZMQ_IDENTITY
            serverFrontendSocket.Options.Identity = Encoding.UTF8.GetBytes(selfId);
            //ZMQ_RECONNECT_IVL
            //ZMQ_RCVHWM
            //ZMQ_SNDHWM
            serverFrontendSocket.ReceiveReady += ProcessReceivedMessage;

            var myConnectionEndpointsInfo = ConnectionEndpointsHelper.GetConnectionEndpointsInfoFromAppConfig(selfId);
            //bind the address to router socket
            var bindAddress = $"tcp://{myConnectionEndpointsInfo.LocalNetworkAddress}:{myConnectionEndpointsInfo.LocalNetworkPort}";
            serverFrontendSocket.Bind(bindAddress);

            //TODO: update the registry with candidate addresses

            //create a backend socket
            //connect the backend socket to the server socket
            //this enables sending messages in a thread-safe manner
            serverBackendSocket = new DealerSocket();
            backendSelfId = $"{selfId}:BACKEND";
            serverBackendSocket.Options.Identity = Encoding.UTF8.GetBytes(backendSelfId);
            serverBackendSocket.Connect($"tcp://localhost:{myConnectionEndpointsInfo.LocalNetworkPort}");

            //create a heartbeat timer
            //periodically send a heartbeat message to all connected peers
            var heartbeatTimer = new NetMQTimer(heartbeatInterval);
            heartbeatTimer.Elapsed += SendHeartbeat;

            //start the recieve message loop
            recieveMessagePoller = new NetMQPoller();
            recieveMessagePoller.Add(serverFrontendSocket);
            recieveMessagePoller.Add(heartbeatTimer);

            serverPersonaTask = Task.Factory.StartNew(recieveMessagePoller.Run);
        }

        public void Stop()
        {
            //TODO:
            //disconnect and close the frontend/backend sockets connections
            recieveMessagePoller.Stop();
            serverPersonaTask.Wait();
        }

        public void SendMessage(byte[] data)
        {
            ////append the target address for routing
            ////[backendSelf ID][empty][self ID][peer ID][self ID][auth token][DATA][data]
            //dataMessage.Push(backendSelfId);

            var dataMessage = MessageHelper.CreateDataMessage(selfId, peerId, selfId, string.Empty, data);
            lock (sendMessageLock)
            {
                serverBackendSocket.SendMultipartMessage(dataMessage);
            }
        }

        EventHandler? onConnected;

        public event EventHandler OnConnected
        {
            add
            {
                onConnected += value;
            }

            remove
            {
                if (onConnected != null)
                    onConnected -= value;
            }
        }

        void RaiseOnConnected(EventArgs args)
        {
            if (onConnected != null)
                onConnected(this, args);
        }

        EventHandler? onDisconnected;

        public event EventHandler OnDisconnected
        {
            add
            {
                onDisconnected += value;
            }

            remove
            {
                if (onDisconnected != null)
                    onDisconnected -= value;
            }
        }

        void RaiseOnDisconnected(EventArgs args)
        {
            if (onDisconnected != null)
                onDisconnected(this, args);
        }

        OnReceiveMessageHandler? onReceiveMessage;

        public event OnReceiveMessageHandler OnReceiveMessage
        {
            add
            {
                onReceiveMessage += value;
            }

            remove
            {
                if (onReceiveMessage != null)
                    onReceiveMessage -= value;
            }
        }

        void RaiseOnRecieveMessage(ReceiveMessageEventArgs args)
        {
            if (onReceiveMessage != null)
                onReceiveMessage(this, args);
        }

        void SendHeartbeat(object? sender, NetMQTimerEventArgs e)
        {
            var currentDateTime = DateTime.UtcNow;

            var expiredPeers = connectedPeersInfo
                .Where(pi => pi.Value.Expiry < currentDateTime)
                .Select(pi => pi.Key).ToList();

            //TODO: 
            //remove expired peers

            //send the heartbeat message to active/connected peers
            foreach (var peerId in connectedPeersInfo.Keys)
            {
                var heartbeatMessage = MessageHelper.CreateHeartbeatMessage(selfId, peerId, selfId, string.Empty);
                //append the target address for routing
                //[peer ID][empty][self ID][peer ID][self ID][auth token][HELLO][empty]
                heartbeatMessage.Push(peerId);
                serverFrontendSocket.SendMultipartMessage(heartbeatMessage);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <remarks>
        /// Incoming message formats from the Peers
        ///     [sender id][e][envelope][body]
        ///     [envelope] -> [sender ID][destination ID][source ID][auth token][message type]
        ///     [body] -> [data]
        /// </remarks>
        void ProcessReceivedMessage(object? sender, NetMQSocketEventArgs e)
        {
            //TODO: Use the following pattern to recieve messages
            //string msg;
            ////  receiving 1000 messages or less if not available
            //for (int count = 0; count < 1000; i++)
            //{
            //    // exit the for loop if failed to receive a message
            //    if (!a.Socket.TryReceiveFrameString(out msg))
            //        break;

            //    // send a response
            //    a.Socket.Send("Response");
            //}

            var receivedMessage = new NetMQMessage();
            if (e.Socket.TryReceiveMultipartMessage(ref receivedMessage))
            {
                //[sender id][e][sender ID][destination ID][source ID][auth token][message type][data]
                var socketProvidedSenderId = receivedMessage.Pop().ConvertToString();

                //check to see the auto added sender ID is from a backend socket
                if (socketProvidedSenderId.Equals(backendSelfId))
                {
                    //[e][sender ID][destination ID][source ID][auth token][message type][data]
                    ProcessSendMessage(receivedMessage);
                }
                else
                {
                    //[e][sender ID][destination ID][source ID][auth token][message type][data]
                    MessageHelper.ReadMessageEnvelope(receivedMessage,
                        out string senderId,
                        out string destinationId,
                        out string sourceId,
                        out string authorizationToken,
                        out MessageType messageType);

                    //DEBUG
                    var data = receivedMessage.Pop().ToByteArray();
                    RaiseOnRecieveMessage(new ReceiveMessageEventArgs(senderId, messageType, data));

                    switch (messageType)
                    {
                        case MessageType.HELLO:
                            ProcessHelloMessage(receivedMessage, senderId, destinationId, sourceId, authorizationToken);
                            break;
                        case MessageType.HEARTBEAT:
                            ProcessHeartbeatMessage(receivedMessage, senderId, destinationId, sourceId, authorizationToken);
                            break;
                        case MessageType.DATA:
                            ProcessDataMessage(receivedMessage);
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
                //MESSAGE TYPES
                //MESSAGE_TYPE = INTERNAL - Internal Messages from BE to FE

                //TODO: Data Frame handling 
                //RaiseOnRecieveMessage(new RecieveMessageEventArgs(message));
            }
        }

        void ProcessSendMessage(NetMQMessage receivedMessage)
        {
            //send this message 
            //[e][sender ID][destination ID][source ID][auth token][message type][data]
            receivedMessage.Pop();
            //[sender ID][destination ID][source ID][auth token][message type][data]
            receivedMessage.Pop();
            //[destination ID][source ID][auth token][message type][data]
            var destinationId = receivedMessage.Pop();


            //[source ID][auth token][message type][data]
            receivedMessage.Push(destinationId);
            //[destination ID][source ID][auth token][message type][data]
            receivedMessage.Push(selfId);
            //[sender ID][destination ID][source ID][auth token][message type][data]
            receivedMessage.PushEmptyFrame();
            //[e][sender ID][destination ID][source ID][auth token][message type][data]
            receivedMessage.Push(destinationId);
            //[destination ID][e][sender ID][destination ID][source ID][auth token][message type][data]

            serverFrontendSocket.SendMultipartMessage(receivedMessage);
        }

        void ProcessHelloMessage(NetMQMessage recvMessage,
            string recvSenderId,
            string recvDestinationId,
            string recvSourceId,
            string recvAuthorizationToken)
        {
            //Store SENDER in connected peers 
            var connectedPeer = new PeerConnectionInfo
            {
                PeerId = recvSenderId,
                IsConnected = true,
                Expiry = DateTime.UtcNow + TimeSpan.FromMilliseconds(heartbeatInterval * 3)
            };
            lock(connectedPeersLock)
            {
                connectedPeersInfo.Add(recvSenderId, connectedPeer);
            }

            //Check if DESTINTATION equals SELF
            if (selfId == recvDestinationId)
            {
                //reply back with Hello
                //generate the default HELLO message
                //[empty][sender ID][destination ID][source ID][auth token][HELLO][empty]
                var replyHelloMessage = MessageHelper.CreateHelloMessage(selfId, recvSenderId, selfId, string.Empty);

                //append the target address for routing
                //[recvSender ID][empty][self ID][recvSender ID][self ID][auth token][HELLO][empty]
                replyHelloMessage.Push(recvSenderId);
                serverFrontendSocket.SendMultipartMessage(replyHelloMessage);

                //raise the OnConnected Event
                RaiseOnConnected(new EventArgs());
            }
            else
            {
                //generate the default HELLO message
                //[empty][self ID][destination ID][source ID][auth token][HELLO][empty]
                var forwardHelloMessage = MessageHelper.CreateHelloMessage(selfId, recvDestinationId, recvSourceId, string.Empty);

                //append the destinationId for forwarding
                //[destination ID][empty][self ID][destination ID][source ID][auth token][HELLO][empty]
                forwardHelloMessage.Push(recvDestinationId);
                
                //forwared to other correpondsing peer
                serverFrontendSocket.SendMultipartMessage(forwardHelloMessage);
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
            string recvDestinationId,
            string recvSourceId,
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

            //Check if DESTINTATION equals self
            //if not route/forward it to other correponsing peer
            if ((isValidSender == true) && 
                (selfId != recvDestinationId))
            {
                //generate the default HELLO message
                //[empty][self ID][destination ID][source ID][auth token][HEARTBEAT][empty]
                var forwardHelloMessage = MessageHelper.CreateHeartbeatMessage(selfId, recvDestinationId, recvSourceId, string.Empty);

                //append the destinationId for forwarding
                //[destination ID][empty][self ID][destination ID][source ID][auth token][HEARTBEAT][empty]
                forwardHelloMessage.Push(recvDestinationId);

                //forwared to other correpondsing peer
                serverFrontendSocket.SendMultipartMessage(forwardHelloMessage);
            }
        }

        void ProcessDataMessage(NetMQMessage receivedMessage)
        {
            //Check is SENDER if is among connected peers 
            //if no - discard 
            
            //update connection status of DESTINATION peer

            //Check if DESTINTATION equals self
            //if no - need to route to other correponsing peer
        }

    }
}
