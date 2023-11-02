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

        public event EventHandler<ClientInfo> OnConnectionsUpdated;

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
            Init();

            QConsole.WriteLine("QuazarServer", $"Server object created. Name: {name} Port: {port} IP: {ListenIP}");
        }

        protected virtual void Init()
        {
            listener = new TcpListener(MyIP, PORT);
            listener.Server.SendBufferSize = SendAmount;
            QConsole.WriteLine("QuazarServer", $"Server object init complete. IP: {MyIP} Port: {PORT}");

            SendThreadInvoke = new ManualResetEvent(false);
            SendThread = new Thread(doSendLoop);
            SendThread.Start();            
        }

        private void doSendLoop()
        {
            QConsole.WriteLine("QuazarServer", $"New Thread {Thread.CurrentThread.ManagedThreadId} created.");
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
                                int sentAmount = 0;
                                do
                                {
                                    sentAmount = buffer.Read(network_buffer, 0, SendAmount);
                                    connection.GetStream().Write(network_buffer, 0, sentAmount);
                                    if (s_buffer.Length > SendAmount)
                                        QConsole.WriteLine("System", $"{Name} - An outgoing packet was large ({s_buffer.Length} bytes) sent a chunk of {sentAmount}");
                                    else QConsole.WriteLine("System", $"{Name} - Sent {sentAmount} bytes to: {ID}");
                                }
                                while (sentAmount == SendAmount);
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

        protected virtual bool OnReceive(uint ID, byte[] dataBuffer)
        {
            if (dataBuffer.Where(x => x == 0).Count() == dataBuffer.Length)
            {
                QConsole.WriteLine(Name, $"Error Detected on Client: " + ID);
                Disconnect(ID);
                return false;
            }
            int amount = 0;
            QConsole.WriteLine("System", $"{Name}: Client: {ID} :: Incoming data: {dataBuffer.Length}");
            int fileNum = new DirectoryInfo("/packets").GetFiles().Count();
            File.WriteAllBytes($"/packets/incoming_[{fileNum}].dat", dataBuffer);
            byte[] readBuffer = new byte[dataBuffer.Length];
            dataBuffer.CopyTo(readBuffer, 0);
            var packets = PacketBase.ParseAll<T>(ref readBuffer);
            foreach (var packet in packets)
            {
                QConsole.WriteLine("System", $"{Name}: Client: {ID} :: Incoming packet was successfully parsed.");
                packet.Received = DateTime.Now;
                if (_packetCache)
                    IncomingTrans.Add(packet);
                OnIncomingPacket(ID, packet);
            }
            QConsole.WriteLine("System", $"{Name}: Client: {ID} :: Found {packets.Count()} Packets...");
            return true;
        }

        private void BeginReceive(TcpClient connection, uint ID)
        {            
            byte[] dataBuffer = null;
            void OnRecieve(object state)
            {
                if (this.OnReceive(ID, dataBuffer))
                    Ready();
                else return;
            }

            if (connection.Client.Available != 0)
            {
                dataBuffer = new byte[connection.Client.Available];
                connection.Client.Receive(dataBuffer);
                OnRecieve(null);
                return;
            }            
            void Ready()
            {
                dataBuffer = new byte[ReceiveAmount];
                try
                {                    
                    //This will handle the client forcibly closing connection by raising an exception the moment connection is lost.
                    //Therefore, this condition is handled here
                    connection?.Client.BeginReceive(dataBuffer, 0, ReceiveAmount, SocketFlags.None, OnRecieve, null);
                }
                catch(SocketException e)
                {
                  //Raise event and disconnect problem client
                  OnForceClose(connection, ID);
                  Disconnect(ID);
                  return;
                }
            }
            Ready();
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

        public void Disconnect(uint id)
        {
            try
            {
                var client = Connections[id];
                client.Close();
            }
            catch (SocketException exc)
            {

            }
            _clients.Remove(id);
            _clientInfo.Remove(id);
            OnConnectionsUpdated?.Invoke(this, null);
            QConsole.WriteLine(Name, $"Disconnected Client {id}");
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
            OnConnectionsUpdated?.Invoke(this, info);
            BeginReceive(Connection, ID);
        }

        protected virtual void OnConnected(TcpClient Connection, uint ID)
        {
            QConsole.WriteLine(Name, $"\nConnected to Server\nIP: {Connection.Client.RemoteEndPoint}\nID: {ID}");
            BeginReceive(Connection, ID);
        }

        protected abstract void OnIncomingPacket(uint ID, T Data);
        protected abstract void OnOutgoingPacket(uint ID, T Data);   

        protected virtual void OnForceClose(TcpClient socket, uint ID)
        {
            OnConnectionsUpdated?.Invoke(this, null);
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
                OnOutgoingPacket(ID, packet);
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
