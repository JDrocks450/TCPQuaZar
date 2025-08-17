using OpenSSL.SSL;
using OpenSSL.X509;
using QuazarAPI.Networking.Data;
using QuazarAPI.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

namespace QuazarAPI.Networking.Standard
{
    public abstract class QuazarServer<T> where T : PacketBase, new()
    {
        public const int DefaultSendAmt = 256;
        public const int DEFAULT_MAX_CONNECTIONS = 100;

        /// <summary>
        /// Server settings for a <see cref="QuazarServer{T}"/> instance
        /// </summary>
        public class QuazarServerSettings
        {       
            /// <summary>
            /// Configures the <see cref="QuazarServer{T}"/> as a Tcp Server without SSL
            /// </summary>
            /// <param name="Name"></param>
            /// <param name="ListenIP"></param>
            /// <param name="Port"></param>
            /// <param name="MaxConcurrentConnections"></param>
            /// <exception cref="ArgumentNullException"></exception>
            public QuazarServerSettings(string Name, IPAddress ListenIP, int Port, uint MaxConcurrentConnections = DEFAULT_MAX_CONNECTIONS)
            {
                if (Name == null)
                    throw new ArgumentNullException(nameof(Name));
                if (ListenIP == null)
                    throw new ArgumentNullException(nameof(ListenIP));
                this.Name = Name;
                this.ListenIP = ListenIP;
                this.Port = Port;
                this.MaxConcurrentConnections = MaxConcurrentConnections;
            }
            /// <summary>
            /// Configures the <see cref="QuazarServer{T}"/> as a Tcp Server with SSL enabled and the authenticates using the provided <paramref name="SSLChain"/>
            /// </summary>
            /// <param name="Name"></param>
            /// <param name="ListenIP"></param>
            /// <param name="Port"></param>
            /// <param name="SSLChain"></param>
            /// <param name="MaxConcurrentConnections"></param>
            public QuazarServerSettings(string Name, IPAddress ListenIP, int Port, X509Certificate ServerCertificate, uint MaxConcurrentConnections = DEFAULT_MAX_CONNECTIONS, X509Chain ClientCertificates = default) :
                this(Name,ListenIP,Port,MaxConcurrentConnections)
            {
                this.ClientChain = ClientCertificates;
                this.ServerCertificate = ServerCertificate;
            }

            /// <summary>
            /// The name of this <see cref="QuazarServer"/>
            /// </summary>
            public string Name { get; set; } = nameof(QuazarServer<T>);
            public IPAddress ListenIP { get; }
            public int Port { get; }
            public uint MaxConcurrentConnections { get; set; }

            /// <summary>
            /// The size of the buffer that will be allocated for each client connection to Voltron for receiving packets.
            /// </summary>
            public int ReceiveAmount { get; set; } = DefaultSendAmt;
            /// <summary>
            /// The size of the buffer that will be allocated for each client connection to Voltron for sending packets.
            /// </summary>
            public int SendAmount { get; set; } = DefaultSendAmt;
            /// <summary>
            /// Gets (or sets) whether or not to store outgoing/incoming packets to the <see cref="OutgoingTrans"/> and <see cref="IncomingTrans"/> collections
            /// </summary>
            public bool CachePackets
            {
                get;
                set;
            } = 
#if DEBUG
            true;
#else
            false;
#endif
            /// <summary>
            /// All packets sent through <see cref="Send"/> functions will be ignored except when sent from  
            /// </summary>
            public bool ManualMode
            {
                get; set;
            } = false;
            /// <summary>
            /// Disposes the packet passed to an overload of the function: <see cref="Send(uint, T[])"/>
            /// <para>If <see cref="CachePackets"/> is <see langword="true"/> this will have no effect as the packets 
            /// cannot be safely disposed as they are still referenced elsewhere</para>
            /// </summary>
            public bool DisposePacketOnSent { get; set; } = true;

            /// <summary>
            /// Dictates whether this <see cref="QuazarServer{T}"/> authenticates clients using SSL
            /// </summary>
            public bool UseSSL => ServerCertificate != default;
            /// <summary>
            /// The certificate to use for this server when <see cref="UseSSL"/> is enabled
            /// </summary>
            public X509Certificate ServerCertificate { get; set; }
            /// <summary>
            /// Optionally, you can specify an <see cref="X509Chain"/> for client authentication
            /// </summary>
            public X509Chain ClientChain { get; set; }

