using Kernys.Bson;
using PixelWorldsServer.Networking.Pixel_Worlds;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PixelWorldsServer.DataManagement;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;
using PixelWorldsServer.WorldSystem;
using System.Collections.Concurrent;
using System.Linq;
using static PixelWorldsServer.DataManagement.ConfigManager;
// pw-specific crafted TCPServer class.

namespace PixelWorldsServer.Networking
{
    class TCPServer
    {
        public static ServerConfiguration globalConfig;
        public static string externalServerIP = "127.0.0.1";
        public static bool isMaster = false;

        public ushort port = 10001;
        public int Version = 83;
        public bool maintenanceMode = false;
        public bool isShuttingDown = false;
        private int backlog = 32;
        public readonly Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        public List<Socket> clientSockets = new List<Socket>();
        public ConcurrentDictionary<Socket, object> socketData = new ConcurrentDictionary<Socket, object>();
        public Dictionary<Socket, BSONObject> queuedPackets = new Dictionary<Socket, BSONObject>();
        private static ManualResetEvent connectDone = new ManualResetEvent(false);
        private TCPEvents tcpEvents; // rebound
        public WorldManager worldManager;

        public TCPServer GetServer() { return this; }

        public void DeleteData(string ipPort)
        {

            lock (socketData)
            {
                List<int> indexesToRemove = new List<int>();
                for (int i = 0; i < socketData.Count; i++)
                {
                    var kp = socketData.ElementAt(i);
                    ClientData cData = (ClientData)kp.Value;
                    if (cData.session == ipPort)
                    {
                        Console.WriteLine("Adding session: " + cData.session + " to remove list!");
                        indexesToRemove.Add(i);
                    }

                    break;
                }

                foreach (int ind in indexesToRemove)
                {
                    object dataObj = null;
                    if (socketData.Remove(socketData.ElementAt(ind).Key, out dataObj))
                        Console.WriteLine("Successfully removed the session!");

                }
            }
        }

        public Socket[] GetSockets()
        {
            lock (clientSockets)
            {
                return clientSockets.ToArray();
            }
        }

        void CheckForInput()
        {
            while (true)
            {
                string input = Console.ReadLine();

                if (input == "quit")
                {
                    Console.WriteLine("Quitting...");
                    isShuttingDown = true;
                    Thread.Sleep(1000);
                    worldManager.SaveAll();
                    //serverSocket.Close();
                    Thread.Sleep(2000);
                    Environment.Exit(0);
                    continue;
                }

                string[] commands = input.Split(" ");

                if (commands.Length < 2) continue;

                switch (commands[0])
                {
                    case "setversion":
                        {
                            if (int.TryParse(commands[1], out Version))
                                Console.WriteLine($"Successfully set server version, the version now is: {Version}");
                            break;
                        }
                    case "broadcast":
                        {
                            var server = this;
                            string msg = "";
                            int cLen = commands.Length;

                            for (int i = 1; i < cLen; i++) {
                                msg += commands[i];
                                if (i != cLen - 1) msg += " ";
                            }

                            foreach (Socket s in this.GetSockets())
                            {
                                if (s == null) continue;

                                lock (s)
                                {
                                    if (!s.Connected) continue;
                                    ClientData cData = (ClientData)s.TryGetData(ref server);
                                    if (cData == null) continue;
                                    if (cData.worldID < 1) continue;

                                    World w = cData.GetWorld(ref server);
                                    if (w == null) continue;


                                    BSONObject bObj = new BSONObject();
                                    bObj[MessageLabels.MessageID] = "BGM";
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", w.worldID.ToString("X8"), w.WorldName, 1, "Global System Message: " + msg);
                                    s.AddBSON(ref server, bObj);
                                }
                            }
                            break;
                        }
                    case "maintenance":
                        {
                            int mode;
                            if (int.TryParse(commands[1], out mode))
                            {
                                maintenanceMode = (mode == 1) ? true : false;
                                Console.WriteLine($"Successfully set maintenance mode to: {maintenanceMode}");
                            }
                            break;
                        }
                    default:
                        break;
                }
            }
        }

        public TCPServer(string IP, bool useCommandShell = false, ushort _port = 10001, int _backlog = 0) // if pw should ever change ports, I can apply a change within a single line in Program.cs
        {
            TCPServer.externalServerIP = IP;
            Console.WriteLine("Initial external server IP: " + TCPServer.externalServerIP);
            port = _port;
            backlog = _backlog;
            worldManager = new WorldManager(this);
            

            if (useCommandShell)
            {
                Console.WriteLine("Running input shell in the background, as 'useCommandShell' was true.");
                Task.Run(() => CheckForInput());
                Console.WriteLine("Launched input shell!");
            }
        }

