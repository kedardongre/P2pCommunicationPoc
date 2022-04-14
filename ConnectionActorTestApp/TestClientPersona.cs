using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

using NetMQ;
using NetMQ.Sockets;

namespace P2pCommunicationPoc.Tests
{
    internal class TestClientPersona
    {

        string actorId, testServerId;
        CancellationToken termiate;

        NetMQ.NetMQMessage? recvMessage;
        RouterSocket testServerSocket;

        ConnectionActor connectionActor;

        public TestClientPersona(string actorId, string testServerId, CancellationToken termiate)
        {
            this.actorId = actorId;
            this.testServerId = testServerId;
            this.termiate = termiate;

            recvMessage = null;
            testServerSocket = new RouterSocket();
            testServerSocket.Options.Identity = Encoding.UTF8.GetBytes(testServerId);
            testServerSocket.ReceiveReady += OnTestServerSocketReceiveReady;

            connectionActor = new ConnectionActor();
            connectionActor.OnReceiveMessage += OnConnectionActorReceivedMessage;
        }


        public void Run()
        {             
            Debug.Trace(testServerId, $"Starting Server: {testServerId}", ConsoleColor.White);
            var testServerEndpointsInfo = EndpointsHelper.GetConnectionEndpointsInfoFromAppConfig(testServerId);
            var testServerAddress = $"tcp://{testServerEndpointsInfo.LocalNetworkAddress}:{testServerEndpointsInfo.LocalNetworkPort}";
            testServerSocket.Bind(testServerAddress);

            Debug.Trace(testServerId, $"Starting actor: {actorId}", ConsoleColor.White);
            connectionActor.Start(actorId, testServerId);

            recvMessage = null;
            bool messageAvailable = false;

            int counter = 0;
            int dataMsgCounter = 0;
            while (true)
            {
                messageAvailable = testServerSocket.Poll(TimeSpan.FromMilliseconds(1000));
                if (termiate.IsCancellationRequested == true)
                {
                    Debug.Trace(testServerId, $"User requested task cancellation", ConsoleColor.Red);
                    break;
                }

                if (!messageAvailable)
                {
                    counter++;

                    //send heartbeats
                    if (counter % 5 == 0)
                    {
                        Debug.Trace(testServerId, $"Sending HEARTBEAT  message to actor: {actorId}", ConsoleColor.White);
                        var heartbeatMessage = MessageHelper.CreateHeartbeatMessage(testServerId, actorId, string.Empty);
                        MessageHelper.WrapRoutingFrames(heartbeatMessage, actorId);
                        testServerSocket.SendMultipartMessage(heartbeatMessage);
                    }
                    //send some data
                    if (counter % 13 == 0)
                    {
                        Debug.Trace(testServerId, $"Sending DATA message actor: {actorId}", ConsoleColor.White);
                        var dataMessage = MessageHelper.CreateDataMessage(testServerId, actorId, string.Empty, Encoding.UTF8.GetBytes($"GREETINGS ... {dataMsgCounter++} !!!"));
                        MessageHelper.WrapRoutingFrames(dataMessage, actorId);
                        testServerSocket.SendMultipartMessage(dataMessage);
                    }
                }
                else
                {
                    //recvMessage = [sender id][e][destination ID][source ID][auth token][message type][data]
                    var recvSenderId = MessageHelper.UnwrapRoutingFrames(recvMessage);
                    //recvMessage = [e][destination ID][source ID][auth token][message type][data]

                    MessageHelper.ReadMessageEnvelope(recvMessage,
                        out string recvSourceId,
                        out string recvDestinationId,
                        out string recvAuthorizationToken,
                        out MessageType recvMessageType);

                    switch (recvMessageType)
                    {
                        case MessageType.HELLO:
                            CheckHelloMessageIsValid(recvSenderId, recvDestinationId);
                            break;
                        case MessageType.HEARTBEAT:
                            CheckHeartbeatMessageIsValid(recvSenderId, recvDestinationId);
                            break;
                        case MessageType.DATA:
                            var data = recvMessage.Pop().ToByteArray();
                            CheckDataMessageIsValid(recvSenderId, recvDestinationId, data);
                            break;
                        case MessageType.BYE:
                            break;
                        default:
                            break;
                    }
                }
            }
            Debug.Trace(testServerId, $"User requested task cancellation", ConsoleColor.White);
            connectionActor.Stop();
        }

        void OnTestServerSocketReceiveReady(object? sender, NetMQSocketEventArgs e)
        {
            recvMessage = e.Socket.ReceiveMultipartMessage();
        }

        void CheckHelloMessageIsValid(string recvSenderId, string recvDestinationId)
        {
            if ((recvSenderId.Length > 0) &&
                (recvDestinationId.Length > 0) &&
                (recvSenderId == actorId) &&
                (recvDestinationId == testServerId))
            {
                Debug.Trace(testServerId, $"Received HELLO from actor {recvSenderId}", ConsoleColor.White);
                var helloMessage = MessageHelper.CreateHelloMessage(testServerId, actorId, string.Empty);
                MessageHelper.WrapRoutingFrames(helloMessage, actorId);
                testServerSocket.SendMultipartMessage(helloMessage);
            }
            else
                Debug.Trace(testServerId, $"Received Incorrect HELLO, expected sender {actorId} recieved {recvSenderId}, expected destination {testServerId} was {recvDestinationId}", ConsoleColor.Red);
        }


        void CheckHeartbeatMessageIsValid(string recvSenderId, string recvDestinationId)
        {
            if ((recvSenderId.Length > 0) &&
                (recvDestinationId.Length > 0) &&
                (recvSenderId == actorId) &&
                (recvDestinationId == testServerId))
            {
                Debug.Trace(testServerId, $"Received HEARTBEAT from actor {recvSenderId}", ConsoleColor.White);
            }
            else
            {
                Debug.Trace(testServerId, $"Received Incorrect HEARTBEAT, expected sender {actorId} recieved {recvSenderId}, expected destination {testServerId} was {recvDestinationId}", ConsoleColor.Red);
            }
        }

        void CheckDataMessageIsValid(string recvSenderId, string recvDestinationId, byte[] data)
        {
            if ((recvSenderId.Length > 0) &&
                (recvDestinationId.Length > 0) &&
                (recvSenderId == actorId) &&
                (recvDestinationId == testServerId))
            {
                var dataValue = Encoding.UTF8.GetString(data);
                Debug.Trace(testServerId, $"Received DATA from actor {recvSenderId}, contents: {dataValue}", ConsoleColor.White);
            }
            else
                Debug.Trace(testServerId, $"Received Incorrect DATA, expected sender {actorId} recieved {recvSenderId}, expected destination {testServerId} was {recvDestinationId}", ConsoleColor.Red);
        }

        void OnConnectionActorReceivedMessage(object s, ReceiveMessageEventArgs e)
        {
            if (e.MessageType != MessageType.DATA)
                Debug.Trace(actorId, $"Actor received {e.MessageType} from peer {e.SenderId}", ConsoleColor.Yellow);
            else
            {
                var msgContents = Encoding.UTF8.GetString(e.Data);
                Debug.Trace(actorId, $"Actor received {e.MessageType} from peer {e.SenderId}, contents: {msgContents}", ConsoleColor.Yellow);

                var echoMessage = $"THANKS FOR THE MESSAGE - {msgContents}";
                Debug.Trace(testServerId, $"Asking actor {actorId} to echo the message back to peer", ConsoleColor.White);
                connectionActor.SendMessage(Encoding.UTF8.GetBytes(echoMessage));
            }
        }
    }
}
