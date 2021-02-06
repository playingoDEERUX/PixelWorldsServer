using BasicTypes;
using Kernys.Bson;
using PixelWorldsServer.DataManagement;
using PixelWorldsServer.Networking.Pixel_Worlds;
using PixelWorldsServer.WorldSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using static PixelWorldsServer.DataManagement.ConfigManager;

// somewhat a "template" for our event functions, different ones can be used of course.
// pixel worlds-protocol specific stuff is inside "Networking/Pixel Worlds" ~playingo or DEERUX or whatever you wanna call me like.

namespace PixelWorldsServer.Networking
{
    class TCPEvents
    {
        private TCPServer server; // rebound
        public event AsyncCallback OnReceive;

        MessageHandler mHandler;

        public byte[] GPDCache = null; // only for now due to these being more complex/time consuming.
        public byte[] GWCCache = null;

        private void RaiseInternalError(string dueTo)
        {
            Console.WriteLine($"ERROR (TCPEvents): {dueTo}");
            // do something else if required soon which I doubt...
        }
        public class StateObject
        {
            public const int BufferSize = 4096;
            public byte[] buffer = new byte[BufferSize];
            BSONObject bsonObject = new BSONObject(); // received bson object, which is the only thing PW uses for their client/server communication
            public Socket currentSocket;
            public bool instantClose = false;