        public MysqlManager mysqlManager = new MysqlManager(TCPServer.isMaster ? "localhost" : "51.116.173.82", "pixelworlds", "root", "testpassword123"); // GrosserSascha.777
        public event AsyncCallback OnConnect;
        public event AsyncCallback OnDisconnect;
        public void HandleOnSend(IAsyncResult AR)
        {
            Console.WriteLine("Data has been sent successfully.");
        }
        private void RaiseInternalError(string dueTo)
        {
            Console.WriteLine($"ERROR: {dueTo}");
            // do something else if required soon which I doubt...
        }

        long time = 0;
        public void CheckAllClients()
        {
            long dtMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
#if DEBUG
            //Console.WriteLine("Checking whether all clients are properly connected... [CheckAllClients]");
#endif

            time = dtMs;

            List<string> ipPortsToRemove = new List<string>();
            foreach (Socket sock in GetSockets()) // a lock used to be here...
            {
                if (sock == null) continue;

                var server = this;
                lock (sock)
                {
                    try
                    {
                        string ipPort = "";
                        ClientData cld = sock.TryGetData(ref server) as ClientData;
                        if (cld == null)
                        {
                            ipPort = sock.RemoteEndPoint.ToString();
                        }
                        else
                        {
                            ipPort = cld.session;
                        }
                        
                        if (!sock.IsSocketConnected())
                        {
                            Console.WriteLine($"Pending to remove a client (due to dc)... ipPort: {ipPort}");
                            ipPortsToRemove.Add(ipPort);
                        }
                        else
                        {
                            sock.ReceiveTimeout = 4000;
                            sock.SendTimeout = 3000;

                            bool needPing = false;

                            if (cld != null)
                            {
                                if (cld.lastPing != 0 && dtMs >= cld.lastPing + 11600)
                                {
                                    Console.WriteLine("Socket timed out, ipPort: " + ipPort);
                                    ipPortsToRemove.Add(ipPort);
                                    goto SKIP_REST;
                                }
                            }

                            if (!sock.Connected)
                                ipPortsToRemove.Add(ipPort);
                        }
                    }
                    catch (SocketException ex)
                    {
                        Console.WriteLine("TODO FIX SOCKET EXCEPTION: " + ex.Message);
                    }
                }
            }

SKIP_REST:

            foreach (string ipPort in ipPortsToRemove)
            {
                Socket s = UnregisterClient(ipPort);
                if (s == null)
                {
                    // TODO FIX!!
                    //Console.WriteLine("error socket is null, after trying to UnregisterClient...");
                    continue;
                }
                lock (s)
                {
                    s.Close();
                    Console.WriteLine($"Closed connection for ipPort: {ipPort}.");
                }
            }

#if DEBUG
                //Console.WriteLine($"STATUS REPORT: There are {clientSockets.Count} players online! [CheckAllClients, End]");
#endif
        }

        public TCPEvents.StateObject RegisterClient(Socket sock)
        {
            lock (clientSockets)
            {
                clientSockets.Add(sock);
                TCPServer server = this;
                ClientData cld = new ClientData("PLAYER", "", "", 0, "");
                cld.session = sock.RemoteEndPoint.ToString();
                sock.SetData(ref server, cld);
            }
            return new TCPEvents.StateObject(sock);
        }

        public object GetDataFromCognitoID(string cogID, string token)
        {
            lock (socketData)
            {
                foreach (object obj in socketData.Values)
                {
                    if (obj == null) continue;
                    ClientData cld = obj as ClientData;
                    if (cld.cognito_ID == cogID && cld.cognito_Token == token)
                        return obj;
                }
            }
            return null; // Invalid or not found or not loaded into memory yet.
        }

        public Socket GetSocketByIpPort(string ipPort) // IP:PORT
        {
            var server = this;
            foreach (Socket sock in this.GetSockets())
            {
                if (sock == null) continue;

                if (sock.Connected)
                {
                    if (((IPEndPoint)sock.RemoteEndPoint).ToString() == ipPort)
                    {
                        return sock;
                    }
                }
                else
                {
                    ClientData cld = (ClientData)sock.TryGetData(ref server);
                    if (cld == null) continue;

                    if (cld.session == ipPort)
                        return sock;
                }
            }

            Console.WriteLine("GetSocketByIpPort no socket found...");
            return null;
        }

