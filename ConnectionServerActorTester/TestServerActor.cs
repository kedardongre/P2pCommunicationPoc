using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConnectionServerActorTester
{
    internal class TestServerActor
    {

        
using P2pCommunicationPoc;

Console.Clear();


string selfId = "SELF_INST";
    string peerId = "PEER_INST";

    Console.WriteLine("STARTING CONNECTION ACTOR FOR: " + selfId);
var connectionActor = new ConnectionActor();
    connectionActor.Start(selfId, peerId);

connectionActor.OnRecieveMessage += OnRecieveMessage;

while (true)
{
    var input = Console.ReadKey();
    if (input == ConsoleKey.X)
        break;

}
connectionActor.Stop();


void OnRecieveMessage(object s, RecieveMessageEventArgs e)
{
    throw new NotImplementedException();
}
    }
}
