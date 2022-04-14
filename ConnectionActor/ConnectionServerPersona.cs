using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

using NetMQ;
using NetMQ.Sockets;

namespace P2pCommunicationPoc
{
    internal class ConnectionServerPersona
    {
        string selfId = string.Empty;
        string peerId = string.Empty;

        ConnectionMessageLoop? receiveMessageLoop = new ConnectionMessageLoop();


        public ConnectionServerPersona()
        {
            receiveMessageLoop.OnConnected += OnPeerConnected;
            receiveMessageLoop.OnDisconnected += OnPeerDisconnected;
            receiveMessageLoop.OnReceiveMessage += OnReceivedMessageFromPeer;
        }

        public void Start(string selfId, string peerId)
        {
            this.selfId = selfId;
            this.peerId = peerId;

           
            receiveMessageLoop?.Start(selfId, peerId, false);
        }

        public void Stop()
        {
            receiveMessageLoop?.Stop();
        }

        public void SendMessage(byte[] data)
        {
            receiveMessageLoop.SendMessage(data);   
        }

        public event OnReceiveMessageHandler? OnReceiveMessage;

        void RaiseOnReceiveMessage(ReceiveMessageEventArgs e)
        {
            if (OnReceiveMessage != null)
                OnReceiveMessage(this, e);
        }

        public event EventHandler? OnConnected;

        void RaiseOnConnected(EventArgs e)
        {
            if (OnConnected != null)
                OnConnected(this, e);
        }

        public event EventHandler? OnDisconnected;

        void RaiseOnDisconnected(EventArgs e)
        {
            if (OnDisconnected != null)
                OnDisconnected(this, e);
        }

        void OnPeerConnected(object? sender, EventArgs e)
        {
            RaiseOnConnected(e);
        }

        void OnPeerDisconnected(object? sender, EventArgs e)
        {
            RaiseOnDisconnected(e);
        }

        void OnReceivedMessageFromPeer(object s, ReceiveMessageEventArgs e)
        {
            RaiseOnReceiveMessage(e);
        }

    }
}
