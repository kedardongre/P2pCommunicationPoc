using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using NetMQ;

namespace P2pCommunicationPoc
{
    public enum MessageType
    {
        UNKNOWN = 0x00,
        HELLO = 0x01,
        HEARTBEAT = 0x02,
        DATA = 0x03,
        BYE = 0x04
    }

    public class MessageHelper
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// HELLO message format
        /// [empty][destination ID][source ID][auth token][hello][empty]
        /// </remarks>
        public static NetMQMessage CreateHelloMessage(string sourceId, string destinationId, string authorizationToken)
        {
            NetMQMessage message = new NetMQMessage();

            //body
            message.Push(NetMQFrame.Empty);
            //message type
            message.Push(new[] { (byte)MessageType.HELLO });
            //authorization token
            message.Push(authorizationToken);
            //source Id
            message.Push(sourceId);
            //destination Id
            message.Push(destinationId);
            //empty
            message.Push(NetMQFrame.Empty);

            return message;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="senderId"></param>
        /// <param name="destinationId"></param>
        /// <param name="authorizationToken"></param>
        /// <param name=""></param>
        /// <remarks>
        /// HEARTBEAT message formats
        /// [empty][destination ID][source ID][auth token][heartbeat][empty]
        /// </remarks>
        /// <returns></returns>
        public static NetMQMessage CreateHeartbeatMessage(string sourceId, string destinationId, string authorizationToken)
        {
            NetMQMessage message = new NetMQMessage();

            //body
            message.Push(NetMQFrame.Empty);
            //message type
            message.Push(new[] { (byte)MessageType.HEARTBEAT });
            //authorization token
            message.Push(authorizationToken);
            //source Id
            message.Push(sourceId);
            //destination Id
            message.Push(destinationId);
            //empty
            message.Push(NetMQFrame.Empty);

            return message;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="senderId"></param>
        /// <param name="destinationId"></param>
        /// <param name="sourceId"></param>
        /// <param name="authorizationToken"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        /// <remarks>
        /// DATA message format
        /// [empty][destination ID][source ID][auth token][message type][data]
        /// </remarks>
        public static NetMQMessage CreateDataMessage(string sourceId, string destinationId, string authorizationToken, byte[] data)
        {
            NetMQMessage message = new NetMQMessage();

            //body
            message.Push(data);
            //message type
            message.Push(new[] { (byte)MessageType.DATA });
            //authorization token
            message.Push(authorizationToken);
            //source Id
            message.Push(sourceId);
            //destination Id
            message.Push(destinationId);
            //empty
            message.Push(NetMQFrame.Empty);

            return message;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="senderId"></param>
        /// <param name="destinationId"></param>
        /// <param name="sourceId"></param>
        /// <param name="authorizationToken"></param>
        /// <param name="messageType"></param>
        /// Incoming message format
        /// [empty][destination ID][source ID][auth token][message type][data]
        /// </remarks>

        public static void ReadMessageEnvelope(NetMQMessage message,
            out string sourceId,
            out string destinationId,
            out string authorizationToken,
            out MessageType messageType)
        {
            sourceId = String.Empty;
            destinationId = String.Empty;
            authorizationToken = String.Empty;
            messageType = MessageType.UNKNOWN;

            if (message != null)
            {
                if (message.FrameCount >= 5)
                {
                    ////TODO: check autoadded senderId == senderID within the message
                    ////check to see the received message from the backend socket
                    //if (message.First.IsEmpty == false)
                    //{
                    //    //[backend socket sender id][e][sender ID][destination ID][source ID][auth token][message type][data]
                    //    message.Pop().ConvertToString();
                    //}

                    //[e][sender ID][destination ID][source ID][auth token][message type][data]
                    //FRAME 0: EMPTY
                    message.Pop();
                    //[destination ID][source ID][auth token][message type][data]
                    //FRAME 1: DESTINATION
                    destinationId = message.Pop().ConvertToString();
                    //[source ID][auth token][message type][data]
                    //FRAME 2: SOURCE
                    sourceId = message.Pop().ConvertToString();
                    //[auth token][message type][data]
                    //FRAME 3: AUTHORIZATION_TOKEN
                    authorizationToken = message.Pop().ConvertToString();
                    //[message type][data]
                    //FRAME 4: MESSAGE_TYPE (HELLO, DATA, HEARTBEAT, BYE) 
                    var messgeTypeFrame = message.Pop();
                    if (messgeTypeFrame.BufferSize == 1)
                        messageType = (MessageType) messgeTypeFrame.Buffer[0];
                }
            }
        }

        public static string UnwrapRoutingFrames(NetMQMessage message)
        {
            //input message
            //[sender id][empty][destination ID][source ID][auth token][MESSAGE_TYPE][MESSAGE_DATA]

            //remove the sender ID
            var senderId = message.Pop().ConvertToString();
            //[empty][destination ID][source ID][auth token][MESSAGE_TYPE][MESSAGE_DATA]

            return senderId;
        }

        public static void WrapRoutingFrames(NetMQMessage message, string destinationId)
        {
            //input message
            //[empty][destination ID][source ID][auth token][MESSAGE_TYPE][MESSAGE_DATA]

            //append the target address for routing
            message.Push(destinationId);

            //result message
            //[destination ID][empty][destination ID][source ID][auth token][MESSAGE_TYPE][MESSAGE_DATA]
        }
    }
}
