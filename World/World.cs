using Kernys.Bson;
using PixelWorldsServer.DataManagement;
using PixelWorldsServer.Networking.Pixel_Worlds;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using PixelWorldsServer.Networking;
using static PixelWorldsServer.WorldSystem.WorldInterface;

namespace PixelWorldsServer.WorldSystem
{
    class World : WorldInterface
    {
        private ConcurrentDictionary<string, BSONObject> worldPacketQueue = new ConcurrentDictionary<string, BSONObject>(); // userID, BSONPacket
        private List<Lock> tileLocks = new List<Lock>(); // except wl, wl doesnt count

        public Lock[] GetTileLocks()
        {
            lock (tileLocks)
            {
                return tileLocks.ToArray();
            }
        }

        public bool HasTileLockAtXY(int x, int y)
        {
            Lock[] locks = GetTileLocks();

            foreach (Lock l in locks)
            {
                if (l.posX == x && l.posY == y || l.claimedArea.Contains(new LockPoint(x, y)))
                    return true;
            }
            return false;
        }

        public void AddTileLock(Lock l)
        {
            lock (tileLocks)
            {
                tileLocks.Add(l);
            }
        }

        public bool RemoveTileLockByID(int id)
        {
            Lock[] locks = GetTileLocks();
            if (locks.Length > id)
            {
                lock (tileLocks)
                {
                    tileLocks.RemoveAt(id);
                    return true;
                }
            }
            return false;
        }

        public bool RemoveTileLockByXY(short x, short y)
        {
            Lock[] locks = GetTileLocks();

            for (int i = 0; i < locks.Length; i++)
            {
                if (locks[i].posX == x && locks[i].posY == y)
                {
                    lock (tileLocks)
                    {
                        tileLocks.RemoveAt(i);
                    }
                    return true;
                }
            }
            return false;
        }


        public int GetPlayerCount(ref TCPServer server)
        {
            int c = 0;

            foreach (Socket s in server.GetSockets())
            {

                if (s == null) continue;
                lock (s)
                {
                    if (!s.Connected) continue;
                    ClientData cData = (ClientData)s.TryGetData(ref server);
                    if (cData == null) continue;
                    if (worldID == cData.worldID)
                        c++;
                }
            
            }

            return c;
        }
        public static World FromTable(List<Dictionary<string, string>> table)
        {
            Dictionary<string, string> dict = table[0];

            World w = new World(int.Parse(dict["ID"]), short.Parse(dict["sizeX"]), short.Parse(dict["sizeY"]), dict["worldName"], short.Parse(dict["spawnPointX"]), short.Parse(dict["spawnPointY"]));
            w.ownerID = int.Parse(dict["ownerID"]);
            w.storedOnServer = dict["cachedOnServer"];
            return w;
        }