            /// <summary>
            /// Gets whether the server has a <see cref="TcpListener"/> initialized
            /// </summary>
            public bool IsInitialized => ServerListener != null;
            internal TcpListener ServerListener { get; set; } = null;
        }
        /// <summary>
        /// The current settings for this <see cref="QuazarServer"/>
        /// </summary>
        public QuazarServerSettings Settings { get; }

        /// <summary>
        /// <inheritdoc cref="QuazarServer{T}.QuazarServerSettings.Name"/>
        /// </summary>
        public string Name
        {
            get => Settings.Name; set => Settings.Name = value;
        }

        /// <summary>
        /// <inheritdoc cref="QuazarServerSettings.ReceiveAmount"/>
        /// </summary>
        public int ReceiveAmount => Settings.ReceiveAmount;
        /// <summary>
        /// <inheritdoc cref="QuazarServerSettings.SendAmount"/>
        /// </summary>
        public int SendAmount => Settings.SendAmount;

        /// <summary>
        /// The port of this <see cref="TcpListener"/>
        /// </summary>
        public int PORT => Settings.Port;

        /// <summary>
        /// The amount of connections accepted.
        /// </summary>
        public uint BACKLOG { get => Settings.MaxConcurrentConnections; set => Settings.MaxConcurrentConnections = value; }

        /// <summary>
        /// All packets sent through <see cref="Send"/> functions will be ignored except when sent from  
        /// </summary>
        public bool ManualMode
        {
            get; set;
        } = false;

        /// <summary>
        /// Gets the assembly info of the nio2so Voltron Protocol used by this server.
        /// </summary>
        public static Assembly QuaZarProtocolAssembly => typeof(PacketBase).Assembly;
        /// <summary>
        /// Gets the version of the nio2so Voltron Protocol used by this server.
        /// </summary>
        public static FileVersionInfo QuaZarProtocolVersion => FileVersionInfo.GetVersionInfo(QuaZarProtocolAssembly.Location);

        protected TcpListener listener { get => Settings.ServerListener; set => Settings.ServerListener = value; }
        protected Dictionary<uint, TcpClient> _clients = new Dictionary<uint, TcpClient>();
        protected Dictionary<uint, ClientInfo> _clientInfo = new Dictionary<uint, ClientInfo>();

        public ObservableCollection<T> IncomingTrans = new ObservableCollection<T>(),
                                            OutgoingTrans = new ObservableCollection<T>();
        public ObservableCollection<ClientInfo> ConnectionHistory = new ObservableCollection<ClientInfo>();
        protected Socket Host => listener.Server;
        protected IDictionary<uint, TcpClient> Connections => _clients;

        protected Queue<(uint ID, byte[] Buffer)> SendQueue = new Queue<(uint ID, byte[] Buffer)>();
        protected Thread SendThread;
        protected ManualResetEvent SendThreadInvoke;

        /// <summary>
        /// <inheritdoc cref="QuazarServer{T}.QuazarServerSettings.DisposePacketOnSent"/>/>
        /// </summary>
        protected bool DisposePacketOnSent { get; set; } = true;
        /// <summary>
        /// <inheritdoc cref="QuazarServer{T}.QuazarServerSettings.CachePackets"/>
        /// </summary>
        protected bool CachePackets { get => Settings.CachePackets; set => Settings.CachePackets = value; }

        public IPAddress MyIP => Settings.ListenIP;

        //**EVENTS        
        public delegate void QuazarClientInfoHandler(uint ClientID, ClientInfo Info);
        public delegate void QuazarDataHandler(uint ClientID, T Packet);
        public event QuazarDataHandler OnIncomingPacket, OnOutgoingPacket;
        public event QuazarClientInfoHandler OnConnectionsUpdated;
        //**        

        /// <summary>
        /// Creates a <see cref="QuazarServer{T}"/> with the specified parameters.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="port"></param>
        /// <param name="backlog"></param>
        [Obsolete]
        protected QuazarServer(string name, int port, uint backlog = DEFAULT_MAX_CONNECTIONS, IPAddress ListenIP = default) : 
            this(new QuazarServerSettings(name, ListenIP ?? IPAddress.Loopback, port, backlog))
        {            

        }
        /// <summary>
        /// Creates a <see cref="QuazarServer{T}"/> with the specified <see cref="QuazarServerSettings"/>
        /// </summary>
        /// <param name="settings"></param>
        protected QuazarServer(QuazarServerSettings settings)
        {            
            Settings = settings;
            QConsole.WriteLine(Name, $"Server object created. Name: {Name} Port: {PORT} IP: {MyIP}");
            Init();
        }