            public StateObject(Socket sock = null)
            {
                if (sock != null)
                    currentSocket = sock;
            }
        }
        public TCPEvents(TCPServer serv)
        {
            server = serv;
            mHandler = new MessageHandler(ref server, this);
        }
        public void HandleOnConnect(IAsyncResult AR)
        {
            if (server.isShuttingDown) return;
            Console.WriteLine("Processing connection... [HandleOnConnect]");
            Socket socket;

            try
            {
                socket = server.serverSocket.EndAccept(AR);
            }
            catch (ObjectDisposedException) // I cannot seem to avoid this (on exit when properly closing sockets)
            {
                Console.WriteLine("ERROR: Catched ObjectDisposedException, not proceeding. [HandleOnConnect]");
                goto LABEL_ACCEPT_SKIP;
            }

            if (OnReceive != null)
            {
                socket.ReceiveTimeout = 3000;
                socket.SendTimeout = 4000;

                StateObject state = server.RegisterClient(socket);

                try
                {
                    socket.BeginReceive(state.buffer, 0, state.buffer.Length, SocketFlags.None, OnReceive, state);
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
            }
            else
            {
                RaiseInternalError("Cannot begin receiving on a client, no function bound for doing such as OnReceive was null.");
            }

LABEL_ACCEPT_SKIP:
            server.serverSocket.BeginAccept(HandleOnConnect, null);
        }
        public void HandleOnReceive(IAsyncResult AR)
        {
            //Console.WriteLine("Processing packet... [HandleOnReceive]");

            if (server.isShuttingDown) return;
            StateObject peer = (StateObject)AR.AsyncState;
            Socket client = peer.currentSocket;

            if (client == null) return;

            // here was client lock


            int num = 0;

            IPEndPoint ipEndPoint = null;

            try
            {
                ipEndPoint = client.RemoteEndPoint as IPEndPoint; // Always handy to have IPEndPoint
                num = client.EndReceive(AR);
            }
            catch (SocketException)
            {
                Console.WriteLine("Client forcefully disconnected.");
                // Don't shutdown because the socket may be disposed and its disconnected anyway.
                if (ipEndPoint != null) server.UnregisterClient(ipEndPoint.ToString());
                HandleDisconnect(peer);
                return;
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("Turns out that client was already disconnected, huh?");
                return;
            }

            ClientData cld = client.TryGetData(ref server) as ClientData;
            if (cld == null)
            {
                throw new Exception("ClientData was null...");
            }
            cld.IP = ipEndPoint.Address.ToString();

            //Console.WriteLine($"Chunk len: {num}");

            if (num > 4 && num < StateObject.BufferSize) // max 4095 and min 4 because pw always uses initial message length in the beginning and no need msg framing on server anyway
            {
                byte[] array = new byte[num];


                int allegedLength = BitConverter.ToInt32(peer.buffer, 0);

                if (allegedLength == num)
                {
                    Buffer.BlockCopy(peer.buffer, 0, array, 0, num);
                    byte[] arrayWithoutMsgLen = array.Skip(4).ToArray(); // the shit we need to extract BSONObject(s) from it


                    try
                    {

                        //lock (server.socketData)
                        //{
                        bool needPing = true;
                        BSONObject receivedBObj = SimpleBSON.Load(arrayWithoutMsgLen);
                        // Console.WriteLine($"Bson-Message count: {receivedBObj["mc"].int32Value}");
                        // Not necessary to check whether key even exists as this is wrapped in a try catch statement :)
                        // Only go over to MessageHandler.cs functions if packet header or general stuff at least has been verified.
                        int msgCount = receivedBObj[MessageLabels.MessageCount];
                        //Console.WriteLine($"BMessage count: {msgCount}");

                        if (msgCount == 0 && !client.HasQueuedPackets(ref server))
                        {
                            SocketError err;
                            client.BeginSend(array, 0, array.Length, SocketFlags.None, out err, null, null);
                            goto LABEL_SKIP_EVERYTHING;
                        }

                        for (int i = 0; i < msgCount; i++)
                        {
#if DEBUG
                                Console.WriteLine($"Processing bsonmessage #{i}...");
#endif
                            BSONObject currentBObj = receivedBObj["m" + i.ToString()] as BSONObject;

                            string messageId = currentBObj[MessageLabels.MessageID];

                            /*foreach (string key in currentBObj.Keys)
                            {
                                BSONValue bVal = currentBObj[key];

                                switch (bVal.valueType)
                                {
                                    case BSONValue.ValueType.String:
                                        Console.WriteLine("[SERVER] >> KEY: " + key + " VALUE: " + bVal.stringValue);
                                        break;
                                    case BSONValue.ValueType.Object:
                                        {
                                            Console.WriteLine("[SERVER] >> KEY: " + key + " VALUE: object");
                                            // that object related shit is more complex so im gonna leave that for later
                                            break;
                                        }
                                    case BSONValue.ValueType.Int32:
                                        Console.WriteLine("[SERVER] >> KEY: " + key + " VALUE: " + bVal.int32Value);
                                        break;
                                    case BSONValue.ValueType.Int64:
                                        Console.WriteLine("[SERVER] >> KEY: " + key + " VALUE: " + bVal.int64Value);
                                        break;
                                    case BSONValue.ValueType.Double:
                                        Console.WriteLine("[SERVER] >> KEY: " + key + " VALUE: " + bVal.doubleValue);
                                        break;
                                    case BSONValue.ValueType.Boolean:
                                        Console.WriteLine("[SERVER] >> KEY: " + key + " VALUE: " + bVal.boolValue.ToString());
                                        break;
                                    default:
                                        Console.WriteLine("[SERVER] >> KEY: " + key);
                                        break;
                                }
                            }*/

                            //Console.WriteLine(messageId);


                            

                            switch (messageId)
                            {
                                case MessageLabels.Ident.VersionCheck:
                                    {
                                        cld.OS = currentBObj["OS"];
                                        Console.WriteLine("Client wants to verify version!");

                                        BSONObject resp = new BSONObject();
                                        resp[MessageLabels.MessageID] = MessageLabels.Ident.VersionCheck;
                                        resp[MessageLabels.VersionNumberKey] = server.Version;
                                        client.AddBSON(ref server, resp);
                                        break;
                                    }
                                case "rOP":
                                    {
                                        if (cld.worldID < 1) break;
                                        //client.BroadcastBSONInWorld(ref server, pObj, cld, false, false);

                                        bool hasPlayers = false;

                                        foreach (Socket s in server.GetSockets())
                                        {

                                            if (s == null) continue;
                                            if (!s.Connected) continue;


                                            ClientData cData = (ClientData)s.TryGetData(ref server);
                                            if (cData == null) continue;
                                            if (cData.userID == cld.userID) continue; // also the "same" player, wtf?

                                            if (cData.worldID == cld.worldID)
                                            {
                                                World w = cData.GetWorld(ref server);
                                                if (w == null) continue;
                                                
                                                Console.WriteLine($"Located player with userID: {cData.userID} in world with ID: {cld.worldID}! (RequestOtherPlayers)");

                                                BSONObject pObj = new BSONObject();
                                                pObj[MessageLabels.MessageID] = "AnP";
                                                pObj["x"] = cld.posX;
                                                pObj["y"] = cld.posY;
                                                pObj["t"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                                pObj["a"] = cld.currentAnimation;
                                                pObj["d"] = cld.currentDirection;
                                                List<int> spotsList = new List<int>();
                                                //spotsList.AddRange(Enumerable.Repeat(0, 35));

                                                pObj["spots"] = spotsList;
                                                pObj["familiar"] = 0;
                                                pObj["familiarName"] = "";
                                                pObj["familiarLvl"] = 0;
                                                pObj["familiarAge"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                                pObj["isFamiliarMaxLevel"] = false;
                                                pObj["UN"] = cld.nickName;
                                                pObj["U"] = cld.userID;
                                                pObj["Age"] = 69;
                                                pObj["LvL"] = 99;
                                                pObj["xpLvL"] = 99;
                                                pObj["pAS"] = 0;
                                                pObj["PlayerAdminEditMode"] = false;
                                                pObj["Ctry"] = 999;
                                                pObj["GAmt"] = cld.gems;
                                                pObj["ACo"] = 0;
                                                pObj["QCo"] = 0;
                                                pObj["Gnd"] = 0;
                                                pObj["skin"] = 7;
                                                pObj["faceAnim"] = 0;
                                                pObj["inPortal"] = false;
                                                pObj["SIc"] = 0;
                                                pObj["VIPEndTimeAge"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                                pObj["IsVIP"] = false;
                                                pObj["U"] = cData.userID;
                                                pObj["UN"] = cData.nickName;
                                                pObj["x"] = cData.posX;
                                                pObj["y"] = cData.posY;
                                                pObj["a"] = cData.currentAnimation;
                                                pObj["d"] = cData.currentDirection;

                                                client.AddBSON(ref server, pObj);
                                                hasPlayers = true;
                                            }
                                        }


                                        client.AddBSON(ref server, currentBObj);
                                        
                                        break;
                                    }
                                case "BIPack":
                                    {
                                        List<int> ids = new List<int>();
                                        ids.Add(9);
                                        ids.Add(1);
                                        ids.Add(16);

                                        currentBObj["S"] = "PS";
                                        currentBObj["IPRs"] = ids;
                                        //currentBObj["IPRs2"] = ids;
                                        client.AddBSON(ref server, currentBObj);
                                        //client.Ping(ref server, ref cld);
                                        break;
                                    }
                                case "rAI":
                                    {
                                        client.AddBSON(ref server, currentBObj);
                                        break;
                                    }
                                case "WCM":
                                    {
                                        mHandler.HandleWorldChatMessage(client, currentBObj, ref cld);
                                        break;
                                    }
                                case "rAIp":
                                    {
                                        client.AddBSON(ref server, currentBObj);
                                        break;
                                    }
                                case MessageLabels.Ident.LeaveWorld:
                                    {
                                        if (cld.worldID > 0)
                                        {
                                            BSONObject bObj = new BSONObject();
                                            bObj[MessageLabels.MessageID] = "PL";
                                            bObj[MessageLabels.UserID] = cld.userID;
                                            client.BroadcastBSONInWorld(ref server, bObj, cld, false, false);
                                        }

                                        cld.worldEnterTimeout = 0;
                                        cld.GetWorld(ref server).RemoveFromQueue(cld.userID);
                                        cld.hasSpawned = false;
                                        cld.worldID = -1;
                                        cld.isEnteringWorld = false;
                                        client.AddBSON(ref server, currentBObj);
                                        break;
                                    }
                                case "RtP":
                                    {
                                        BSONObject pObj = new BSONObject();
                                        pObj[MessageLabels.MessageID] = "AnP";
                                        pObj["x"] = cld.posX;
                                        pObj["y"] = cld.posY;
                                        pObj["t"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                        pObj["a"] = cld.currentAnimation;
                                        pObj["d"] = cld.currentDirection;
                                        List<int> spotsList = new List<int>();
                                        //spotsList.AddRange(Enumerable.Repeat(0, 35));

                                        pObj["spots"] = spotsList;
                                        pObj["familiar"] = 0;
                                        pObj["familiarName"] = "";
                                        pObj["familiarLvl"] = 0;
                                        pObj["familiarAge"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                        pObj["isFamiliarMaxLevel"] = false;
                                        pObj["UN"] = cld.nickName;
                                        pObj["U"] = cld.userID;
                                        pObj["Age"] = 69;
                                        pObj["LvL"] = 99;
                                        pObj["xpLvL"] = 99;
                                        pObj["pAS"] = 0;
                                        pObj["PlayerAdminEditMode"] = false;
                                        pObj["Ctry"] = 999;
                                        pObj["GAmt"] = cld.gems;
                                        pObj["ACo"] = 0;
                                        pObj["QCo"] = 0;
                                        pObj["Gnd"] = 0;
                                        pObj["skin"] = 7;
                                        pObj["faceAnim"] = 0;
                                        pObj["inPortal"] = false;
                                        pObj["SIc"] = 0;
                                        pObj["VIPEndTimeAge"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                        pObj["IsVIP"] = false;
                                        client.BroadcastBSONInWorld(ref server, pObj, cld, false, false);

                                        cld.worldEnterTimeout = 0;
                                        cld.hasSpawned = true;
                                        client.Ping(ref server, ref cld);
                                        break;
                                    }
                                case MessageLabels.Ident.TryToJoinWorld:
                                    {
                                        Console.WriteLine("Player is trying to join world, region: " + cld.region.ToString());
                                        mHandler.HandleTryToJoinWorld(client, currentBObj, ref cld);
                                        break;
                                    }
                                case MessageLabels.Ident.WearableUsed:
                                case MessageLabels.Ident.WearableRemoved:
                                    {
                                        int id = currentBObj["hBlock"];
                                        if (id < 0 || id >= ItemDB.ItemsCount()) break;

                                        ItemDB.Item it = ItemDB.GetByID(id);
                                        

                                        currentBObj[MessageLabels.UserID] = cld.userID;
                                        client.BroadcastBSONInWorld(ref server, currentBObj, cld, false, false);
                                        break;
                                    }
                                case MessageLabels.Ident.MovePlayer:
                                    {

                                        if (currentBObj.ContainsKey("x") &&
                                            currentBObj.ContainsKey("y") &&
                                            currentBObj.ContainsKey("a") &&
                                            currentBObj.ContainsKey("d") &&
                                            currentBObj.ContainsKey("t"))

                                        {
                                            cld.posX = currentBObj["x"].doubleValue;
                                            cld.posY = currentBObj["y"].doubleValue;

                                            cld.currentAnimation = (byte)currentBObj["a"];
                                            cld.currentDirection = (byte)currentBObj["d"];
                                            currentBObj["U"] = cld.userID;
                                            if (currentBObj.ContainsKey("tp"))
                                                currentBObj.Remove("tp");

                                            //Console.WriteLine($"Your PosX: {cld.posX} PosY: {cld.posY}");

                                            client.BroadcastBSONInWorld(ref server, currentBObj, cld, true, false);
                                        }


                                        client.Ping(ref server, ref cld);
                                        break;
                                    }
                                
                                case "PSicU":
                                    {

                                        currentBObj["U"] = cld.userID;
                                        client.BroadcastBSONInWorld(ref server, currentBObj, cld, false, false);
                                        client.Ping(ref server, ref cld);
                                        break;
                                    }
                                case "Rez":
                                    {
                                        currentBObj["U"] = cld.userID;
                                        client.BroadcastBSONInWorld(ref server, currentBObj, cld, false, false);
                                        break;
                                    }
                                case "RsP":
                                    {
                                        World w = cld.GetWorld(ref server);
                                        if (w == null) continue;
                                        if (w.worldID < 1) continue;

                                        BSONObject bObj = new BSONObject();
                                        bObj[MessageLabels.MessageID] = "UD";
                                        bObj[MessageLabels.UserID] = cld.userID;
                                        bObj["x"] = w.spawnPointX;
                                        bObj["y"] = w.spawnPointY;
                                        bObj["DBl"] = 0;
                                        client.BroadcastBSONInWorld(ref server, bObj, cld, false, true);

                                        client.AddBSON(ref server, currentBObj);
                                        break;
                                    }
                                case "TDmg":
                                    {
                                        World w = cld.GetWorld(ref server);
                                        if (w == null) continue;
                                        if (w.worldID < 1) continue;

                                        BSONObject bObj = new BSONObject();
                                        bObj[MessageLabels.MessageID] = "UD";
                                        bObj[MessageLabels.UserID] = cld.userID;
                                        bObj["x"] = w.spawnPointX;
                                        bObj["y"] = w.spawnPointY;
                                        bObj["DBl"] = 0;
                                        client.BroadcastBSONInWorld(ref server, bObj, cld, false, true);
                                        client.AddBSON(ref server, currentBObj);
                                        break;
                                    }
                                case "PDC":
                                    {
                                        World w = cld.GetWorld(ref server);
                                        if (w == null) continue;
                                        if (w.worldID < 1) continue;

                                        BSONObject bObj = new BSONObject();
                                        bObj[MessageLabels.MessageID] = "UD";
                                        bObj[MessageLabels.UserID] = cld.userID;
                                        bObj["x"] = w.spawnPointX;
                                        bObj["y"] = w.spawnPointY;
                                        bObj["DBl"] = 0;
                                        client.BroadcastBSONInWorld(ref server, bObj, cld, false, true);

                                        client.AddBSON(ref server, currentBObj);
                                        break;
                                    }
                                case "GAW":
                                    {
                                        BSONObject bObj = new BSONObject();
                                        bObj[MessageLabels.MessageID] = "GAW";

                                        World[] worlds = server.worldManager.GetCachedWorlds();
                                        List<string> worldIDs = new List<string>();
                                        List<string> worldNames = new List<string>();
                                        List<int> playerCounts = new List<int>();

                                        foreach (World world in worlds)
                                        {
                                            int pC = world.GetPlayerCount(ref server);
                                            if (pC > 0)
                                            {
                                                worldIDs.Add(world.worldID.ToString("X8"));
                                                worldNames.Add(world.WorldName);
                                                playerCounts.Add(pC);
                                            }
                                        }

                                        bObj["W"] = worldIDs;
                                        bObj["WN"] = worldNames;
                                        bObj["Ct"] = playerCounts;

                                        client.AddBSON(ref server, bObj);

                                        break;
                                    }
                                case MessageLabels.Ident.GetWorld:
                                    {
                                        /*if (reg != "")
                                        {
                                            if (ServerEntryList.GetRegionFromIP(TCPServer.externalServerIP) != cld.region)
                                            {

                                            }
                                        }*/
                                        string worldName = currentBObj["W"];
                                        int jr;

                                        string worldID = server.worldManager.GetWorldIDByName(worldName);
                                        string reg = cld.region.ToLower();

                                        string subIP = TCPServer.externalServerIP;
                                        string subregion = "europe";

                                        if (worldID == "NONE")
                                        {
                                            if (reg != "")
                                            {
                                                Console.WriteLine("Region of Player: " + reg);
                                                if (ServerEntryList.GetRegionFromIP(TCPServer.externalServerIP) != reg) // find preferred server for the new world...
                                                {
                                                    ServerUnit su = ServerEntryList.FindServerByRegion(reg);

                                                    if (su.ip != TCPServer.externalServerIP)
                                                    {
                                                        subregion = su.region;
                                                        // master server is different ip... so it must be a different one
                                                        subIP = su.ip;
                                                    }
                                                }
                                            }
                                        }

                                        World world = server.worldManager.GetWorld(subIP, worldName, worldID);
                                        if (world == null)
                                            jr = (int)MessageLabels.JR.UNAVAILABLE;
                                        else if (server.maintenanceMode)
                                            jr = (int)MessageLabels.JR.MAINTENANCE;
                                        else
                                            jr = (int)MessageLabels.JR.SUCCESS;

                                        if (world.worldID != cld.tryingToJoinWorld) break;

                                        else if (world.storedOnServer != TCPServer.externalServerIP)
                                        {
                                            
                                            Console.WriteLine($"Switching servers for client with userID: {cld.userID} and IP: {cld.IP}. External Server IP: {TCPServer.externalServerIP} Stored on server: {world.storedOnServer}");

                                            if (!BinaryHelper.GetBit(ref TCPServer.globalConfig.settingsflags8bit, (byte)SettingsFlag.HOST_LOCALLY))
                                            {
                                                Console.WriteLine("Server is hosted locally! Doing no server switch at all. Continuing...");
                                            }
                                            else if (!BinaryHelper.GetBit(ref TCPServer.globalConfig.settingsflags8bit, (byte)SettingsFlag.MULTI_SERVER_SUPPORT))
                                            {
                                                Console.WriteLine("Server is set to not support any subservers! Doing no server switch at all. Continuing...");
                                            }
                                            else
                                            {
                                                if (!server.mysqlManager.RequestSendQuery($"UPDATE players SET playingOnServer = '{world.storedOnServer}' WHERE ID = '{cld.GetUID()}'"))
                                                {
                                                    Console.WriteLine("switch : mysql fail!");
                                                    cld.stopReceiving = true; // no need disconnect, this will time it out...
//
// 
                                                    break;
                                                }

                                                BSONObject bObj = new BSONObject();
                                                bObj[MessageLabels.MessageID] = "OoIP";
                                                bObj["IP"] = world.storedOnServer;
                                                bObj["WN"] = world.WorldName;
                                                client.AddBSON(ref server, bObj);

                                                break;
                                            }
                                        }

                                        Console.WriteLine("Join result: " + jr.ToString());

                                        if (jr == 0 && world.worldID > 0)
                                        {
                                            if (!server.mysqlManager.RequestSendQuery($"UPDATE players SET playingOnServer = '{TCPServer.externalServerIP}', currentWorld = '{world.WorldName}' WHERE ID = '{cld.GetUID()}'"))
                                            {
                                                Console.WriteLine("Error at TCPEvents before HandleGetWorld, current server IP could not be assigned!");
                                                break;
                                            }

                                            cld.worldID = world.worldID;

                                            Console.WriteLine("Handling get world...");
                                            mHandler.HandleGetWorld(client, currentBObj, ref cld, world);
                                        }
                                        break;
                                    }
                                case MessageLabels.Ident.SetBackgroundBlock:
                                    {

                                        World w = cld.GetWorld(ref server);
                                        if (w == null) break;
                                        Console.WriteLine($"Set Background Block request from player with userID: {cld.userID} in worldID: {w.worldID} with worldName: {w.WorldName}");
                                        int x = currentBObj["x"], y = currentBObj["y"];
                                        if (w.ownerID != -1 && w.ownerID != cld.GetUID()) break;

                                        if (w.SetFG(x, y, (short)currentBObj["BlockType"]))
                                        {
                                            //client.AddBSON(ref server, currentBObj);
                                            currentBObj["U"] = cld.userID;
                                            client.BroadcastBSONInWorld(ref server, currentBObj, cld, false);
                                        }
                                        break;
                                    }
                                case "HA":
                                    {
                                        currentBObj["U"] = cld.userID;
                                        client.BroadcastBSONInWorld(ref server, currentBObj, cld, false);
                                        break;
                                    }
                                case MessageLabels.Ident.HitBackgroundBlock:
                                    {
                                        World w = cld.GetWorld(ref server);
                                        if (w == null) break;
                                        Console.WriteLine($"Hit Background Block request from player with userID: {cld.userID} in worldID: {w.worldID} with worldName: {w.WorldName}");
                                        int x = currentBObj["x"], y = currentBObj["y"];
                                        if (w.ownerID != -1 && w.ownerID != cld.GetUID()) break;

                                        int id = w.GetID(x, y, WorldInterface.LayerType.Background);

                                        if (id > 0) // check if block wasnt alrd none anyway lol
                                        {
                                            if (w.HitBG(x, y, (short)id))
                                            {
                                                if (w.SetBG(x, y, 0, true))
                                                { // handle hits soon...
                                                    BSONObject bObj = new BSONObject();
                                                    bObj[MessageLabels.MessageID] = "DB";
                                                    bObj[MessageLabels.DestroyBlockBlockType] = id;
                                                    bObj[MessageLabels.UserID] = cld.userID;
                                                    bObj["x"] = x;
                                                    bObj["y"] = y;

                                                    //client.AddBSON(ref server, bObj);

                                                    client.BroadcastBSONInWorld(ref server, bObj, cld, false);
                                                }
                                            }
                                        }
                                        break;
                                    }
                                case "A":
                                    {
                                        client.AddBSON(ref server, currentBObj);
                                        break;
                                    }
                                case MessageLabels.Ident.SetBlock:
                                    {
                                        World w = cld.GetWorld(ref server);
                                        if (w == null) break;
                                        Console.WriteLine($"Set Block request from player with userID: {cld.userID} in worldID: {w.worldID} with worldName: {w.WorldName}");
                                       

                                        int x = currentBObj["x"], y = currentBObj["y"];
                                        short blockType = (short)currentBObj["BlockType"];
                                        ItemDB.Item it = ItemDB.GetByID(blockType);
                                        if (w.ownerID != -1 && w.ownerID != cld.GetUID()) break;

                                        currentBObj["U"] = cld.userID;

                                        switch (it.type)
                                        {
                                            case 3:
                                                {
                                                    currentBObj[MessageLabels.MessageID] = MessageLabels.Ident.SetBlockWater;

                                                    if (w.SetL(x, y, blockType))
                                                    {
                                                        //client.AddBSON(ref server, currentBObj);

                                                        client.BroadcastBSONInWorld(ref server, currentBObj, cld, false);
                                                    }

                                                    break;
                                                }
                                            default:
                                                {
                                                    
                                                    if (w.SetFG(x, y, blockType))
                                                    {
                                                        //client.AddBSON(ref server, currentBObj);

                                                        client.BroadcastBSONInWorld(ref server, currentBObj, cld, false);

                                                        // tile extra (worlditemupdate/WIU) here, starting with a lock

                                                        if (ItemDB.ItemIsLock(it) && !w.HasTileLockAtXY(x, y)) // is it even a lock?
                                                        {
                                                            Console.WriteLine($"A lock for the world was placed, item ID: {it.ID}.");

                                                            ItemTileLockType lt = ItemDB.GetLockType(ref it);

                                                            WorldInterface.Lock l = new WorldInterface.Lock();
                                                            l.type = (byte)lt;

                                                            int spread = 0;

                                                            switch (lt)
                                                            {
                                                                case ItemTileLockType.SMALL:
                                                                    spread++;
                                                                    break;

                                                                case ItemTileLockType.BIG:
                                                                    spread = 2;
                                                                    break;

                                                                case ItemTileLockType.LARGE:
                                                                    spread = 3;
                                                                    break;

                                                                case ItemTileLockType.WORLD:
                                                                    w.ownerID = cld.GetUID();
                                                                    break;
                                                                default:
                                                                    Console.WriteLine($"Unknown tile lock type: {(byte)lt}");
                                                                    break;
                                                            }

                                                            if (spread > 0)
                                                            {
                                                                for (int sy = y - spread; sy <= y + spread; sy++)
                                                                {
                                                                    for (int sx = x - spread; sx <= x + spread; sx++)
                                                                    {
                                                                        if (w.HasTileLockAtXY(sx, sy))
                                                                        {
                                                                            
                                                                        }
                                                                        else
                                                                        {
                                                                            l.posX = (short)sx;
                                                                            l.posY = (short)sy;
                                                                            w.AddTileLock(l);
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            // else if ...
                                                        }
                                                    }

                                                    break;
                                                }
                                            break;
                                        }

                                        
                                        break;
                                    }
                                case MessageLabels.Ident.HitBlockWater:
                                    {
                                        World w = cld.GetWorld(ref server);
                                        if (w == null) break;
                                        Console.WriteLine($"Hit Block request from player with userID: {cld.userID} in worldID: {w.worldID} with worldName: {w.WorldName}");
                                       
                                        int x = currentBObj["x"], y = currentBObj["y"];
                                        int id = w.GetID(x, y, WorldInterface.LayerType.Water);
                                        if (w.ownerID != -1 && w.ownerID != cld.GetUID()) break;


                                        if (id > 0) // check if block wasnt alrd none anyway lol
                                        {
                                            if (w.HitL(x, y, (short)id))
                                            {
                                                if (w.SetL(x, y, 0, true))
                                                {
                                                    BSONObject bObj = new BSONObject();
                                                    bObj[MessageLabels.MessageID] = "DB";
                                                    bObj[MessageLabels.DestroyBlockBlockType] = id;
                                                    bObj[MessageLabels.UserID] = cld.userID;
                                                    bObj["x"] = x;
                                                    bObj["y"] = y;
                                                    //client.AddBSON(ref server, bObj);

                                                    client.BroadcastBSONInWorld(ref server, bObj, cld, false);
                                                }
                                            }
                                        }
                                        break;
                                    }
                                case "MWli":
                                    {
                                        int count = 0;
                                        string worldID = server.worldManager.GetWorldIDByName(currentBObj["WN"]);
                                        foreach (Socket s in server.GetSockets())
                                        {
                                            if (s == null) continue;
                                            if (!s.Connected) continue;

                                            ClientData cData = (ClientData)s.TryGetData(ref server);
                                            if (cData == null) continue;

                                            if (cData.worldID.ToString("X8") == worldID) count++;
                                        }

                                        currentBObj[MessageLabels.Count] = count;
                                        client.AddBSON(ref server, currentBObj);
                                        break;
                                    }
                                case MessageLabels.Ident.HitBlock:
                                    {
                                        World w = cld.GetWorld(ref server);
                                        if (w == null) break;
                                        Console.WriteLine($"Hit Block request from player with userID: {cld.userID} in worldID: {w.worldID} with worldName: {w.WorldName}");
                                        

                                        int x = currentBObj["x"], y = currentBObj["y"];
                                        int id = w.GetID(x, y, WorldInterface.LayerType.Block);
                                        if (w.ownerID != -1 && w.ownerID != cld.GetUID()) break;

                                        if (id > 0) // check if block wasnt alrd none anyway lol
                                        {
                                            if (w.HitFG(x, y, (short)id))
                                            {
                                                if (w.SetFG(x, y, 0, true))
                                                {
                                                    BSONObject bObj = new BSONObject();
                                                    bObj[MessageLabels.MessageID] = "DB";
                                                    bObj[MessageLabels.DestroyBlockBlockType] = id;
                                                    bObj[MessageLabels.UserID] = cld.userID;
                                                    bObj["x"] = x;
                                                    bObj["y"] = y;
                                                    //client.AddBSON(ref server, bObj);

                                                    client.BroadcastBSONInWorld(ref server, bObj, cld, false);
                                                }
                                            }
                                        }
                                        break;
                                    }
                                case "GSb":
                                    break;
                                /*case "MWli":
                                    {

                                        break;
                                    }*/
                                case MessageLabels.Ident.GetPlayerData:
                                    {
                                        Console.WriteLine("Client wants to get player data!");
                                        cld.cognito_Token = currentBObj[MessageLabels.CognitoToken];
                                        cld.cognito_ID = currentBObj[MessageLabels.CognitoId];

                                        foreach (Socket s in server.GetSockets())
                                        {
                                            if (s == null) continue;
                                            lock (s)
                                            {
                                                if (s == client) continue;
                                                if (!s.Connected) continue;

                                                ClientData cData = (ClientData)s.TryGetData(ref server);
                                                if (cData == null) continue;

                                                if (cData.cognito_ID == cld.cognito_ID)
                                                {
                                                    Console.WriteLine("Player tried to login while it's account was online already!");

                                                    cld.stopReceiving = true;
                                                    client.Shutdown(SocketShutdown.Send);
                                                    client.BeginDisconnect(false, HandleOnDisconnect, peer);
                                                    Socket sock = server.UnregisterClient(cData.session);

                                                    if (sock == null)
                                                    {
                                                        Console.WriteLine("Socket was null, so manually removing the rest...");
                                                        World w = cData.GetWorld(ref server);
                                                        if (w != null) w.RemoveFromQueue(cData.userID);

                                                        server.DeleteData(cData.session);
                                                        Socket cSock = server.DeleteClientSocketFromList(cld.session);

                                                        if (cSock != null)
                                                            server.DeletePacketQueue(cSock);

                                                        Console.WriteLine("Removed the socket!");
                                                    }

                                                    goto LABEL_SKIP_PROCESS;
                                                }
                                            }
                                        }

                                      
                                        Console.WriteLine($"User logon with CognitoID: {cld.cognito_ID} CognitoToken: {cld.cognito_Token}");

                                        if (MysqlManager.HasIllegalChar(cld.cognito_ID) || (MysqlManager.HasIllegalChar(cld.cognito_Token) && cld.cognito_Token != "") || MysqlManager.HasIllegalChar(cld.IP))
                                        {
                                            Console.WriteLine("One of the cognito values contained an illegal character!!! Aborted.");

                                            client.Shutdown(SocketShutdown.Send);
                                            client.BeginDisconnect(false, HandleOnDisconnect, peer);
                                            return;
                                        }

                                        Console.WriteLine("Moving on to requesting the player data from sql db...");

                                        List<Dictionary<string, string>> table = server.mysqlManager.RequestFetchQuery($"SELECT * FROM players WHERE CognitoID = '{cld.cognito_ID}'");
                                        if (table == null)
                                        {
                                            Console.WriteLine("At getplayerdata: table was null, check mysql connection?");
                                            break;
                                        }

                                        int results = table.Count;

                                        Console.WriteLine("Found results at table: " + results.ToString());

                                        if (results > 0)
                                        {
                                            cld.SetDataFromTable(table);

                                            if (cld.region == "" || cld.recentIP != cld.IP)
                                            {
                                                Console.WriteLine($"Region was unprovided after login, attempting to re-fetch it... Recent IP: {cld.recentIP}, current IP: {cld.IP}");
                                                if (!BinaryHelper.GetBit(ref TCPServer.globalConfig.settingsflags8bit, (byte)SettingsFlag.HOST_LOCALLY))
                                                {
                                                    IpInfo ipInfo = IPLookup.GetIPLookup(cld.IP);

                                                    if (ipInfo.timezone != null)
                                                    {
                                                        string regi = ipInfo.timezone.Split("/")[0];

                                                        Console.WriteLine($"Region of IP '{cld.IP}' Detected: {regi}");
                                                        cld.region = regi;
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("Error while trying to get timezone, timezone variable was null!");
                                                    }
                                                }
                                            }

                                            string recentServer = table[0]["playingOnServer"];
                                            if (recentServer != "" && BinaryHelper.GetBit(ref TCPServer.globalConfig.settingsflags8bit, (byte)SettingsFlag.HOST_LOCALLY) && BinaryHelper.GetBit(ref TCPServer.globalConfig.settingsflags8bit, (byte)SettingsFlag.MULTI_SERVER_SUPPORT))
                                            {
                                                if (table[0]["playingOnServer"] != TCPServer.externalServerIP)
                                                {
                                                    // TODO!
                                                    string cw = table[0]["currentWorld"];
                                                    Console.WriteLine("Client has been retraced from a different server, also was in the world: " + cw);

                                                    if (cw != "")
                                                    {
                                                        BSONObject bObj = new BSONObject();
                                                        bObj[MessageLabels.MessageID] = "OoIP";
                                                        bObj["IP"] = table[0]["playingOnServer"];
                                                        bObj["WN"] = cw;
                                                        client.AddBSON(ref server, bObj);
                                                        client.ReleaseBSON(ref server);
                                                        
                                                        break;
                                                    }
                                                }
                                            }

                                            Console.WriteLine("User was retraced, ID: " + cld.userID + " USERNAME: " + table[0]["UserName"] + ".");
                                        }
                                        else if (results == 0)
                                        {
                                            Console.WriteLine("Attempting to register the user into the db...");

                                            if (!BinaryHelper.GetBit(ref TCPServer.globalConfig.settingsflags8bit, (byte)SettingsFlag.HOST_LOCALLY))
                                            {
                                                IpInfo ipInfo = IPLookup.GetIPLookup(cld.IP);

                                                if (ipInfo.timezone != null)
                                                {
                                                    string regi = ipInfo.timezone.Split("/")[0];

                                                    Console.WriteLine($"Region of IP '{cld.IP}' Detected: {regi}");
                                                    cld.region = regi;
                                                }
                                                else
                                                {
                                                    Console.WriteLine("Error while trying to get timezone, timezone variable was null!");
                                                }
                                            }

                                            bool success = server.mysqlManager.RequestSendQuery(string.Format("INSERT INTO players (CognitoID, Token, UserName, PasswordHash, IP, gems, bytecoins, region) VALUES('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}')",
                                                cld.cognito_ID, cld.cognito_Token, "Dummy", "", cld.IP, cld.gems, cld.bytecoins, cld.region));

                                            if (success)
                                            {
                                                Console.WriteLine("At GetPlayerData: No user was found from the db. Registered him/her into the database! Obtaining new data...");
                                                table = server.mysqlManager.RequestFetchQuery($"SELECT * FROM players WHERE CognitoID = '{cld.cognito_ID}'");

                                                if (table.Count > 0)
                                                {
                                                    cld.SetDataFromTable(table);
                                                }
                                                else
                                                {
                                                    cld.stopReceiving = true;
                                                    client.Shutdown(SocketShutdown.Both);
                                                    client.BeginDisconnect(false, HandleOnDisconnect, peer);

                                                    Console.WriteLine("Fatal error occured while registering, user should have been able to obtain new data after register but apparently that wasnt the case??");
                                                    goto LABEL_SKIP_PROCESS;
                                                }
                                            }
                                            else
                                            {
                                                Console.WriteLine("At GetPlayerData: Trying to register user into the db failed.");
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine("Invalid amount of users found.");
                                        }

                                        // find best suited region server for player
                                        //string reg = cld.region.ToLower();
                                       

                                        mHandler.HandleGetPlayerData(client, currentBObj, ref cld);
                                        break;
                                    }
                                case MessageLabels.Ident.SyncTime:
                                    {
                                        BSONObject tObj = new BSONObject();
                                        tObj[MessageLabels.MessageID] = MessageLabels.Ident.SyncTime;
                                        tObj[MessageLabels.TimeStamp] = KukouriTools.GetTime();
                                        tObj[MessageLabels.SequencingInterval] = 60;
                                        client.AddBSON(ref server, tObj);
                                        break;
                                    }
                                default:
                                    {
                                        client.Ping(ref server, ref cld);
                                        break;
                                    }
                            }
                        }
                        //}
                    }
                    catch (Exception ex)
                    {
                        RaiseInternalError($"Couldnt retrieve BSONObject from client packet or receiving had to be aborted, further reason: {ex}");
                    }

                    //Console.WriteLine("Repolling...");

                    //Array.Fill<byte>(peer.buffer, 0);

                }
                else // ping to check if client is healthy/alive
                {
                    Console.WriteLine("No data available.");
                    client.Ping(ref server, ref cld);
                }

            LABEL_SKIP_PROCESS:
                client.ReleaseBSON(ref server);

                if (cld.hasSpawned && cld.worldID > 0)
                {
                    World w = cld.GetWorld(ref server);
                    if (w != null)
                    {
                        w.ReleaseQueue(ref server, client, cld);
                    }
                }
                else if (cld.worldID > 0 && !cld.hasSpawned)
                {
                    cld.worldEnterTimeout++;
                    if (cld.worldEnterTimeout >= 16)
                    {
                        cld.worldEnterTimeout = 0;
                        Console.WriteLine("PLAYER EXCEEDED WORLD ENTER TIMEOUT!!!!!!!!!!!!!!!!!!!!");
                        World w = cld.GetWorld(ref server);
                        if (w != null) w.ReleaseQueue(ref server, client, cld);

                        cld.stopReceiving = true;
                        client.Shutdown(SocketShutdown.Both);
                        client.BeginDisconnect(false, HandleOnDisconnect, peer);
                        return;
                    }
                }

                client.SetData(ref server, cld);

            LABEL_SKIP_EVERYTHING:

                lock (client)
                {
                    try
                    {
                        if (client.Connected && !cld.stopReceiving)
                        {
                            client.BeginReceive(peer.buffer, 0, peer.buffer.Length, SocketFlags.None, OnReceive, peer);
                        }
                        else
                        {
                            Console.WriteLine("Error client was not connected!");
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        Console.WriteLine("Client was disposed, huh?");
                    }
                    catch (SocketException ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    catch (KeyNotFoundException ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            else
            {
                try
                {
                    cld.stopReceiving = true;
                    client.Shutdown(SocketShutdown.Both);
                    client.BeginDisconnect(false, HandleOnDisconnect, peer);
                }
                catch (SocketException ex) {}
                catch (ObjectDisposedException ex) { }
            }
            //Console.WriteLine($"Message length that client claimed to be: {allegedLength}");
        }

       
        public void HandleOnDisconnect(IAsyncResult AR) // A disconnect that the client itself acknowledges.
        {
            if (server.isShuttingDown) return;
            Console.WriteLine("Processing disconnect... [HandleOnDisconnect]");
            HandleDisconnect((StateObject)AR.AsyncState);
        }

        public void HandleDisconnect(StateObject stateObj)
        {
            Socket sock = stateObj.currentSocket;
            if (sock != null)
            {
                try
                {
                    if (stateObj.instantClose)
                        sock.Close();
                }
                catch (ObjectDisposedException)
                {

                }
            }
            else
            {
                Console.WriteLine("sock was null so nothing was really done... [HandleDisconnect]");
                return;
            }
            Console.WriteLine("A client just disconnected. [HandleDisconnect]");
        }
    }
}
