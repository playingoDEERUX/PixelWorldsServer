using System;
using System.Collections.Generic;
using System.Text;
using PixelWorldsServer.Networking;
using PixelWorldsServer.DataManagement;
using System.Linq;
using System.IO;

namespace PixelWorldsServer.WorldSystem
{
    class WorldManager
    {
        private byte version = 0x1;
        private Dictionary<string, World> globalWorlds = new Dictionary<string, World>();
        private TCPServer server;
        
        public WorldManager (TCPServer _server)
        {
            server = _server;
            Console.WriteLine("WorldManager is set up!");
        }

        public World[] GetCachedWorlds()
        {
            lock (globalWorlds)
            {
                return globalWorlds.Values.ToArray();
            }
        }

        public void SaveAll()
        {
            Console.WriteLine("Saving all worlds...");
            lock (globalWorlds)
            {
                foreach (World w in globalWorlds.Values)
                {
                    Console.WriteLine($"Found cached world: {w.WorldName}, saving it...");
                    using (MemoryStream ms = new MemoryStream())
                    {
                        ms.WriteByte(version); // version

                        for (int x = 0; x < w.GetBoundsX(); x++) 
                        {
                            for (int y = 0; y < w.GetBoundsY(); y++) 
                            {
                                ms.Write(BitConverter.GetBytes(w.GetID(x, y, WorldInterface.LayerType.Block)));
                                ms.Write(BitConverter.GetBytes(w.GetID(x, y, WorldInterface.LayerType.Background)));
                                ms.Write(BitConverter.GetBytes(w.GetID(x, y, WorldInterface.LayerType.Water)));
                                ms.Write(BitConverter.GetBytes(w.GetID(x, y, WorldInterface.LayerType.Wiring)));
                            }
                        }

                        File.WriteAllBytes("database/worlds/" + w.worldID.ToString() + ".map", LZMAHelper.CompressLZMA(ms.ToArray()));
                    }
                }
            }
        }

        public bool AddWorld(World world) // returns whether world was already cached...
        {
            lock (globalWorlds)
            {
                if (globalWorlds.ContainsKey(world.worldID.ToString("X8")))
                    return true;
                else
                    globalWorlds.Add(world.worldID.ToString("X8"), world);
            }
            return false;
        }
        public World GetWorld(string providedIP, string worldName, string IDKey = "NONE") // uses worldID mainly, but will use worldName for tracking or creating world it self once worldID is not provided
        {
            if (server == null) return null;
            if (worldName.Length > 32) return null;
            
            string ID = IDKey;
            lock (globalWorlds)
            {
                if (ID == "NONE")
                {
                    World w = CreateWorld(providedIP, worldName, 80, 60);
                    if (w != null)
                    {
                        w.storedOnServer = providedIP;
                        w.GenerateEnvironment();
                        AddWorld(w);
                    }
                    return w;
                }
                else if (!globalWorlds.ContainsKey(IDKey))
                {
                    // check if world exists in mysql db first, if yes, serialize and add that one to cache
                    List<Dictionary<string, string>> table = server.mysqlManager.RequestFetchQuery($"SELECT * FROM worlds WHERE worldName = '{worldName}'");

                    World w;

                    if (table.Count > 0)
                        w = World.FromTable(table);
                    else
                        w = CreateWorld(providedIP, worldName, 80, 60);
                    

                    if (w != null)
                    {
                        if (w.storedOnServer == "")
                            w.storedOnServer = TCPServer.externalServerIP;

                        if (w.storedOnServer == TCPServer.externalServerIP)
                        {
                            if (!File.Exists("database/worlds/" + w.worldID.ToString() + ".map"))
                                w.GenerateEnvironment();
                            else
                                w.LoadEnvironment();
                        }
                        
                        AddWorld(w);
                    }
                    return w;
                }
                else
                {
                    return globalWorlds[IDKey];
                }
            }
        }

        public bool IsWorldCached(string worldID) 
        {
            lock (globalWorlds)
            {
                return globalWorlds.ContainsKey(worldID);
            }
        }
        public string GetWorldIDByName(string worldName, bool instantMySqlReq = false)
        {
            if (server == null) return "";
            int worldId = -1;

            if (worldName.Contains("\\") || worldName.Contains(".") || worldName.Contains("-") || worldName.Contains("/") || worldName.Contains("=") || worldName.Contains("+")) return "";
            if (MysqlManager.HasIllegalChar(worldName)) return "";

            if (!instantMySqlReq)
            {
                lock (globalWorlds)
                {
                    foreach (World world in globalWorlds.Values)
                    {
                        if (world == null) continue; // ???
                        if (world.WorldName == worldName)
                        {
                            worldId = world.worldID;
                            break;
                        }
                    }
                }
            }

            if (worldId == -1) // still -1? contact mysql server
            {
                List<Dictionary<string, string>> table = server.mysqlManager.RequestFetchQuery($"SELECT * FROM worlds WHERE worldName = '{worldName}'");
                if (table.Count > 0)
                    worldId = int.Parse(table[0]["ID"]);
            }

            return worldId == -1 ? "NONE" : worldId.ToString("X8");
        }

        public int GetWorldIDFromName(string worldName) // difference: provides worldID in int format
        {
            int worldId = Convert.ToInt32(GetWorldIDByName(worldName), 16);
            return worldId;
        }

        private World CreateWorld(string destIP, string name, short sizeX, short sizeY, int type = 0)
        {
           
            // create new world with random shit in mysql database and use its latest ID
            string wName = name.ToUpper();
            if (server == null || MysqlManager.HasIllegalChar(wName)) return null;

            int worldId = -1;
            short spX = (short)Randomizer.RandomInt(30, 60), spY = (short)Randomizer.RandomInt(20, 30);

            if (server.mysqlManager.RequestSendQuery(string.Format("REPLACE INTO worlds (worldName, sizeX, sizeY, spawnPointX, spawnPointY, cachedOnServer) VALUES('{0}', '{1}', '{2}', '{3}', '{4}', '{5}')",
                wName, sizeX, sizeY, spX, spY, destIP)))
            {
                worldId = GetWorldIDFromName(wName);
            }
            else
            {
                return null;
            }
            return worldId < 0 ? null : new World(worldId, sizeX, sizeY, wName, spX, spY);
        }
    }
}
