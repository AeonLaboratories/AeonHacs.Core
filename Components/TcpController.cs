using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AeonHacs.Components
{
    public class TcpClientManager
    {
        private TcpClient client;
        private NetworkStream stream;

        public bool Connected => client?.Connected ?? false;

        public async Task ConnectAsync(string host, int port)
        {
            try
            {
                client = new TcpClient();
                await client.ConnectAsync(host, port);
                stream = client.GetStream();
            }
            catch
            {
                client?.Dispose();
                stream = null;
            }
        }

        public async Task SendRequest(byte[] request)
        {
            if (stream == null) return;
                //throw new InvalidOperationException("Connection not established");
            await stream.WriteAsync(request.AsMemory(0, request.Length));
        }

        public async Task<byte[]> ReceiveResponse(int bufferSize)
        {
            var buffer = new byte[bufferSize];
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
            if (bytesRead == 0)
                throw new IOException("No data received.");

            Array.Resize(ref buffer, bytesRead);  // Resize buffer to actual bytes read
            return buffer;
        }

        public void Close()
        {
            stream?.Close();
            client?.Close();
        }
    }

    public class TcpController : HacsComponent
    {
        [HacsPostStart]
        protected virtual void PostStart()
        {
            updateThread = new Thread(UpdateLoop) { Name = $"{Name} Update", IsBackground = true };
            updateThread.Start();
        }

        [HacsStop]
        protected virtual void Stop()
        {
            stopping = true;
            Disconnect();
        }
        bool stopping = false;

        [JsonProperty("HostIP"), DefaultValue("192.168.111.222")]
        public string Host
        {
            get => host;
            set => Ensure(ref host, value);
        }
        string host = "192.168.111.222";

        [JsonProperty("TcpPort"), DefaultValue(502)]
        public int Port
        {
            get => port;
            set => Ensure(ref port, value);
        }
        int port = 502;

        private TcpClientManager client;

        public TcpController()
        {
            client = new TcpClientManager();
        }

        public TcpController(string host, int port) : this()
        {
            Host = host;
            Port = port;
        }

        public override bool Connected => client?.Connected ?? false;

        public async Task Connect() => await client.ConnectAsync(host, port);

        public async Task SendMessage(byte[] txBuffer) => await client.SendRequest(txBuffer);

        public async Task<byte[]> ReceiveMessage()
        {
            byte[] rxBuffer = [];
            if (Connected)
                rxBuffer = await client.ReceiveResponse(256);  // Assuming 256 as buffer size
            return rxBuffer;
        }

        public void Disconnect() => client?.Close();

        /// <summary>
        /// The assigned method provides a command message to the controller.
        /// </summary>
        public Func<byte[]> SelectService { get; set; }

        /// <summary>
        /// The assigned method receives a response message for processing.
        /// </summary>
        public Func<byte[], bool> ValidateResponse { get; set; }

        Thread updateThread;
        async void UpdateLoop()
        {
            while (!stopping)
            {
                if (!Connected) await Connect();
                await SendMessage(SelectService());
                ValidateResponse(await ReceiveMessage());
                Utility.WaitFor(() => stopping, IdleTimeout, 50);
            }
        }

        /// <summary>
        /// Milliseconds between requests for data from the device.
        /// </summary>
        [JsonProperty, DefaultValue(2000)]
        public int IdleTimeout
        {
            get => idleTimeout;
            set => Ensure(ref idleTimeout, value);
        }
        int idleTimeout = 2000;
    }
}
