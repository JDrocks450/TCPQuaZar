using QuazarAPI.Networking.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace QuazarAPI.Networking.Standard
{
    public enum ClientRecvStrategy
    {
        /// <summary>
        /// The <c>AwaitPacket</c> method is activated and will wait for a packet to be received and yield the result
        /// </summary>
        ASYNC_AWAIT,
        /// <summary>
        /// The <c>AwaitPacket</c> event is disabled and a background worker thread will wait for packets to come in and 
        /// raise the <c>OnPacketReceived</c> event
        /// </summary>
        EVENT_BASED
    }

    /// <summary>
    /// A wrapper for the <see cref="TcpClient"/> class
    /// </summary>
    public abstract class QuazarClient<T> : IDisposable where T : PacketBase, new()
    {
        /// <summary>
        /// Settings in relation to the <see cref="QuazarClient{T}"/> events
        /// </summary>
        public struct QuazarEventParameters
        {
            /// <summary>
            /// Enables or disables the packet parsing subsystem, which in turn enables or disables the <see cref="OnPacketReceived"/> event.
            /// </summary>
            public bool OnPacketReceivedEnabled;

            public static QuazarEventParameters Default => new QuazarEventParameters()
            {
                OnPacketReceivedEnabled = true
            };
        }
        public QuazarEventParameters EventParameters { get; set; } = QuazarEventParameters.Default;
        public bool IsDisposed { get; private set; }
        public bool IsConnected => _client != null && _client.Connected;
        protected TcpClient _client;
        protected Task _recvTask;
        protected Task _packetTask;
        private bool awaiterOverride = false;
        protected bool _recvStop = true;
        protected readonly List<ArraySegment<byte>> _recvQueue;
        private ManualResetEvent _recvInvoke, _recvEnqueuePause;
        private ClientRecvStrategy _strategy = ClientRecvStrategy.ASYNC_AWAIT;
        

        //EVENTS
        public event EventHandler<QEventArgs<Exception>> OnDisconnect;

        /// <summary>
        /// This changes the way the client interprets incoming Packets only.
        /// <para>Switching the strategy can cause certain packets to potentially be lost
        /// so switch it with caution and read the notes on each mode to pick the right one for the 
        /// current usecase</para>
        /// <para>Switching this mode submits a request which is dealt with as soon as the current task is complete.
        /// If it is awaiting a packet in <see cref="ClientRecvStrategy.EVENT_BASED"/>, the request to switch mode
        /// is not guaranteed to be handled until the next packet is received.</para> <see cref=""/>
        /// </summary>
        public ClientRecvStrategy Strategy
        {
            get => _strategy;
            set
            {
                if (_strategy == value) return;
                _strategy = value;

                //Manually cancel threads
                if (_packetTask.Status == TaskStatus.Running)
                {
                    awaiterOverride = true;
                    _recvInvoke.Set();
                    _packetTask.Wait();
                }

                switch (value)
                {
                    case ClientRecvStrategy.ASYNC_AWAIT:

                        break;
                    case ClientRecvStrategy.EVENT_BASED:
                        _packetTask = CreatePacketTask();
                        //_packetTask.Start();
                        break;
                }
            }
        }

        /// <summary>
        /// This method is called each time a packet is received and takes precidence over <see cref="AwaitPacket"/>
        /// <para>This will not fire unless the <see cref="Strategy"/> is set to <see cref="ClientRecvStrategy.EVENT_BASED"/></para>
        /// </summary>
        public event EventHandler<QEventArgs<T>> OnPacketReceived;
        /// <summary>
        /// This event is fired whenever new data is received and is called no matter what <see cref="Strategy"/> you are using.
        /// <para/>Note: This is called syncronously to ensure that any time-sensitive logic is handled in order it arrives in, you should do any long tasks on a background thread.
        /// </summary>
        public event EventHandler<QEventArgs<byte[]>> OnDataReceived;

        /// <summary>
        /// Initializes a new instance of the <see cref="QuazarClient{T}"/> class with the given <paramref name="Name"/>, <paramref name="Address"/>, and <paramref name="Port"/>
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="Address"></param>
        /// <param name="Port"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public QuazarClient(string Name, IPAddress Address, int Port)
        {
            if (Address is null)
            {
                throw new ArgumentNullException(nameof(Address));
            }

            this.Name = Name;
            this.Address = Address;
            this.Port = Port;
            _recvQueue = new List<ArraySegment<byte>>();
            _recvInvoke = new ManualResetEvent(false);
            _recvEnqueuePause = new ManualResetEvent(true);

            _client = new TcpClient()
            {
                LingerState = new LingerOption(true, 0),                
            };
            _packetTask = CreatePacketTask();
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="QuazarClient{T}"/> class with the given <paramref name="Name"/>, <paramref name="Address"/>, <paramref name="Port"/>, and <paramref name="quazarEventParameters"/>
        /// <para/>This will set <see cref="Strategy"/> to <see cref="ClientRecvStrategy.EVENT_BASED"/> and the <see cref="EventParameters"/> will dictate how the events are handled.
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="Address"></param>
        /// <param name="Port"></param>
        /// <param name="quazarEventParameters"></param>
        public QuazarClient(string Name, IPAddress Address, int Port, QuazarEventParameters quazarEventParameters) : this(Name, Address, Port)
        {
            this.EventParameters = quazarEventParameters;
            Strategy = ClientRecvStrategy.EVENT_BASED;
        }

        private Task CreatePacketTask()
        {
            return new Task(async delegate ()
            {
                while (Strategy == ClientRecvStrategy.EVENT_BASED)
                {
                    try
                    {
                        T packet = await AwaitPacket();
                        OnPacketReceived?.Invoke(this, new QEventArgs<T>() { Data = packet });
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                }
            });
        }

        public static uint Default_SendRecvAmt { get; set; } = 512;

        public string Name { get; }
        public IPAddress Address { get; }
        public int Port { get; }
        public uint ReceiveAmount => Default_SendRecvAmt;

        public async Task Connect()
        {
            await _client.ConnectAsync(Address, Port);
            _recvTask = new Task(StartReceiveAsync);
            _recvStop = false;
            _recvTask.Start();
            
            _packetTask.Start();
        }
        public Task<int> Send(byte[] Data)
        {
            try
            {
                return _client.Client.SendAsync(new ArraySegment<byte>(Data, 0, Data.Length), SocketFlags.None);
            }
            catch(SocketException e)
            {
                OnDisconnect?.DynamicInvoke(this, new QEventArgs<Exception>(e));
                return new Task<int>(() => { return 0; });
            }
        }
        public async Task SendPacket(T Packet) => await Send(Packet.GetBytes());
        public async Task SendPackets(params T[] Packets)
        {
            foreach (T packet in Packets)
                await SendPacket(packet);
        }

        private Task EnqueueData(byte[] Data, int Index = -1)
        {
            return Task.Run(delegate
            {
                _recvEnqueuePause.WaitOne();
                _recvEnqueuePause.Reset();
                var segment = new ArraySegment<byte>(Data);
                if (segment.Array == null)                
                    return;                
                if (Index == -1)
                    _recvQueue.Add(segment);
                else _recvQueue.Insert(Index, segment);                
                _recvEnqueuePause.Set();
                _recvInvoke.Set();
            });
        }

        public async Task<T> AwaitPacket()
        {
            void CHECK_CANCEL()
            {
                if (Strategy != ClientRecvStrategy.EVENT_BASED)
                    throw new TaskCanceledException("Strategy was changed from Event-Based");
            }

            var Data = await AwaitResponse();            
            using (MemoryStream networkData = new MemoryStream())
            {
                await networkData.WriteAsync(Data, 0, Data.Length);                    
                while (true)
                {
                    CHECK_CANCEL();
                    if (EventParameters.OnPacketReceivedEnabled == false)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                    try
                    {
                        var packet = PacketBase.Parse<T>(networkData.ToArray(), out int EndIndex);
                        networkData.Position = EndIndex;
                        byte[] remaining = new byte[networkData.Length - EndIndex];
                        if (remaining.Length > 0)
                        {
                            networkData.Read(remaining, 0, remaining.Length);
                            await EnqueueData(remaining, 0);
                        }                       
                        return packet;
                    }
                    catch(ArgumentException)
                    {
                        QConsole.WriteLine(Name, "Partial data received...");
                    }
                    catch (FormatException ex)
                    {
                        QConsole.WriteLine(Name, ex.ToString());
                    }
                    CHECK_CANCEL();
                    Data = await AwaitResponse();
                    await networkData.WriteAsync(Data, 0, Data.Length);
                }                
            }
        }

        public Task<byte[]> AwaitResponse()
        {
            return Task.Run(delegate
            {                
                while (true)
                {
                    if (!_recvQueue.Any())
                    {
                        _recvInvoke.WaitOne();
                        _recvInvoke.Reset();
                    }
                    if (awaiterOverride)
                    {
                        awaiterOverride = false;
                        throw new TaskCanceledException();
                    }
                    byte[] Data = _recvQueue.FirstOrDefault().Array;
                    if (Data == null) continue;
                    try
                    {
                        _recvQueue.RemoveAt(0);
                    }
                    catch { }
                    return Data;
                }
            });
        }

        private async void StartReceiveAsync()
        {
            while (!_recvStop)
            {
                try
                {
                    byte[] Data = await awaitData();
                    if (Strategy == ClientRecvStrategy.EVENT_BASED)
                        OnDataReceived?.Invoke(this, new(Data));
                    await EnqueueData(Data);
                }
                catch (SocketException e) // Connection Error
                {
                    QConsole.WriteLine(Name, "A connection error occured: " + e.Message);
                    break;
                }                
            }
        }
        private async Task<byte[]> awaitData()
        {
            ArraySegment<byte> segment = new ArraySegment<byte>(new byte[ReceiveAmount]);
            int readValue = await _client.Client.ReceiveAsync(segment, SocketFlags.None);
            byte[] data = segment.Array;
            Array.Resize(ref data, readValue);
            return data;
        }

        public async void Dispose()
        {
            if (IsDisposed) return;
            _recvStop = true;
            _recvTask?.Wait();

            //**safe close TCPClient
            await _client.Client.DisconnectAsync(false);
            _client.Close();
           
            _client.Dispose();
            IsDisposed = true;
        }
    }
}
