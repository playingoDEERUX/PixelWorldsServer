using Kernys.Bson;
using PixelWorldsServer.DataManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using PixelWorldsServer.WorldSystem;

namespace PixelWorldsServer.Networking.Pixel_Worlds
{
    class MessageHandler
    {
        private TCPServer server;
        private TCPEvents serverEvents;
        public MessageHandler(ref TCPServer _server, TCPEvents _serverEvents)
        {
            server = _server;
            serverEvents = _serverEvents;
            Console.WriteLine(server == null ? "Error in MessageHandler, server was null!" : "MessageHandler for individual bson events has been initialized!");
        }

        public void HandleGetPlayerData(Socket client, BSONObject bsonData, ref ClientData cld)
        {
            
            int category = bsonData[MessageLabels.Category];
            string token = bsonData[MessageLabels.CognitoToken];
            string cognitoId = bsonData[MessageLabels.CognitoId];

            byte[] gpdbuffer = serverEvents.GPDCache == null ? serverEvents.GPDCache = File.ReadAllBytes("player.dat") : serverEvents.GPDCache;
            BSONObject pObj = SimpleBSON.Load(gpdbuffer.Skip(4).ToArray())["m0"] as BSONObject;

            BSONObject pd = SimpleBSON.Load(pObj["pD"]);
            pd[MessageLabels.PlayerData.ByteCoinAmount] = cld.bytecoins;
            pd[MessageLabels.PlayerData.GemsAmount] = cld.gems;
            pd[MessageLabels.PlayerData.Username] = cld.userName;
            pd[MessageLabels.PlayerData.PlayerOPStatus] = 2;
            pd[MessageLabels.PlayerData.InventorySlots] = 400;
           
            byte[] inven = new byte[20 * 6];

            int x = 0;
            for (int j = 0; j < inven.Length; j += 6)
            {
                int itemID = 0;
                bool isntWing = false;

                switch (x)
                {
                    case 0:
                        itemID = 791;
                        break;

                    case 1:
                        itemID = 880;
                        break;

                    case 2:
                        itemID = 881;
                        break;
                    case 3:
                        itemID = 215;
                        break;

                    case 4:
                        itemID = 586;
                        break;

                    case 5:
                        itemID = 608;
                        break;

                    case 6:
                        itemID = 724;
                        break;

                    case 7:
                        itemID = 877;
                        break;
                    case 8:
                        itemID = 4266;
                        break;
                    case 9:
                        itemID = 4267;
                        break;
                    case 10:
                        itemID = 4268;
                        break;
                    default:
                        itemID = x;
                        isntWing = true;
                        break;
                }

                Buffer.BlockCopy(BitConverter.GetBytes(itemID), 0, inven, j, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(isntWing ? 0 : 1024), 0, inven, j + 2, 2);
                Buffer.BlockCopy(BitConverter.GetBytes((short)1337), 0, inven, j + 4, 2);
                x++;
            }

            pd["inv"] = inven;

            /*List<int> spotsList = pd["spots"].int32ListValue;
            foreach (int s in spotsList)
            {
                Console.WriteLine("SPOT: " + s.ToString());
            }

            foreach (string key in pd.Keys)
            {
                BSONValue bVal = pd[key];
                switch (bVal.valueType)
                {
                    case BSONValue.ValueType.String:
                        Console.WriteLine("[INVDATA] >> KEY: " + key + " VALUE: " + bVal.stringValue);
                        break;
                    case BSONValue.ValueType.Object:
                        {
                            Console.WriteLine("[INVDATA] >> KEY: " + key);
                            // that object related shit is more complex so im gonna leave that for later
                            break;
                        }
                    case BSONValue.ValueType.Int32:
                        Console.WriteLine("[INVDATA] >> KEY: " + key + " VALUE: " + bVal.int32Value);
                        break;
                    case BSONValue.ValueType.Int64:
                        Console.WriteLine("[INVDATA] >> KEY: " + key + " VALUE: " + bVal.int64Value);
                        break;
                    case BSONValue.ValueType.Double:
                        Console.WriteLine("[INVDATA] >> KEY: " + key + " VALUE: " + bVal.doubleValue);
                        break;
                    case BSONValue.ValueType.Boolean:
                        Console.WriteLine("[INVDATA] >> KEY: " + key + " VALUE: " + bVal.boolValue.ToString());
                        break;
                    default:
                        Console.WriteLine("[INVDATA] >> KEY: " + key + " VALUE (unknown, only type): " + bVal.valueType.ToString());
                        break;
                }
            }*/

            //Console.WriteLine("INV KEYS: " + bInvData.Count.ToString());

            pd[MessageLabels.UserID] = cld.userID;
            pObj["rUN"] = cld.nickName;
            pObj["pD"] = SimpleBSON.Dump(pd);
            pObj[MessageLabels.UserID] = cld.userID;

            client.AddBSON(ref server, pObj);
        }

