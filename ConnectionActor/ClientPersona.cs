using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NetMQ;
using NetMQ.Sockets;

namespace P2pCommunicationPoc
{
    internal class ClientPersona 
    {
        string selfId;
        string peerId;
        string backendSelfId;

        private int peerServerPortNumber;
        private DealerSocket clientBackendSocket;
        private DealerSocket clientFrontendSocket;
        private object sendMessageLock = new object();

        int heartbeatInterval = 5000;
        private NetMQPoller recieveMessagePoller;

        object connectedPeersLock = new object();
        Dictionary<string, PeerConnectionInfo> connectedPeersInfo = new Dictionary<string, PeerConnectionInfo>();

        private Task clientPersonaTask;


        public void Start(string selfId, string peerId)
        {
            this.selfId = selfId;
            this.peerId = peerId;


            clientFrontendSocket = new DealerSocket();
            // ZMQ_IDENTITY
            clientFrontendSocket.Options.Identity = Encoding.UTF8.GetBytes(selfId);
            //TODO: set following socket options
            //ZMQ_RECONNECT_IVL
            //ZMQ_RCVHWM
            //ZMQ_SNDHWM
            clientFrontendSocket.ReceiveReady += ProcessReceivedMessage;

            //bind an inproc address to the frontend socket
            var bindAddress = $"inproc://{selfId}:FRONTEND-ENDPOINT";
            clientFrontendSocket.Bind(bindAddress);

            //TODO: retrieve from the registry the peer candidate addresses
            var peerEndpointsInfo = ConnectionEndpointsHelper.GetConnectionEndpointsInfoFromAppConfig(peerId);
            var peerAddress = $"tcp://{peerEndpointsInfo.LocalNetworkAddress}:{peerEndpointsInfo.LocalNetworkPort}";
            clientFrontendSocket.Connect(peerAddress);

            //create a backend socket
            //connect the backend socket to the client frontend socket
            //this enables sending messages in a thread-safe manner
            clientBackendSocket = new DealerSocket();
            backendSelfId = $"{selfId}:BACKEND";
            clientBackendSocket.Options.Identity = Encoding.UTF8.GetBytes(backendSelfId);
            clientBackendSocket.Connect(bindAddress);

            //create a heartbeat timer
            //periodically send a heartbeat message to all connected peers
            var heartbeatTimer = new NetMQTimer(heartbeatInterval);
            heartbeatTimer.Elapsed += SendHeartbeat;

            //start the recieve message loop
            recieveMessagePoller = new NetMQPoller();
            recieveMessagePoller.Add(clientFrontendSocket);
            recieveMessagePoller.Add(heartbeatTimer);

            clientPersonaTask = Task.Factory.StartNew(recieveMessagePoller.Run);

            //send a HELLO message to initiate connection
            var helloMsg = MessageHelper.CreateHelloMessage(selfId, peerId, selfId, string.Empty);
            clientFrontendSocket.SendMultipartMessage(helloMsg);
        }

        public void Stop()
        {
            //TODO:
            //disconnect and close the frontend/backend sockets connections
            recieveMessagePoller.Stop();
            clientPersonaTask.Wait();
        }

        public void SendMessage(byte[] data)
        {
            ////append the target address for routing
            ////[backendSelf ID][empty][self ID][peer ID][self ID][auth token][DATA][data]
            //dataMessage.Push(backendSelfId);

            var dataMessage = MessageHelper.CreateDataMessage(selfId, peerId, selfId, string.Empty, data);
            lock (sendMessageLock)
            {
                clientBackendSocket.SendMultipartMessage(dataMessage);
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
            {
                if (onDisconnected != null)
                    onDisconnected(this, args);
            }
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
                //[empty][self ID][peer ID][self ID][auth token][HELLO][empty]

                //NO NEED TO append the target address for routing as the frontend is a dealer socket
                //[peer ID][empty][self ID][peer ID][self ID][auth token][HELLO][empty]
                //heartbeatMessage.Push(peerId);

                clientFrontendSocket.SendMultipartMessage(heartbeatMessage);
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
                //check to see the received message from the backend socket
                if (receivedMessage.First.IsEmpty == false)
                {
                    //[backend socket sender id][e][sender ID][destination ID][source ID][auth token][message type][data]
                    var socketProvidedSenderId = receivedMessage.Pop().ConvertToString();

                    //check to see the auto added sender ID is from a backend socket
                    if (socketProvidedSenderId.Equals(backendSelfId))
                    {
                        //[e][sender ID][destination ID][source ID][auth token][message type][data]
                        ProcessSendMessage(receivedMessage);
                    }

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

            clientFrontendSocket.SendMultipartMessage(receivedMessage);
        }

        void ProcessHelloMessage(NetMQMessage recvMessage,
            string recvSenderId,
            string recvDestinationId,
            string recvSourceId,
            string recvAuthorizationToken)
        {

            //Check if DESTINTATION equals SELF
            if (selfId == recvDestinationId)
            {
                //This is the ACK to HELLO from the Peer 
                //store SENDER in connected peers 
                var connectedPeer = new PeerConnectionInfo
                {
                    PeerId = recvSenderId,
                    IsConnected = true,
                    Expiry = DateTime.UtcNow + TimeSpan.FromMilliseconds(heartbeatInterval * 3)
                };
                lock (connectedPeersLock)
                {
                    connectedPeersInfo.Add(recvSenderId, connectedPeer);
                }

                //raise the OnConnected Event
                RaiseOnConnected(new EventArgs());
            }
            else
            {
                //TODO:
                //undefined case for a client Persona
                //do nothing
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
            if ((isValidSender == true) &&
                (selfId != recvDestinationId))
            {
                //TODO:
                //if not route/forward it to other correponsing peer
                //undefined case for a client Persona
                //do nothing
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