        public Socket UnregisterClient(string ipPort)
        {
            TCPServer server = this;

            Socket sock = GetSocketByIpPort(ipPort);
            if (sock != null)
            {
                try
                {
                    //sock.Shutdown(SocketShutdown.Both);
                    if (socketData.ContainsKey(sock))
                    {
                        if (socketData[sock] != null)
                        {
                            ClientData cld = socketData[sock] as ClientData;

                            if (cld.worldID > 0)
                            {
                                World w = cld.GetWorld(ref server);
                                if (w != null)
                                {
                                    BSONObject bObj = new BSONObject();
                                    bObj[MessageLabels.MessageID] = "PL";
                                    bObj[MessageLabels.UserID] = cld.userID;
                                    sock.BroadcastBSONInWorld(ref server, bObj, cld, false);
                                    w.RemoveFromQueue(cld.userID);
                                }
                            }

                            if (cld.cognito_ID != string.Empty && cld.GetUID() > 0)
                            {
                                if (mysqlManager.RequestSendQuery(string.Format("UPDATE players SET Token = '{0}', UserName = '{1}', IP = '{2}', gems = '{3}', bytecoins = '{4}', spots = '{5}', region = '{6}' WHERE ID = '" + Convert.ToInt32(cld.userID, 16).ToString() + "'",
                                    cld.cognito_Token, 
                                    cld.isRegistered ? cld.userName : "Dummy", 
                                    cld.IP, 
                                    cld.gems, 
                                    cld.bytecoins, 
                                    cld.SerializeSpotsData(), 
                                    cld.region)))

                                    Console.WriteLine("Successfully saved player data of user with ID: " + cld.userID + "! Region: " + cld.region);
                            }
                        }
                        else
                        {
                            RaiseInternalError("sock was null??!");
                        }
                        object o;
                        socketData.Remove(sock, out o);
                    }
                }
                catch (ObjectDisposedException ex)
                {
                    RaiseInternalError("at UnregisterClient >> " + ex.Message);
                    // ...
                }

                clientSockets.Remove(sock);
                Console.WriteLine("Removed 1 socket. (due to dc/else)");

                lock (queuedPackets)
                {
                    queuedPackets.Remove(sock);
                }
            }
            else
            {
                // TODO FIX!!
                
                Console.WriteLine("Socket was null");
            }
            return sock;
        }

        public void DeletePacketQueue(Socket sock)
        {
            lock (queuedPackets)
            {
                queuedPackets.Remove(sock);
            }
        }

        public void DeleteClientSocketFromList(Socket sock)
        {
            lock (clientSockets)
            {
                clientSockets.Remove(sock);
            }
        }

        public Socket DeleteClientSocketFromList(string ipPort)
        {
            Socket wantedSock = null;
            var server = this;
            lock (clientSockets)
            {
                List<int> indexesToRemove = new List<int>();
                for (int i = 0; i < clientSockets.Count; i++)
                {
                    Socket sock = clientSockets[i];
                    if (sock == null) continue;

                    ClientData cld = (ClientData)sock.TryGetData(ref server);
                    if (cld != null)
                    {
                        if (cld.session == ipPort)
                        {
                            indexesToRemove.Add(i);
                            break;
                        }
                    }
                }

                foreach (int ind in indexesToRemove)
                {
                    wantedSock = clientSockets[ind];
                    clientSockets.RemoveAt(ind);
                }
                return wantedSock;
            }
        }

        public int Setup(TCPEvents ev, bool local = true, string localIP = "127.0.0.1") // localIP only used if local = true
        {
            tcpEvents = ev;
            Console.WriteLine($"Setting up server... Chosen port: {port}");

            if (OnConnect == null)
            {
                RaiseInternalError("OnConnect event has not been set (was null)!");
                return -1;
            }
            
            serverSocket.Bind(new IPEndPoint(local ? IPAddress.Parse(localIP) : IPAddress.Any, port));
            Console.WriteLine("serverSocket has been bound!");
            serverSocket.Listen(backlog);
            Console.WriteLine($"serverSocket is listening with a backlog of: {backlog}!");

            serverSocket.BeginAccept(OnConnect, null);
            return 0;
        }
    }
}
