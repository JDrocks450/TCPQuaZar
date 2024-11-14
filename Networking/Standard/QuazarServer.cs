using QuazarAPI.Networking.Data;
using QuazarAPI.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace QuazarAPI.Networking.Standard
{
    public abstract class QuazarServer<T> where T : PacketBase, new()
    {        
        /// <summary>
        /// The name of this <see cref="QuazarServer"/>
        /// </summary>
        public string Name
        {
            get; set;
        }

        public const int DefaultSendAmt = 256;

        /// <summary>
        /// The amount of data to receive per transmission
        /// </summary>
        public int ReceiveAmount { get; protected set; } = 1000;
        public int SendAmount { get; set; } = DefaultSendAmt;

        /// <summary>
        /// The port of this <see cref="TcpListener"/>
        /// </summary>
        public int PORT
        {
            get; set;
        }

        /// <summary>
        /// The amount of connections accepted.
        /// </summary>
        public uint BACKLOG
        {
            get;set;
        }

        /// <summary>
        /// All packets sent through <see cref="Send"/> functions will be ignored except when sent from  
        /// </summary>
        public bool ManualMode
        {
            get; set;
        } = false;

        /// <summary>
        /// The current packet queue, dont use this i fucked it.
        /// </summary>
        public uint PacketQueue
        {
            get; protected set;
        } = 0x0A;

        /// <summary>
        /// Sets whether or not incoming / outgoing packets are cached.
        /// </summary>
        /// <param name="Enabled"></param>
        protected void SetPacketCaching(bool Enabled) =>
            _packetCache =
#if DEBUG 
            true;
#else
            Enabled;
#endif

        protected TcpListener listener;
        protected Dictionary<uint, TcpClient> _clients = new Dictionary<uint, TcpClient>();
        protected Dictionary<uint, ClientInfo> _clientInfo = new Dictionary<uint, ClientInfo>();

        public ObservableCollection<T> IncomingTrans = new ObservableCollection<T>(),
                                            OutgoingTrans = new ObservableCollection<T>();
        public ObservableCollection<ClientInfo> ConnectionHistory = new ObservableCollection<ClientInfo>();
        protected bool _packetCache =
#if DEBUG
            true;
#else
            false;
#endif                

        protected Socket Host => listener.Server;
        protected IDictionary<uint, TcpClient> Connections => _clients;

        protected Queue<(uint ID, byte[] Buffer)> SendQueue = new Queue<(uint ID, byte[] Buffer)>();
        protected Thread SendThread;
        protected ManualResetEvent SendThreadInvoke;

        /// <summary>
        /// Disposes the packet passed to an overload of the function: <see cref="Send(uint, T[])"/>
        /// <para>If <see cref="CachePackets"/> is <see langword="true"/> this will have no effect as the packets 
        /// cannot be safely disposed as they are still referenced elsewhere</para>
        /// </summary>
        protected bool DisposePacketOnSent { get; set; } = true;
        /// <summary>
        /// Gets (or sets) whether or not to store outgoing/incoming packets to the <see cref="OutgoingTrans"/> and <see cref="IncomingTrans"/> collections
        /// </summary>
        protected bool CachePackets { get => _packetCache; set => _packetCache = value; }

        public IPAddress MyIP { get; }

        //**EVENTS        
        public delegate void QuazarClientInfoHandler(uint ClientID, ClientInfo Info);
        public delegate void QuazarDataHandler(uint ClientID, T Packet);
        public event QuazarDataHandler OnIncomingPacket, OnOutgoingPacket;
        public event QuazarClientInfoHandler OnConnectionsUpdated;
        //**

        /// <summary>
        /// Creates a <see cref="QuazarServer"/> with the specified parameters.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="port"></param>
        /// <param name="Waypoint"></param>
        /// <param name="backlog"></param>
        protected QuazarServer(string name, int port, uint backlog = 1, IPAddress ListenIP = default)
        {
            if (ListenIP == default) { ListenIP = IPAddress.Loopback; }
            MyIP = ListenIP;
            PORT = port;
            BACKLOG = backlog;
            Name = name;
            
            QConsole.WriteLine(Name, $"Server object created. Name: {name} Port: {port} IP: {ListenIP}");
            
            Init();
        }

        protected virtual void Init()
        {
            listener = new TcpListener(MyIP, PORT);
            listener.Server.SendBufferSize = SendAmount;
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
                                    connection.GetStream().Write(network_buffer, 0, sentAmount);
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
                catch
                {
                    Console.WriteLine("ERROR");
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

            byte[] headerBuffer = new byte[packetHeaderLen]; // header data
            dataBuffer.Read(headerBuffer, 0, (int)packetHeaderLen);
            if (!packetBase.TryGetHeaderData(headerBuffer, out uint PayloadSize)) // try to get the size of the data
            { // Error getting size of the data -- incorrect format                
                //no this data is not a packet, remove this data after returning to caller.
                QConsole.WriteLine("cQuaZar.PacketBase", "First packet in the response body isn't formatted correctly.");
                return false;    
            }
            dataBuffer.Seek(0, SeekOrigin.Begin); // move the buffer back to the start
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
                if (_packetCache)
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

            MemoryStream dataBuffer = new MemoryStream();
            byte[] networkStream = new byte[connection.Client.Available];

            void NetworkAsyncCallback(int NewDataLength)
            {
                QConsole.WriteLine(Name, $"Incoming data received from {ID}: {NewDataLength} bytes");

                //write new data to the end of the buffer, being careful to preserve previous data
                dataBuffer.Seek(0, SeekOrigin.End);
                dataBuffer.Write(networkStream, 0, NewDataLength); // write network stream to buffer

                //read incoming data -- on TRUE, indicates there is data still in the buffer to process.
                //run this function until all data is processed. For split packets, this will return false indicating no further
                //processing shall be done until additional data is received.
                while (OnReceive(ID, ref dataBuffer, NewDataLength))
                {

                }

                //discard all used bytes after all OnReceive calls.
                //this will leave only untouched/unread data.
                DiscardAllReadBytes(ref dataBuffer);
            }

            SocketError ErrorCode = SocketError.Success;
            while (connection.Connected)
            {
                networkStream = new byte[ReceiveAmount];
                try
                {
                    //This will handle the client forcibly closing connection by raising an exception the moment connection is lost.
                    //Therefore, this condition is handled here
                    int ReceiveSize = connection.Client.Receive(networkStream, 0, ReceiveAmount, SocketFlags.None, out ErrorCode);
                    if (ErrorCode != SocketError.Success)
                        break;
                    NetworkAsyncCallback(ReceiveSize);
                }
                catch (SocketException e)
                {
                    ErrorCode = e.SocketErrorCode;
                    break;
                }
            }

            //Raise event and disconnect problem client
            Disconnect(ID, ErrorCode);
            QConsole.WriteLine(Name, $"Closing Listener Thread: {Thread.CurrentThread.ManagedThreadId}");
        }
        
        private void AcceptConnection(IAsyncResult ar)
        {
            var newConnection = listener.EndAcceptTcpClient(ar);
            uint id = awardID();
            _clients.Add(id, newConnection);            
            OnClientConnect(newConnection, id);
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
        public void Disconnect(uint id, SocketError Reason = SocketError.Success)
        {
            try
            {
                var client = Connections[id];
                client.Dispose();
            }
            catch (SocketException exc)
            {

            }
            _clients.Remove(id);
            _clientInfo.Remove(id);
            OnConnectionsUpdated?.Invoke(id, null);
            QConsole.WriteLine(Name, $"Disconnected Client {id} " + (Reason != SocketError.Success ? $"[{Reason}]" : ""));
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
                PacketQueue++;
                if (_packetCache)
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
