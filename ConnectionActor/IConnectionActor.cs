using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace P2pCommunicationPoc
{
    public interface IConnectionActor
    {
        void Start(string selfId, string peerId);
        void Stop();
        void SendMessage(byte[] message);

        event EventHandler OnConnected;

        event EventHandler OnDisconnected;

        event OnRecieveMessageHandler OnRecieveMessage;
    }
}