        public void LoadEnvironment()
        {
            int sizeX = GetBoundsX();
            int sizeY = GetBoundsY();
            Console.WriteLine($"Loading world environment for the first time, sizeX/sizeY: {sizeX}/{sizeY}");

            byte[] worldData = LZMAHelper.DecompressLZMA(File.ReadAllBytes("database/worlds/" + worldID.ToString() + ".map"));
            if (worldData.Length < ((sizeX * sizeY) * 8) + 1) 
            {
                Console.WriteLine("ERROR: World Data Length was too small to be serialized!");
                return;
            }

            byte worldVer = worldData[0];
            Console.WriteLine($"World version is: {worldVer}");

            int pos = 1;
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    tiles[x, y].foregroundID = BitConverter.ToInt16(worldData, pos); pos += 2;
                    tiles[x, y].backgroundID = BitConverter.ToInt16(worldData, pos); pos += 2;
                    tiles[x, y].liquidID = BitConverter.ToInt16(worldData, pos); pos += 2;
                    tiles[x, y].gateID = BitConverter.ToInt16(worldData, pos); pos += 2;
                }
            }
        }

        public void GenerateEnvironment()
        {
            Console.WriteLine($"Generating world environment, spawnPointX/Y: {spawnPointX}/{spawnPointY}");
            for (int x = 0; x < GetBoundsX(); x++)
            {
                for (int y = 0; y < spawnPointY; y++)
                {
                    //Console.WriteLine("X: " + x.ToString() + " Y: " + y.ToString());
                    tiles[x, y].foregroundID = 1;
                    tiles[x, y].backgroundID = 2;
                }
            }
        }
        public void AddPacketToQueue(string toUserID, BSONObject bObj)
        {
            BSONObject bson;
            if (worldPacketQueue.ContainsKey(toUserID))
            {
                bson = worldPacketQueue[toUserID];
                int msgCount = bson["mc"];

                bson["m" + msgCount.ToString()] = bObj;
                bson["mc"] = msgCount + 1;
                worldPacketQueue[toUserID] = bson;
            }    
            else
            {
                bson = new BSONObject();
                bson["mc"] = 1;
                bson["m0"] = bObj;

                worldPacketQueue[toUserID] = bson;
            }
        }

        public BSONObject GetPacketFromQueue(string userID)
        {
            return worldPacketQueue[userID];
        }

        public BSONObject RemoveFromQueue(string userID)
        {
            BSONObject rObj = null;
            if (worldPacketQueue.ContainsKey(userID))
                worldPacketQueue.Remove(userID, out rObj);
            return rObj; // returns removed bobj
        }

        public bool ReleaseQueue(ref TCPServer server, Socket socket, ClientData cld)
        {
            if (socket == null) return false;
            if (worldPacketQueue.ContainsKey(cld.userID))
            {
                BSONObject bObj = worldPacketQueue[cld.userID];
                if (bObj.ContainsKey("mc"))
                {
                    int msgCount = bObj["mc"];

                    for (int i = 0; i < msgCount; i++)
                    {
                        socket.AddBSON(ref server, bObj["m" + i.ToString()] as BSONObject);
                    }
                }

                RemoveFromQueue(cld.userID);
                return true;
            }
            
            return false;
        }

        private WorldTile[,] tiles;
        public int GetBoundsX() => tiles.GetUpperBound(0) + 1;
        public int GetBoundsY() => tiles.GetUpperBound(1) + 1;


        public string WorldName = "DEFAULT";
        public int worldID = -1;
        public short spawnPointX = 30, spawnPointY = 20;
        public int ownerID = -1;
        public string storedOnServer = "";

        public bool SetTile(int x, int y, WorldTile tile)
        {
            if (x >= GetBoundsX() || y >= GetBoundsY() || x < 0 || y < 0) return false;
            tiles[x, y] = tile;
            return true;
        }

        public bool SetFG(int x, int y, short foreID, bool reset = false)
        {
            lock (tiles)
            {
                if (x >= GetBoundsX() || y >= GetBoundsY() || x < 0 || y < 0) return false;
                if (x == spawnPointX && y == spawnPointY) return false;

                if (tiles[x, y].foregroundID == 0 || reset)
                {
                    tiles[x, y].foregroundID = foreID;
                    tiles[x, y].foregroundHits = 0;
                    tiles[x, y].lastHit[(int)LayerType.Block] = 0;
                }
                else
                    return false;

                return true;
            }
        }

        public bool SetBG(int x, int y, short backID, bool reset = false)
        {
            if (x >= GetBoundsX() || y >= GetBoundsY() || x < 0 || y < 0) return false;
            if (tiles[x, y].backgroundID == 0 || reset)
            {
                tiles[x, y].backgroundID = backID;
                tiles[x, y].backgroundHits = 0;
                tiles[x, y].lastHit[(int)LayerType.Background] = 0;
            }
            else
                return false;
            return true;
        }

        public bool SetL(int x, int y, short liquidID, bool reset = false) // set liquid
        {
            if (x >= GetBoundsX() || y >= GetBoundsY() || x < 0 || y < 0) return false;
            if (tiles[x, y].liquidID == 0 || reset)
            {
                tiles[x, y].liquidID = liquidID;
                tiles[x, y].liquidHits = 0;
                tiles[x, y].lastHit[(int)LayerType.Water] = 0;
            }
            else
                return false;
            return true;
        }

        public bool HitL(int x, int y, short foreID, bool reset = false)
        {
            lock (tiles)
            {
                if (x >= GetBoundsX() || y >= GetBoundsY() || x < 0 || y < 0) return false;
                if (tiles[x, y].liquidID == 0) return false;

                long time = KukouriTools.GetTimeInternalMS();
                int layerType = (int)LayerType.Water;

                // Console.WriteLine("Time is: " + time.ToString() + " of lastHit: " + tiles[x, y].lastHit[layerType].ToString());

                if (tiles[x, y].lastHit[layerType] + 4000 > time || tiles[x, y].lastHit[layerType] == 0)
                {
                    tiles[x, y].lastHit[layerType] = time;
                    tiles[x, y].liquidHits++;
                    if (tiles[x, y].liquidHits > 3) // lets use 3 statically for now...
                    {
                        return true;
                    }
                }
                else
                {
                    tiles[x, y].lastHit[layerType] = time;
                    tiles[x, y].liquidHits = 1; // Hits are re-set, basically back to 1 cuz reset + current hit ig ?
                }

                return false; // only return true if block should be broken rn...
            }
        }

        public bool HitFG(int x, int y, short foreID, bool reset = false)
        {
            lock (tiles)
            {
                if (x >= GetBoundsX() || y >= GetBoundsY() || x < 0 || y < 0) return false;
                if (tiles[x, y].foregroundID == 0) return false;

                long time = KukouriTools.GetTimeInternalMS();
                int layerType = (int)LayerType.Block;

               // Console.WriteLine("Time is: " + time.ToString() + " of lastHit: " + tiles[x, y].lastHit[layerType].ToString());

                if (tiles[x, y].lastHit[layerType] + 4000 > time || tiles[x, y].lastHit[layerType] == 0)
                {
                    tiles[x, y].lastHit[layerType] = time;
                    tiles[x, y].foregroundHits++;
                    if (tiles[x, y].foregroundHits > 3) // lets use 3 statically for now...
                    {
                        return true;
                    }
                }
                else
                {
                    tiles[x, y].lastHit[layerType] = time;
                    tiles[x, y].foregroundHits = 1; // Hits are re-set, basically back to 1 cuz reset + current hit ig ?
                }

                return false; // only return true if block should be broken rn...
            }
        }

        public bool HitBG(int x, int y, short foreID, bool reset = false)
        {
            lock (tiles)
            {
                if (x >= GetBoundsX() || y >= GetBoundsY() || x < 0 || y < 0) return false;
                if (tiles[x, y].backgroundID == 0) return false;

                long time = KukouriTools.GetTimeInternalMS();
                int layerType = (int)LayerType.Background;

                // Console.WriteLine("Time is: " + time.ToString() + " of lastHit: " + tiles[x, y].lastHit[layerType].ToString());

                if (tiles[x, y].lastHit[layerType] + 4000 > time || tiles[x, y].lastHit[layerType] == 0)
                {
                    tiles[x, y].lastHit[layerType] = time;
                    tiles[x, y].backgroundHits++;
                    if (tiles[x, y].backgroundHits > 3) // lets use 3 statically for now...
                    {
                        return true;
                    }
                }
                else
                {
                    tiles[x, y].lastHit[layerType] = time;
                    tiles[x, y].backgroundHits = 1; // Hits are re-set, basically back to 1 cuz reset + current hit ig ?
                }

                return false; // only return true if block should be broken rn...
            }
        }


        public bool HitW(int x, int y, short foreID, bool reset = false)
        {
            lock (tiles)
            {
                if (x >= GetBoundsX() || y >= GetBoundsY() || x < 0 || y < 0) return false;
                if (tiles[x, y].gateID == 0) return false;

                long time = KukouriTools.GetTimeInternalMS();
                int layerType = (int)LayerType.Wiring;

                // Console.WriteLine("Time is: " + time.ToString() + " of lastHit: " + tiles[x, y].lastHit[layerType].ToString());

                if (tiles[x, y].lastHit[layerType] + 4000 > time || tiles[x, y].lastHit[layerType] == 0)
                {
                    tiles[x, y].lastHit[layerType] = time;
                    tiles[x, y].gateHits++;
                    if (tiles[x, y].gateHits > 3) // lets use 3 statically for now...
                    {
                        return true;
                    }
                }
                else
                {
                    tiles[x, y].lastHit[layerType] = time;
                    tiles[x, y].gateHits = 1; // Hits are re-set, basically back to 1 cuz reset + current hit ig ?
                }

                return false; // only return true if block should be broken rn...
            }
        }

        public bool SetW(int x, int y, short gateID, bool reset = false) // set wire
        {
            lock (tiles)
            {
                if (x >= GetBoundsX() || y >= GetBoundsY() || x < 0 || y < 0) return false;

                if (tiles[x, y].gateID == 0 || reset)
                {
                    tiles[x, y].gateID = gateID;
                    tiles[x, y].gateHits = 0;
                    tiles[x, y].lastHit[(int)LayerType.Wiring] = 0;
                }
                else
                    return false;
                return true;
            }
        }

        public short GetID(int x, int y, LayerType lType) // todo handle buffers & layerhits!
        {
            if (x < GetBoundsX() || y < GetBoundsY() && x >= 0 && y >= 0)
            {
                switch (lType)
                {
                    case LayerType.Block:
                        return tiles[x, y].foregroundID;
                    case LayerType.Background:
                        return tiles[x, y].backgroundID;
                    case LayerType.Water:
                        return tiles[x, y].liquidID;
                    case LayerType.Wiring:
                        return tiles[x, y].gateID;
                    default:
                        {
                            Console.WriteLine($"At World.GetID: Unknown layer-type ??");
                        }
                        break;
                }
            }
            return -1;
        }

        public World(int worldId, short sizeX, short sizeY, string worldName, short spawnX = 30, short spawnY = 20) // Constructor Generates plain world
        {
            worldID = worldId;
            WorldName = worldName;
            tiles = new WorldTile[sizeX, sizeY];
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++) 
                {
                    tiles[x, y].lastHit = new long[4]; // fg, bg, liquid, wiring
                }
            }
            spawnPointX = spawnX;
            spawnPointY = spawnY;
        }

        private bool GenerateWorld()
        {
            if (tiles != null && WorldName != "") return false;


            return true;
        }

        public BSONObject Serialize() // todo optimize?
        {
            BSONObject wObj = new BSONObject();

            int tileLen = tiles.Length;
            int allocLen = tileLen * 2;

            byte[] blockLayerData = new byte[allocLen];
            byte[] backgroundLayerData = new byte[allocLen];
            byte[] waterLayerData = new byte[allocLen];
            byte[] wiringLayerData = new byte[allocLen];

            int width = GetBoundsX();
            int height = GetBoundsY();

            Console.WriteLine($"Serializing world '{WorldName}' with width: {width} and height: {height}.");

            int pos = 0;
            for (int i = 0; i < tiles.Length; ++i)
            {
                int x = i % width;
                int y = i / width;

                if (x == spawnPointX && y == spawnPointY)
                   tiles[x, y].foregroundID = 110;


                if (tiles[x, y].foregroundID != 0) Buffer.BlockCopy(BitConverter.GetBytes(tiles[x, y].foregroundID), 0, blockLayerData, pos, 2);
                if (tiles[x, y].backgroundID != 0) Buffer.BlockCopy(BitConverter.GetBytes(tiles[x, y].backgroundID), 0, backgroundLayerData, pos, 2);
                if (tiles[x, y].liquidID != 0) Buffer.BlockCopy(BitConverter.GetBytes(tiles[x, y].liquidID), 0, waterLayerData, pos, 2);
                if (tiles[x, y].gateID != 0) Buffer.BlockCopy(BitConverter.GetBytes(tiles[x, y].gateID), 0, wiringLayerData, pos, 2);
                pos += 2;
            }

            wObj[MessageLabels.MessageID] = MessageLabels.Ident.GetWorld;
            wObj["World"] = worldID.ToString("X8");
            wObj["BlockLayer"] = blockLayerData;
            wObj["BackgroundLayer"] = backgroundLayerData;
            wObj["WaterLayer"] = waterLayerData;
            wObj["WiringLayer"] = wiringLayerData;

            BSONObject cObj = new BSONObject();
            cObj["Count"] = 0;

            List<int>[] layerHits = new List<int>[4];
            for (int j = 0; j < layerHits.Length; j++)
            {
                layerHits[j] = new List<int>();
                layerHits[j].AddRange(Enumerable.Repeat(0, tileLen));
            }

            List<int>[] layerHitBuffers = new List<int>[4];
            for (int j = 0; j < layerHitBuffers.Length; j++)
            {
                layerHitBuffers[j] = new List<int>();
                layerHitBuffers[j].AddRange(Enumerable.Repeat(0, tileLen));
            }

            wObj["BlockLayerHits"] = layerHits[0];
            wObj["BackgroundLayerHits"] = layerHits[1];
            wObj["WaterLayerHits"] = layerHits[2];
            wObj["WiringLayerHits"] = layerHits[3];

            wObj["BlockLayerHitBuffers"] = layerHitBuffers[0];
            wObj["BackgroundLayerHitBuffers"] = layerHitBuffers[1];
            wObj["WaterLayerHitBuffers"] = layerHitBuffers[2];
            wObj["WiringLayerHits"] = layerHitBuffers[3];

            // change to template null count for optimization soon...
            BSONObject wLayoutType = new BSONObject();
            wLayoutType["Count"] = 0;
            BSONObject wBackgroundType = new BSONObject();
            wBackgroundType["Count"] = 0;
            BSONObject wMusicSettings = new BSONObject();
            wMusicSettings["Count"] = 0;

            BSONObject wStartPoint = new BSONObject();
            wStartPoint["x"] = (int)spawnPointX; wStartPoint["y"] = (int)spawnPointY;

            BSONObject wSizeSettings = new BSONObject();
            wSizeSettings["WorldSizeX"] = width; wSizeSettings["WorldSizeY"] = height;
            BSONObject wGravityMode = new BSONObject();
            wGravityMode["Count"] = 0;
            BSONObject wRatings = new BSONObject();
            wRatings["Count"] = 0;
            BSONObject wRaceScores = new BSONObject();
            wRaceScores["Count"] = 0;
            BSONObject wLightingType = new BSONObject();
            wLightingType["Count"] = 0;


            wObj["WorldLayoutType"] = wLayoutType;
            wObj["WorldBackgroundType"] = wBackgroundType;
            wObj["WorldMusicIndex"] = wMusicSettings;
            wObj["WorldStartPoint"] = wStartPoint;
            wObj["WorldItemId"] = 0;
            wObj["WorldSizeSettings"] = wSizeSettings;
            //wObj["WorldGravityMode"] = wGravityMode;
            wObj["WorldRatingsKey"] = wRatings;
            wObj["WorldItemId"] = 1;
            wObj["InventoryId"] = 1;
            wObj["RatingBoardCountKey"] = 0;
            wObj["QuestStarterItemSummerCountKey"] = 0;
            wObj["WorldRaceScoresKey"] = wRaceScores;
            wObj["WorldTagKey"] = 0;
            wObj["PlayerMaxDeathsCountKey"] = 0;
            wObj["RatingBoardDateTimeKey"] = DateTimeOffset.UtcNow.Date;
            wObj["WorldLightingType"] = wLightingType;
            wObj["WorldWeatherType"] = wLightingType;
            wObj["WorldItems"] = new BSONObject();

            BSONObject pObj = new BSONObject();
            
            wObj["PlantedSeeds"] = pObj;
            wObj["Collectables"] = cObj;

            Console.WriteLine("World Serialized Object has key count of: " + wObj.Count.ToString());
            return wObj; // TODO!!
        }
    }
}
