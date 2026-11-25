using System;
using System.Threading.Tasks;
using AeroSpot.Core.IPC;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroSpot.Tests
{
    [TestClass]
    public class NamedPipeIntegrationTests
    {
        [TestMethod]
        public async Task NamedPipe_BidirectionalCommunication_Success()
        {
            string pipeName = $"AeroSpot_TestPipe_{Guid.NewGuid():N}";

            using var server = new NamedPipeHostServer(pipeName);
            using var client = new NamedPipeWorkerClient(pipeName);

            var serverReceivedTcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var clientReceivedTcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            server.MessageReceived += msg => serverReceivedTcs.TrySetResult(msg);
            client.MessageReceived += msg => clientReceivedTcs.TrySetResult(msg);

            server.Start();

            bool connected = await client.ConnectAsync(3000);
            Assert.IsTrue(connected, "Client failed to connect to pipe server.");

            // 1. Client -> Server
            var clientMsg = new IpcMessage(IpcMessageType.WorkerReady, "PID=1234");
            bool sentByClient = await client.SendMessageAsync(clientMsg);
            Assert.IsTrue(sentByClient, "Client failed to send message.");

            var receivedByServer = await Task.WhenAny(serverReceivedTcs.Task, Task.Delay(3000));
            Assert.AreEqual(serverReceivedTcs.Task, receivedByServer, "Server did not receive message in time.");
            Assert.AreEqual(IpcMessageType.WorkerReady, serverReceivedTcs.Task.Result.Type);
            Assert.AreEqual("PID=1234", serverReceivedTcs.Task.Result.Payload);

            // 2. Server -> Client
            var serverMsg = new IpcMessage(IpcMessageType.ShowLauncher, "TestContext");
            bool sentByServer = await server.SendMessageAsync(serverMsg);
            Assert.IsTrue(sentByServer, "Server failed to send message.");

            var receivedByClient = await Task.WhenAny(clientReceivedTcs.Task, Task.Delay(3000));
            Assert.AreEqual(clientReceivedTcs.Task, receivedByClient, "Client did not receive message in time.");
            Assert.AreEqual(IpcMessageType.ShowLauncher, clientReceivedTcs.Task.Result.Type);
            Assert.AreEqual("TestContext", clientReceivedTcs.Task.Result.Payload);
        }

        [TestMethod]
        public async Task NamedPipe_ClientDisconnect_ServerResetsAndAcceptsNewConnection()
        {
            string pipeName = $"AeroSpot_TestPipe_Reconnect_{Guid.NewGuid():N}";

            using var server = new NamedPipeHostServer(pipeName);
            var firstConnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstDisconnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondConnectedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            int connectionCount = 0;
            server.ClientConnected += () =>
            {
                int count = System.Threading.Interlocked.Increment(ref connectionCount);
                if (count == 1) firstConnectedTcs.TrySetResult(true);
                else if (count == 2) secondConnectedTcs.TrySetResult(true);
            };
            server.ClientDisconnected += () => firstDisconnectedTcs.TrySetResult(true);
            server.Start();

            // First client connects and disconnects
            using (var client1 = new NamedPipeWorkerClient(pipeName))
            {
                bool connected = await client1.ConnectAsync(3000);
                Assert.IsTrue(connected);
                await Task.WhenAny(firstConnectedTcs.Task, Task.Delay(3000));
                client1.Disconnect();
            }

            var disconnectedDone = await Task.WhenAny(firstDisconnectedTcs.Task, Task.Delay(3000));
            Assert.AreEqual(firstDisconnectedTcs.Task, disconnectedDone, "Server did not observe first client disconnect in time.");

            // Second client connects (poll ConnectAsync briefly if server loop is mid-recreate)
            var secondClientReceivedTcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var client2 = new NamedPipeWorkerClient(pipeName))
            {
                client2.MessageReceived += msg => secondClientReceivedTcs.TrySetResult(msg);

                bool connected = false;
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (!connected && DateTime.UtcNow < deadline)
                {
                    connected = await client2.ConnectAsync(500);
                }
                Assert.IsTrue(connected, "Second client failed to connect after first client disconnected.");

                await Task.WhenAny(secondConnectedTcs.Task, Task.Delay(3000));
                bool sent = await server.SendMessageAsync(new IpcMessage(IpcMessageType.Ping));
                Assert.IsTrue(sent);

                var completed = await Task.WhenAny(secondClientReceivedTcs.Task, Task.Delay(3000));
                Assert.AreEqual(secondClientReceivedTcs.Task, completed);
                Assert.AreEqual(IpcMessageType.Ping, secondClientReceivedTcs.Task.Result.Type);
            }
        }
    }
}
