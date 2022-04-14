using System;
using System.Net;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;

using P2pCommunicationPoc;
using NetMQ.Sockets;
using NetMQ;

namespace P2pCommunicationPoc.Tests
{
    internal class TestServerActor
    {
        string actorId, testClientId;
        CancellationToken termiate;

        NetMQ.NetMQMessage? recvMessage;
        RouterSocket testClientSocket;

        ConnectionActor connectionActor;


        public TestServerActor(string actorId, string testClientId, CancellationToken termiate)
        {
            this.actorId = actorId;
            this.testClientId = testClientId;
            this.termiate = termiate;

            recvMessage = null;
            testClientSocket = new RouterSocket();
            testClientSocket.Options.Identity = Encoding.UTF8.GetBytes(testClientId);
            //testClientSocket.Options.RouterMandatory = true;
            testClientSocket.ReceiveReady += OnTestClientSocketReceiveReady;

            connectionActor = new ConnectionActor();
            connectionActor.OnReceiveMessage += OnConnectionActorReceivedMessage;
        }

        public void Run()
        {            
            Debug.Trace(testClientId, $"Starting actor: {actorId}", ConsoleColor.White);        
            connectionActor.Start(actorId, testClientId);            

            Debug.Trace(testClientId, $"Connecting to actor: {actorId}", ConsoleColor.White);
            var serverActorEndpointsInfo = EndpointsHelper.GetConnectionEndpointsInfoFromAppConfig(actorId);
            var serverActorAddress = $"tcp://{serverActorEndpointsInfo.LocalNetworkAddress}:{serverActorEndpointsInfo.LocalNetworkPort}";
            testClientSocket.Connect(serverActorAddress);

            //Send Hello
            Debug.Trace(testClientId, $"Sending HELLO message to actor: {actorId}", ConsoleColor.White);
            var helloMsg = MessageHelper.CreateHelloMessage(testClientId, actorId, string.Empty);
            MessageHelper.WrapRoutingFrames(helloMsg, actorId);
            testClientSocket.SendMultipartMessage(helloMsg);

            recvMessage = null;
            bool messageAvailable = false;

            int counter = 0;
            int dataMsgCounter = 0;
            bool helloAckReceived = false;
            while (true)
            {
                messageAvailable = testClientSocket.Poll(TimeSpan.FromMilliseconds(1000));
                if (termiate.IsCancellationRequested == true)
                {
                    Debug.Trace(testClientId, $"User requested task cancellation", ConsoleColor.Red);
                    break;
                }

                if (!messageAvailable) 
                {
                    if (helloAckReceived == false)
                    {
                        Debug.Trace(testClientId, $"Sending HELLO message to actor: {actorId}", ConsoleColor.White);
                        testClientSocket.SendMultipartMessage(helloMsg);
                    }
                    else
                    {
                        counter++;

                        //send heartbeats
                        if (counter % 5 == 0)
                        {
                            Debug.Trace(testClientId, $"Resending HEARTBEAT  message to actor: {actorId}", ConsoleColor.White);
                            var heartbeatMessage = MessageHelper.CreateHeartbeatMessage(testClientId, actorId, string.Empty);
                            MessageHelper.WrapRoutingFrames(heartbeatMessage, actorId);
                            testClientSocket.SendMultipartMessage(heartbeatMessage);
                        }
                        //send some data
                        if (counter % 13 == 0)
                        {
                            Debug.Trace(testClientId, $"Sending DATA message actor: {actorId}", ConsoleColor.White);
                            var dataMessage = MessageHelper.CreateDataMessage(testClientId, actorId, string.Empty, Encoding.UTF8.GetBytes($"GREETINGS ... {dataMsgCounter++} !!!"));
                            MessageHelper.WrapRoutingFrames(dataMessage, actorId);
                            testClientSocket.SendMultipartMessage(dataMessage);
                        }
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
                            helloAckReceived = true;
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
            Debug.Trace(testClientId, $"User requested task cancellation", ConsoleColor.White);
            connectionActor.Stop();
        }

        void OnTestClientSocketReceiveReady(object? sender, NetMQSocketEventArgs e)
        {
            recvMessage = e.Socket.ReceiveMultipartMessage();
        }

        void CheckHelloMessageIsValid(string recvSenderId, string recvDestinationId)
        {
            if ((recvSenderId.Length > 0) &&
                (recvDestinationId.Length > 0) &&
                (recvSenderId == actorId) &&
                (recvDestinationId == testClientId))
            {
                Debug.Trace(testClientId, $"Received HELLO from actor {recvSenderId}", ConsoleColor.White);
            }
            else
                Debug.Trace(testClientId, $"Received Incorrect HELLO, expected sender {actorId} recieved {recvSenderId}, expected destination {testClientId} was {recvDestinationId}", ConsoleColor.Red);
        }


        void CheckHeartbeatMessageIsValid(string recvSenderId, string recvDestinationId)
        {
            if ((recvSenderId.Length > 0) &&
                (recvDestinationId.Length > 0) &&
                (recvSenderId == actorId) &&
                (recvDestinationId == testClientId))
            {
                Debug.Trace(testClientId, $"Received HEARTBEAT from actor {recvSenderId}", ConsoleColor.White);
            }
            else
            {
                Debug.Trace(testClientId, $"Received Incorrect HEARTBEAT, expected sender {actorId} recieved {recvSenderId}, expected destination {testClientId} was {recvDestinationId}", ConsoleColor.Red);
            }
        }

        void CheckDataMessageIsValid(string recvSenderId, string recvDestinationId, byte[] data)
        {
            if ((recvSenderId.Length > 0) &&
                (recvDestinationId.Length > 0) &&
                (recvSenderId == actorId) &&
                (recvDestinationId == testClientId))
            {
                var dataValue = Encoding.UTF8.GetString(data);
                Debug.Trace(testClientId, $"Received DATA from actor {recvSenderId}, contents: {dataValue}", ConsoleColor.White);
            }
            else
                Debug.Trace(testClientId, $"Received Incorrect DATA, expected sender {actorId} recieved {recvSenderId}, expected destination {testClientId} was {recvDestinationId}", ConsoleColor.Red);
        }

        void OnConnectionActorReceivedMessage(object s, ReceiveMessageEventArgs e)
        {
            if (e.MessageType != MessageType.DATA)
            {
                Debug.Trace(actorId, $"Actor received {e.MessageType} from peer {e.SenderId}", ConsoleColor.Yellow);
            }
            else
            {
                var msgContents = Encoding.UTF8.GetString(e.Data);
                Debug.Trace(actorId, $"Actor received {e.MessageType} from peer {e.SenderId}, contents: {msgContents}", ConsoleColor.Yellow);

                //ask the actor to echo the message back
                var echoMessage = $"THANKS FOR THE MESSAGE - {msgContents}";
                Debug.Trace(testClientId, $"Asking actor {actorId} to echo the message back to peer", ConsoleColor.White);
                connectionActor.SendMessage(Encoding.UTF8.GetBytes(echoMessage));
            }
        }

    }
}
