using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace P2pCommunicationPoc
{
    public class ReceiveMessageEventArgs
    {
        public string SenderId { get; private set; }

        public MessageType MessageType { get; private set; }

        public byte[] Data { get; private set; }

        public ReceiveMessageEventArgs(string senderId, MessageType messageType, byte[] data)
        {
            SenderId = senderId;
            MessageType = messageType;
            Data = data;
        }
    }

    public delegate void OnReceiveMessageHandler(object s, ReceiveMessageEventArgs e);

}
