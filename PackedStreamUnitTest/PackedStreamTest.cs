using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO.Pipes;
using TLS;
using System.Threading;
using System.IO;
using System.Threading.Tasks;

namespace PackedStreamUnitTest
{
    [TestClass]
    public class PackedStreamTest
    {
        [TestMethod]
        public void SimpleMessageTest()
        {
            var pipeServer = new AnonymousPipeServerStream(PipeDirection.In);
            var pipeClient = new AnonymousPipeClientStream(PipeDirection.Out, pipeServer.ClientSafePipeHandle);
            try
            {
                var ps = new PackedStream(pipeServer, pipeClient);
                var rdn = new Random();
                var data = new byte[rdn.Next(10, 1024)];
                byte[] nData = null;
                var mre = new ManualResetEvent(false);
                rdn.NextBytes(data);

                ps.DataReceived += (s, d) =>
                {
                    nData = d.MemoryStream.ToArray();

                    mre.Set();
                };

                ps.Write(new MemoryStream(data));

                mre.WaitOne();

                Assert.AreEqual(data.Length, nData.Length);

                for (var i = 0; i < data.Length; i++)
                {
                    Assert.AreEqual(data[i], nData[i]);
                }
            }
            finally
            {
                pipeServer.Close();
                pipeClient.Close();
            }
        }

        [TestMethod]
        public void NoEventWatch()
        {
            using (var pipeServer = new AnonymousPipeServerStream(PipeDirection.In))
            using (var pipeClient = new AnonymousPipeClientStream(PipeDirection.Out, pipeServer.ClientSafePipeHandle))
            {
                var ps = new PackedStream(pipeServer, pipeClient);

                var data = new byte[] { 0, 1, 2 };
                ps.Write(new MemoryStream(data));
            }
        }

        [TestMethod]
        public void Disconnected()
        {
            var mre = new ManualResetEvent(false);
            using (var pipeServer = new AnonymousPipeServerStream(PipeDirection.In))
            using (var pipeClient = new AnonymousPipeClientStream(PipeDirection.Out, pipeServer.ClientSafePipeHandle))
            {
                var ps = new PackedStream(pipeServer, pipeClient);

                ps.Disconected += (s, e) => mre.Set();
            }

            if (!mre.WaitOne(1000))
            {
                Assert.Fail();
            }
        }

        [TestMethod]
        public async Task SimpleMessageTestAsync()
        {
            using (var pipeServer = new AnonymousPipeServerStream(PipeDirection.In))
            using (var pipeClient = new AnonymousPipeClientStream(PipeDirection.Out, pipeServer.ClientSafePipeHandle))
            {
                var ps = new PackedStream(pipeServer, pipeClient);
                var rdn = new Random();
                var data = new byte[rdn.Next(10, 1024)];
                byte[] nData = null;
                var mre = new ManualResetEvent(false);
                rdn.NextBytes(data);

                ps.DataReceived += (s, d) =>
                {
                    nData = d.MemoryStream.ToArray();

                    mre.Set();
                };

                await ps.WriteAsync(new MemoryStream(data));

                mre.WaitOne();

                Assert.AreEqual(data.Length, nData.Length);

                for (var i = 0; i < data.Length; i++)
                {
                    Assert.AreEqual(data[i], nData[i]);
                }
            }
        }
    }
}