        protected virtual void Init()
        {
            SslStream.USE_SNI = false;

            listener = new TcpListener(MyIP, PORT);            
            Host.SendBufferSize = SendAmount;
            Host.ReceiveBufferSize = ReceiveAmount;
            Host.NoDelay = true; // disable Nagle's algorithm
            Host.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false); // do not allow multiple servers on the same address:port            

            QConsole.WriteLine(Name, $"Server object init complete. IP: {MyIP} Port: {PORT}");

            SendThreadInvoke = new ManualResetEvent(false);
            SendThread = new Thread(doSendLoop);
            SendThread.Start();            
        }

        private void doSendLoop()
        {
            QConsole.WriteLine(Name, $"New Thread {Thread.CurrentThread.ManagedThreadId} created.");
            while (true)
            {
                SendThreadInvoke.WaitOne();
                while (SendQueue.Count > 0)
                {
                    var data = SendQueue.Dequeue();
                    uint ID = data.ID;
                    byte[] s_buffer = data.Buffer;

                    //send data
                    if (Connections.ContainsKey(ID))
                    {
                        try
                        {
                            var connection = Connections[ID];
                            byte[] network_buffer = new byte[SendAmount];
                            using (var buffer = new MemoryStream(s_buffer)) {
                                int sentAmount = 0, piecesSent = 0;
                                do
                                {
                                    sentAmount = buffer.Read(network_buffer, 0, SendAmount);
                                    OpenNetworkStream(connection,ID).Write(network_buffer, 0, sentAmount);
                                    if (s_buffer.Length > SendAmount)
                                        piecesSent++;                                    
                                }
                                while (sentAmount == SendAmount);
                                QConsole.WriteLine(Name, $"{Name} - Sent {sentAmount} bytes ({piecesSent} slices) to: {ID}");
                            }                                                       
                        }
                        catch (IOException exc)
                        {
                            QConsole.WriteLine(Name, $"ERROR: {exc.Message}");
                        }
                        catch (InvalidOperationException invalid)
                        {
                            QConsole.WriteLine(Name, $"ERROR: {invalid.Message}");
                        }
                    }
                    else QConsole.WriteLine(Name, $"Tried to send data to a disposed connection.");
                }
                SendThreadInvoke.Reset();
            }
        }

        private void Ready() => listener.BeginAcceptTcpClient(AcceptConnection, listener);