        public void HandleWorldChatMessage(Socket client, BSONObject bsonData, ref ClientData cld)
        {
            /*MESSAGE ID: OoIP
[SERVER] >> KEY: ID VALUE: OoIP
[SERVER] >> KEY: IP VALUE: ec2-54-86-141-80.compute-1.amazonaws.com
[SERVER] >> KEY: WN VALUE: DEIVIDAS132089*/

            if (cld.worldID < 1) return;
            string msg = bsonData["msg"];

            if (msg == "") return;
            if (cld.cognito_ID == "")
            {
                Console.WriteLine("Hmm that's weird, the user doesn't seem to have a cognito id. That is bad.");
                return;
            }

            BSONObject bObj = new BSONObject();
            bObj["ID"] = "BGM";

            string[] tokens = msg.Split(" ");
            int tokCount = tokens.Count();

            if (tokCount <= 0) return;
            if (tokens[0].Length <= 1) return;

            World w = cld.GetWorld(ref server);
            if (w == null) return;

            if (tokens[0][0] == '/') 
            {
                switch (tokens[0])
                {
                    case "/help":
                        {
                            bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Commands >> /help /give /find /findany /dummycommand /status");
                            break;
                        }
                    case "/register":
                        {
                            if (tokCount == 3)
                            {
                                string username = tokens[1], password = tokens[2];

                                Console.WriteLine($"Player attempted to register with username: {username} and pass: {password}.");

                                if (username == "" || password == "")
                                {
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Username or password may not be empty!");
                                    break;
                                }

                                if (MysqlManager.HasIllegalChar(username) || MysqlManager.HasIllegalChar(password) || MysqlManager.ContainsAnyChars(username, "{}:\\-.=/+"))
                                {
                                    // block this, not because of sql injection or anything (queries are prepared anyway), but cuz i dont want players to occupy weird names
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Illegal name or pass, no spaces or special symbols in username please! For the pass you can use them though.");
                                    break;
                                }

                                //string currentNameLower = cld.userName.ToLower();

                                if (cld.isRegistered)
                                {
                                    Console.WriteLine($"Aborted at /register due to player already being registered. Username is: {cld.userName}");
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "This account is registered already, consider playing on a new account via a different device or by reinstalling pixelworlds first.");
                                    break;
                                }

                                // does an entry with the same username exist already?
                                List<Dictionary<string, string>> table = server.mysqlManager.RequestFetchQuery($"SELECT * FROM players WHERE UserName = '{username}'");
                                if (table == null)
                                {
                                    Console.WriteLine("Table null at MessageHandler.cs during /register. This should not happen at all so not proceeding.");
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "An error occured. Please contact DEERUX#1551 immediately.");
                                    break;
                                }
                                else if (table.Count > 0)
                                {
                                    Console.WriteLine($"Requested registration for account was unsuccessful because the account existed already as a record. Count of records: {table.Count}.");
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Account exists already.");
                                    break;
                                }

                                // do the next check which is ip limit...
                                table = null;
                                table = server.mysqlManager.RequestFetchQuery($"SELECT * FROM players WHERE IP = '{cld.IP}'");

                                if (table == null)
                                {
                                    Console.WriteLine("Table null at MessageHandler.cs during /register (2). This should not happen at all so not proceeding.");
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "An error occured. Please contact DEERUX#1551 immediately.");
                                    break;
                                }
                                else if (table.Count > 3)
                                {
                                    Console.WriteLine($"Too many accounts created with IP: '{cld.IP}'.");
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "You have created too many accounts from this device or location, try again later.");
                                    break;
                                }


                                // seems like we can go on, everything is OK
                                cld.userName = username;
                                server.mysqlManager.RequestSendQuery($"UPDATE players SET UserName = '{cld.userName}', PasswordHash = md5('salt_432G{password}728') WHERE CognitoID = '{cld.cognito_ID}'");
                                Console.WriteLine($"Success register from IP: '{cld.IP}'.");
                                bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Successfully registered!");
                            }
                            break;
                        }
                    case "/find":
                        {
                            bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Usage: /find (item name)");
                            if (tokCount > 1)
                            {
                                string item_query = tokens[1];
                                
                                if (item_query.Length < 2)
                                {
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FFFF00>System", cld.worldID.ToString("X8"), w.WorldName, 1, $"Please enter an item name with more than 2 characters!");
                                    break;
                                }

                                ItemDB.Item[] items = ItemDB.FindByName(item_query);
                                if (items != null)
                                {
                                    if (items.Length > 0)
                                    {
                                        string found = "";

                                        foreach (ItemDB.Item it in items)
                                        {
                                            found += $"\nItem Name: {it.name}   ID: {it.ID}";
                                        }

                                        bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FFFF00>System", cld.worldID.ToString("X8"), w.WorldName, 1, $"Found items:{found}");
                                    }
                                    else
                                    {
                                        bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FFFF00>System", cld.worldID.ToString("X8"), w.WorldName, 1, $"No item starting with '{item_query}' was found.");
                                    }
                                }
                            }
                            
                            break;
                        }
                    case "/findany":
                        {
                            bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FFFF00>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Usage: /find (item name)");
                            if (tokCount > 1)
                            {
                                string item_query = tokens[1];

                                if (item_query.Length < 2)
                                {
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FFFF00>System", cld.worldID.ToString("X8"), w.WorldName, 1, $"Please enter an item name with more than 2 characters!");
                                    break;
                                }

                                ItemDB.Item[] items = ItemDB.FindByAnyName(item_query);
                                if (items != null)
                                {
                                    if (items.Length > 0)
                                    {
                                        string found = "";

                                        foreach (ItemDB.Item it in items)
                                        {
                                            found += $"\nItem Name: {it.name}   ID: {it.ID}";
                                        }

                                        bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FFFF00>System", cld.worldID.ToString("X8"), w.WorldName, 1, $"Found items:{found}");
                                    }
                                    else
                                    {
                                        bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FFFF00>System", cld.worldID.ToString("X8"), w.WorldName, 1, $"No item containing '{item_query}' was found.");
                                    }
                                }
                            }

                            break;
                        }
                    case "/give":
                        {
                            
                            if (tokCount > 3)
                            {
                                string paramUserID = tokens[1];
                                ushort paramQuantity;
                                if (!ushort.TryParse(tokens[2], out paramQuantity))
                                {
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Usage: /give (userID) (quantity) (item name)");
                                    break;
                                }

                                string paramItemName = "";

                                // add restly shit
                                for (int i = 3; i < tokCount; i++)
                                {
                                    paramItemName += (tokens[i]);
                                    if (i != tokCount - 1) paramItemName += " ";
                                }

                                
                                Console.WriteLine($"Player with userID: {cld.userID} attempts to give item with name: {paramItemName} with quantity: {paramQuantity} to userID: {paramUserID}");

                                bool couldFind = false;
                                foreach (Socket s in server.GetSockets())
                                {
                                    if (s == null) continue;
                                    lock (s)
                                    {
                                        if (!s.Connected) continue;
                                        ClientData cData = (ClientData)s.TryGetData(ref server);
                                        if (cData == null) continue;

                                        if (cData.userID == paramUserID)
                                        {
                                            ItemDB.Item it = ItemDB.GetByName(paramItemName);

                                            BSONObject cObj = new BSONObject();

                                            cObj[MessageLabels.MessageID] = "nCo";
                                            cObj["CollectableID"] = 1;
                                            cObj["BlockType"] = it.ID;
                                            cObj["Amount"] = paramQuantity;
                                            cObj["InventoryType"] = it.type;
                                            cObj["PosX"] = cData.posX * 3.125d; /*cld.posX = world.spawnPointX / 3.125d;
            cld.posY = world.spawnPointY / 3.181d;*/
            cObj["PosY"] = cData.posY * 3.181d;
                                            cObj["IsGem"] = false;
                                            cObj["GemType"] = 0;
                                            s.AddBSON(ref server, cObj);

                                            cObj[MessageLabels.MessageID] = "C";
                                            s.AddBSON(ref server, cObj);

                                            
                                            

                                            bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, $"Given  {paramQuantity} {it.name}. Rarity: (unknown)");
                                           
                                            Console.WriteLine($"Given item with ID: {it.ID}.");
                                            couldFind = true;
                                            break;
                                        }
                                    }
                                }

                                if (!couldFind)
                                {
                                    bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, $"Could not find userID: {paramUserID}");
                                }
                            }
                            else
                            {
                                bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Usage: /give (userID) (quantity) (item name)");
                            }
                            break;
                        }
                    case "/dummycommand":
                        {
                            bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "This is a test command. If you see a message that popped up on your message history, then this was a success!");
                            break;
                        }
                    case "/edit":
                        {
                            bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Command maintenanced for now. (Error command type: COMMAND_MAINTENANCE)");
                            break;
                        }
                    default:
                        {
                            bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage("<color=#FF0000>System", cld.worldID.ToString("X8"), w.WorldName, 1, "Unknown command.");
                            break;
                        }
                }
            }
            else 
            {
                bObj["ID"] = "WCM";
                bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage(cld.nickName, cld.userID, "#" + w.WorldName, 0, msg);
                client.BroadcastBSONInWorld(ref server, bObj, cld, true, false);
                return;
                // normal chat message lol
            }
            client.AddBSON(ref server, bObj);
        }

        public void HandleRequestOtherPlayers(Socket client, BSONObject bsonData, ref ClientData cld)
        {

        }

        public void HandleRequestOtherPlayers(Socket client, BSONObject bsonData, ref ClientData cld, World world) // current world
        {
            if (cld.worldID < 1) return;

            
        }
        public void HandleGetWorld(Socket client, BSONObject bsonData, ref ClientData cld, World world)
        {
            string worldName = bsonData["W"];
            Console.WriteLine("Requested World with Name: " + worldName);
            BSONObject bsonObj = new BSONObject();
            
            BSONObject worldObj = world.Serialize();

            byte[] wData = SimpleBSON.Dump(worldObj);
            
            bsonObj[MessageLabels.MessageID] = MessageLabels.Ident.GetWorldCompressed;
            bsonObj["W"] = LZMAHelper.CompressLZMA(wData);
            client.AddBSON(ref server, bsonObj);

            cld.posX = world.spawnPointX / 3.125d;
            cld.posY = world.spawnPointY / 3.181d;

            long time = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            int onlineCount = 0;

            foreach (Socket s in server.GetSockets())
            {
                if (s == null) continue;
                if (!s.Connected) continue;
                onlineCount++;
            }

            BSONObject bObj = new BSONObject();
            bObj[MessageLabels.MessageID] = MessageLabels.Ident.BroadcastGlobalMessage;
            bObj[MessageLabels.ChatMessageBinary] = Chat.CreateChatMessage($"<color=#7FFFD4>pixelmmfi", "0000000", worldName, 1, $"Attempting to join world {worldName}... Pixel Worlds Private Server v0.1.5 beta by playingo - Cheers. Discord: DEERUX#1551 PLAYERS ONLINE: {onlineCount}");
            client.AddBSON(ref server, bObj);
        }

        public void HandleTryToJoinWorld(Socket client, BSONObject bsonData, ref ClientData cld)
        {
            string worldName = bsonData["W"];
            Console.WriteLine("Handling try to join world for World Name: " + worldName);
            // prepare bson response
            BSONObject resp = new BSONObject();
            resp[MessageLabels.MessageID] = MessageLabels.Ident.TryToJoinWorld;

            int jr = 5;

            if (MysqlManager.HasIllegalChar(worldName))
            {
                jr = (int)MessageLabels.JR.INVALID_NAME;
            }
            else
            {
                string reg = cld.region.ToLower();

                string subIP = TCPServer.externalServerIP;
                string subregion = "europe";
                string worldID = server.worldManager.GetWorldIDByName(worldName);

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
                {
                    if (cld.worldID > 1) cld.GetWorld(ref server).RemoveFromQueue(cld.userID);
                    //cld.worldID = world.worldID;
                    cld.tryingToJoinWorld = world.worldID;
                    cld.hasSpawned = false;
                    jr = (int)MessageLabels.JR.SUCCESS;
                }
            }

            resp[MessageLabels.JoinResult] = jr;
            client.AddBSON(ref server, resp);
        }
    }
}
