using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace P2pCommunicationPoc
{
    internal struct ConnectionPeerInfo
    {
        public string PeerId;

        public bool IsConnected;

        public DateTime Expiry;
    }
}
