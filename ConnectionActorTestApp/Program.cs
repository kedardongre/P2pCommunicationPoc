using P2pCommunicationPoc;
using P2pCommunicationPoc.Tests;

Console.Clear();

var testServerTaskCts = new CancellationTokenSource();
var testClientTaskCts = new CancellationTokenSource();
var testP2pTaskCts = new CancellationTokenSource();

//var testServerActorSelfId = "SERVER_PERSONA_INST";
//var testServerActorPeerId = "TEST_SERVER_PERSONA";

//var testServerActor = new TestServerActor(testServerActorSelfId, testServerActorPeerId, testServerTaskCts.Token);
//var testServerActorTask = Task.Factory.StartNew(testServerActor.Run);

var testClientPersonaSelfId = "CLIENT_PERSONA_INST";
var testClientPersonaPeerId = "TEST_CLIENT_PERSONA";

var testClientPersona = new TestClientPersona(testClientPersonaSelfId, testClientPersonaPeerId, testClientTaskCts.Token);
var testClientPersonaTask = Task.Factory.StartNew(testClientPersona.Run);

while (true)
{
    var input = Console.ReadKey();
    if (input.Key == ConsoleKey.X)
    {
        testServerTaskCts.Cancel();
        testClientTaskCts.Cancel();
        testP2pTaskCts.Cancel();
        break;
    }
}

//testServerActorTask.Wait();
testClientPersonaTask.Wait();