        private void GetAllInformation(TcpClient client)
        {
            foreach(var str in Enum.GetNames(typeof(SocketOptionName))) {                
                Console.Write(str + ": ");
                try
                {
                    Console.WriteLine(
                        client.Client.GetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)Enum.Parse(typeof(SocketOptionName), str)));                        
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex}");
                }
            }
        }

        private void DiscardAllReadBytes(ref MemoryStream Stream)
        {
            //discard all used bytes before Position.
            //this will leave only untouched/unread data.
            byte[] tempBuffer = new byte[Stream.Length - Stream.Position];
            Stream.Read(tempBuffer, 0, tempBuffer.Length);
            Stream.Dispose();
            Stream = new MemoryStream();
            Stream.Write(tempBuffer, 0, tempBuffer.Length);
        }

        protected virtual bool OnReceive(uint ID, ref MemoryStream dataBuffer, int dataLength)
        {            
            //move buffer to start
            dataBuffer.Seek(0,SeekOrigin.Begin);

            var packetBase = new T(); // use this to get packet format for individual 'product' (Quazar is used in many projects)
            uint packetHeaderLen = packetBase.GetHeaderSize(); // Get Packet Header -- basically get how much data we're waiting for

            bool packetHeaderSuccess = false;
            uint PayloadSize = 0, headerFailCount = 0;
            do
            {
                byte[] headerBuffer = new byte[packetHeaderLen]; // header data
                dataBuffer.Read(headerBuffer, 0, (int)packetHeaderLen);
                packetHeaderSuccess = packetBase.TryGetHeaderData(headerBuffer, out PayloadSize); // try to get the size of the data
                if (!packetHeaderSuccess)
                {
                    // Error getting size of the data -- incorrect format                
                    //no this data is not a packet, remove this data after returning to caller.                    
                    if(headerFailCount == 0)
                        QConsole.WriteLine("cQuaZar.PacketBase", "First packet in the response body isn't formatted correctly.");
                    headerFailCount++;
                    if (dataBuffer.Position >= dataBuffer.Length) // end of stream
                        break;
                }
            }
            while (!packetHeaderSuccess);
            if (!packetHeaderSuccess)
            {
                // Error getting size of the data -- incorrect format                
                //no this data is not a packet, remove this data after returning to caller.
                QConsole.WriteLine("cQuaZar.PacketBase", $"Read the entire stream -- no header found. (Attempts: {headerFailCount})");
                return false;
            }
            dataBuffer.Seek(0, SeekOrigin.Begin); // move the buffer back to the start
            File.WriteAllBytes(@"c:\nio2so\dump.dat",dataBuffer.ToArray());
            if (packetHeaderLen + PayloadSize > dataBuffer.Length)
            { // PACKET IS SPLIT -- we will put the buffer back and wait for more data.                
                return false; // This will tell it to not call receive function again until more data arrives
            }

            QConsole.WriteLine(Name, $"Frame received from {ID}: {dataBuffer.Length} bytes of {packetHeaderLen + PayloadSize} bytes");

            int transmissionSize = (int)(PayloadSize + packetHeaderLen); // size of the data stream
            byte[] readBuffer = new byte[transmissionSize];
            dataBuffer.Read(readBuffer, 0, transmissionSize);

            //read all packets found within this completed data frame
            IEnumerable<T> packets = default;
            packets = PacketBase.ParseAll<T>(ref readBuffer);

            //parse error!!!
            if (!packets?.Any() ?? true)
                throw new InvalidDataException("No packets found in transmission!!!");

            //process all packets
            foreach (var packet in packets)
            {
                packet.Received = DateTime.Now;
                packet.ConnectionID = ID;
                if (CachePackets)
                    IncomingTrans.Add(packet);
                InvokeOnIncomingPacket(ID, packet);
            }            

            int remainingSize = (int)(dataBuffer.Length - dataBuffer.Position);
            if (remainingSize > 0) // DATA REMAINING
            {
                DiscardAllReadBytes(ref dataBuffer);
                return true; // DATA REMAINING -- CALL AGAIN
            }
            return false; // NO MORE DATA
        }

        private void BeginReceive(TcpClient connection, uint ID)
        {
            QConsole.WriteLine(Name, $"Created Listener Thread: {Thread.CurrentThread.ManagedThreadId}");

            Exception SocketException = null;
            MemoryStream networkDataBuffer = new MemoryStream();
            byte[] networkData = new byte[connection.Client.Available];

            try
            {
                //Open the Network Stream now (for SSL will be SslStream, otherwise NetworkStream)
                using (Stream networkStream = OpenNetworkStream(connection, ID))
                {
                    void NetworkAsyncCallback(int NewDataLength)
                    {
                        QConsole.WriteLine(Name, $"Incoming data received from {ID}: {NewDataLength} bytes");

                        //write new data to the end of the buffer, being careful to preserve previous data
                        networkDataBuffer.Seek(0, SeekOrigin.End);
                        networkDataBuffer.Write(networkData, 0, NewDataLength); // write network stream to buffer

                        //read incoming data -- on TRUE, indicates there is data still in the buffer to process.
                        //run this function until all data is processed. For split packets, this will return false indicating no further
                        //processing shall be done until additional data is received.
                        while (OnReceive(ID, ref networkDataBuffer, NewDataLength))
                        {

                        }

                        //discard all used bytes after all OnReceive calls.
                        //this will leave only untouched/unread data.
                        DiscardAllReadBytes(ref networkDataBuffer);
                    }

                    //NETWORK RECEIVE LOOP
                    while (connection.Connected)
                    {
                        networkData = new byte[ReceiveAmount];

                        //This will handle the client forcibly closing connection by raising an exception the moment connection is lost.
                        //Therefore, this condition is handled here
                        int ReceiveSize = networkStream.Read(networkData, 0, ReceiveAmount);
                        if (ReceiveSize == 0)
                            continue; // cycle around and try to read again for ReadTimeout
                        NetworkAsyncCallback(ReceiveSize);
                    }
                }
            }
            catch (Exception ex)
            {
                SocketException = ex;
            }
            finally
            {
                //dispose data buffer
                networkDataBuffer.Dispose();
            }
            
            //Raise event and disconnect client (optionally with an error if one occurred)
            Disconnect(ID, SocketException);
            QConsole.WriteLine(Name, $"Listener Thread: {Thread.CurrentThread.ManagedThreadId} has completed and is now closed.");
        }

        /// <summary>
        /// Opens a new <see cref="Stream"/> for the <see cref="TcpClient"/> <paramref name="Connection"/> and configures it for SSL if <see cref="QuazarServerSettings.UseSSL"/> is <see langword="true"/>
        /// </summary>
        /// <param name="Connection"></param>
        /// <param name="ID"></param>
        /// <returns></returns>
        private Stream? OpenNetworkStream(TcpClient Connection, uint ID)
        {
            //TCP ONLY
            int ReadTimeout = Timeout.Infinite; // TSO can go very long without sending data, so we don't want to timeout in any scenario as long as the connection is open.

            Stream underlyingStream = Connection.GetStream();
            if (!Settings.UseSSL)
            {
                //SET READ TIMEOUTS
                underlyingStream.ReadTimeout = ReadTimeout;
                return underlyingStream;
            }
            //SSL
            SslStream ssl = SslUtil.GetSslStream(ID);
            return ssl;
        }

        private void AcceptConnection(IAsyncResult ar)
        {
            lock (_clients) // is full?
            {
                int backlog = _clients.Count;
                if (backlog >= BACKLOG)
                {
                    QConsole.WriteLine(Name, $"Server is full. Backlog: {backlog} >= {BACKLOG}");
                    ar.AsyncWaitHandle.Close();
                    goto reset;
                }
            }
            //can accept a new connection
            var newConnection = listener.EndAcceptTcpClient(ar);
            uint id = awardID();
            _clients.Add(id, newConnection);
            if (Settings.UseSSL)
            {
                //SSL Handshake
                SslUtil.SslHandshake(Settings.ServerCertificate, newConnection, id, Settings.ClientChain);
            }
            OnClientConnect(newConnection, id);
        reset:
            Ready();
        }

        public abstract void Start();
        public abstract void Stop();

        public IEnumerable<(uint ID, TcpClient Client)> GetAllConnectedClients()
        {
            foreach (var connection in _clients)
                yield return (connection.Key, connection.Value);
        }        
        public ClientInfo GetClientInfoByID(uint ID) => _clientInfo[ID];

        /// <summary>
        /// Disposes the connection stream, removes the Client, and invokes <see cref="OnConnectionsUpdated"/>
        /// </summary>
        /// <param name="id"></param>
        /// <param name="Reason"></param>
        public void Disconnect(uint id, Exception SocketError = default)
        {
            try
            {
                if(Settings.UseSSL)
                    SslUtil.RemoveSslStream(id); // remove the SSL stream if it exists
                if(Connections.TryGetValue(id, out TcpClient client))
                    client.Dispose();                
            }
            catch (SocketException exc)
            {

            }
            if (_clients.Remove(id))
            {
                OnConnectionsUpdated?.Invoke(id, null);
                OnClientDisconnect(id);
                QConsole.WriteLine(Name, $"Disconnected Client {id}. Reason: " + ((SocketError != default) ? $"Socket Error/Exception: [{SocketError}]" : "Expected Disconnect"));
            }
            _clientInfo.Remove(id);                        
        }

        protected void StopListening()
        {
            listener.Stop();
        }

        protected void BeginListening()
        {            
            listener.Start();
            QConsole.WriteLine(Name, $"Listening\nIP: {listener.LocalEndpoint}\nPORT: {PORT}");
            Ready();
        }      

        /// <summary>
        /// Called when a client connects to the server and has a <see cref="ClientInfo"/> struct associated.
        /// </summary>
        /// <param name="Connection"></param>
        /// <param name="ID"></param>
        protected virtual void OnClientConnect(TcpClient Connection, uint ID)
        {
            QConsole.WriteLine(Name, $"\nClient Connected\nIP: {Connection.Client.RemoteEndPoint}\nID: {ID}");
            //GetAllInformation(Connection);   
            ClientInfo info = new ClientInfo()
            {
                ID = ID,
                Name = "CLIENT",
                ConnectTime = DateTime.Now
            };
            _clientInfo.Add(ID, info);
            ConnectionHistory.Add(info);
            OnConnectionsUpdated?.Invoke(ID, info);
            ClientStartListenThread(Connection, ID);
        }

        protected virtual void OnConnected(TcpClient Connection, uint ID)
        {
            QConsole.WriteLine(Name, $"\nConnected to Server\nIP: {Connection.Client.RemoteEndPoint}\nID: {ID}");
            ClientStartListenThread(Connection, ID);
        }

        private void ClientStartListenThread(TcpClient Connection, uint ID)
        {
            Thread listenThread = new Thread(() => BeginReceive(Connection, ID));   
            listenThread.Start();
        }

        private void InvokeOnIncomingPacket(uint ID, T Data) => OnIncomingPacket?.Invoke(ID, Data);
        private void InvokeOnOutgoingPacket(uint ID, T Data) => OnOutgoingPacket?.Invoke(ID, Data);

        protected virtual void OnForceClose(TcpClient socket, uint ID)
        {
            OnConnectionsUpdated?.Invoke(ID, null);
            QConsole.WriteLine(Name, $"Client forcefully disconnected: {ID}");
        }
        protected virtual void OnClientDisconnect(uint ID) { }

        /// <summary>
        /// Gets an ID that isn't taken. This is not a GUID.
        /// </summary>
        /// <returns></returns>
        private uint awardID() =>        
            UniqueNumber.Generate(_clients.Keys);        

        /// <summary>
        /// Connects to a server component
        /// </summary>
        public uint Connect(IPAddress address, int port)
        {
            var newConnection = ConnectionHelper.Connect(address, port);
            uint id = awardID();
            OnConnected(newConnection, id);
            Ready();
            return id;
        }

        /// <summary>
        /// Connects to a server using the same IP as this server component was created
        /// </summary>
        /// <param name="port"></param>
        protected uint Connect(int port) => Connect(IPAddress.Loopback, port);

        //protected uint GetIDByWaypoint(SIMThemeParkWaypoints waypoint) => KnownConnectionMap[waypoint];

        /// <summary>
        /// Sends the packet(s) to every connected client
        /// </summary>
        /// <param name="Packets"></param>
        public void Broadcast(params T[] Packets)
        {
            var clients = new uint[_clients.Count];
            _clients.Keys.CopyTo(clients, 0);
            foreach(var client in clients)            
                Send(client, Packets);            
        }
        /// <summary>
        /// Sends the packet(s) to the client by ID.
        /// </summary>
        /// <param name="ID"></param>
        /// <param name="Packets"></param>
        public void Send(uint ID, params T[] Packets)
        {
            foreach(var packet in Packets)            
                Send(ID, packet);            
        }
        /// <summary>
        /// Sends raw data to a client by ID.
        /// </summary>
        /// <param name="ID"></param>
        /// <param name="s_buffer"></param>
        /// <param name="ManualModeOverride"></param>
        protected void Send(uint ID, byte[] s_buffer, bool ManualModeOverride = false)
        {
            if (ManualMode && !ManualModeOverride)
            {
                QConsole.WriteLine(Name, "ManualMode is Enabled. Use Manual Controls to send the required data.");
                return;
            }
            if (!_clientInfo.TryGetValue(ID, out var cxInfo))
            {
                QConsole.WriteLine(Name, "Couldn't find the client: " + ID);
                return;
            }            
            SendQueue.Enqueue((ID, s_buffer));
            SendThreadInvoke.Set();
        }

        /// <summary>
        /// Sends a packet to a client by ID.
        /// <para></para>
        /// </summary>
        /// <param name="ID"></param>
        /// <param name="Data"></param>
        /// <param name="ManualModeOverride"></param>
        protected void Send(uint ID, T Data, bool ManualModeOverride = false)
        {
            void _doSend(T packet)
            {
                packet.Sent = DateTime.Now;
                InvokeOnOutgoingPacket(ID, packet);

                if (CachePackets)
                    OutgoingTrans.Add(packet);
                Send(ID, packet.GetBytes(), ManualModeOverride);
                if (!CachePackets && DisposePacketOnSent)
                    packet.Dispose();
            }
            _doSend(Data);
            if (Data.HasChildPackets)
            {
                foreach (var child in Data.splitPackets)
                    _doSend(child as T);
                QConsole.WriteLine("System", $"{Name}: Found an outgoing transmission with {Data.ChildPacketAmount} child packets, those have been sent too.");
            }
        }
        protected virtual void OnManualSend(uint ID, ref T Data)
        {

        }
        /// <summary>
        /// Sends a packet irrespective of the server being in <see cref="ManualMode"/>
        /// </summary>
        /// <param name="ID"></param>
        /// <param name="Data"></param>
        public void ManualSend(uint ID, T Data)
        {
            OnManualSend(ID, ref Data);
            Send(ID, Data, true);            
        }
        /// <summary>
        /// Sends packet(s) irrespective of the server being in <see cref="ManualMode"/>
        /// </summary>
        /// <param name="ID"></param>
        /// <param name="Data"></param>
        public void ManualSend(uint ID, params T[] Packets)
        {
            foreach(var packet in Packets)            
                ManualSend(ID, packet); 
        }
    }
}
