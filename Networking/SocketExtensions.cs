/* Creation Date: 03.06.2020 */
/* Author: @playingo */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Kernys.Bson;
using PixelWorldsServer.DataManagement;
using PixelWorldsServer.Networking.Pixel_Worlds;
using PixelWorldsServer.WorldSystem;

namespace PixelWorldsServer.Networking
{
    static class SocketExtensions
    {
        public static object TryGetData(this Socket socket, ref TCPServer server) // From a specific TCP server
        {
            // a lock used to be here...
            if (socket != null)
            {
                if (server.socketData.ContainsKey(socket))
                    return server.socketData[socket];
            }
        
            return null;
        }

        public static void Ping(this Socket socket, ref TCPServer server, ref ClientData cld)
        {
            if (cld != null)
            {
                if (cld.isEnteringWorld && cld.hasGottenPacketsUntilWorldEnter)
                {

                    BSONObject bsonObj = new BSONObject();
                    BSONObject bObj = new BSONObject();
                    bObj[MessageLabels.MessageID] = MessageLabels.Ident.Ping;

                    bsonObj[MessageLabels.MessageCount] = 1;
                    bsonObj["m0"] = bObj;

                    byte[] bsonData = SimpleBSON.Dump(bsonObj);
                    byte[] data = new byte[bsonData.Length + 4]; // Message framing
                    Array.Copy(BitConverter.GetBytes(bsonData.Length + 4), data, 4);
                    Buffer.BlockCopy(bsonData, 0, data, 4, bsonData.Length);

                    SocketError err;
                    socket.BeginSend(data, 0, data.Length, SocketFlags.None, out err, null, null);
                }
                else
                {
                    BSONObject bObj = new BSONObject();
                    bObj[MessageLabels.MessageID] = MessageLabels.Ident.Ping;
                    socket.AddBSON(ref server, bObj);
                }
                cld.lastPing = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        public static void SetBSON(this Socket socket, ref TCPServer server, BSONObject bObj)
        {
            if (socket != null)
            {
                lock (socket)
                {
                    lock (server.queuedPackets)
                    {
                        if (server.queuedPackets.ContainsKey(socket) && socket.Connected)
                        {
                            server.queuedPackets[socket] = bObj;
                        }
                    }
                }
            }
        }

        public static BSONObject GetBSON(this Socket socket, ref TCPServer server)
        {
            if (socket != null)
            {
                lock (socket)
                {
                    lock (server.queuedPackets)
                    {
                        if (server.queuedPackets.ContainsKey(socket) && socket.Connected)
                        {
                            return server.queuedPackets[socket];
                        }
                    }
                }
            }
            return null;
        }

        public static bool HasQueuedPackets(this Socket socket, ref TCPServer server)
        {
            if (socket == null) return false;
            lock (socket)
            {
                lock (server.queuedPackets)
                {
                    return server.queuedPackets.ContainsKey(socket);
                }
            }
        }

        public static void ReleaseBSON(this Socket socket, ref TCPServer server) // Sends the BSONObject in serialized form with the collected amount of messages to the Socket, if any queued/available.
        {
            
            if (socket == null) return;
            lock (socket)
            {
                lock (server.queuedPackets)
                {
                    if (server.queuedPackets.ContainsKey(socket)) // That means message is already pending for a socket...
                    {
                        if (socket.Connected)
                        {
                            BSONObject bsonObj = server.queuedPackets[socket]; // Master-bson-object
                            if (!bsonObj.ContainsKey("mc"))
                            {
                                //Console.WriteLine("No mc (message count) was present.");
                                return;
                            }

                            int msgCount = bsonObj["mc"].int32Value;
                            if (msgCount == 0)
                            {
                                //Console.WriteLine("Message count was 0??");
                                return;
                            }
                            
                            
                            byte[] bsonData = SimpleBSON.Dump(bsonObj);
                            byte[] data = new byte[bsonData.Length + 4]; // Message framing
                            Array.Copy(BitConverter.GetBytes(bsonData.Length + 4), data, 4);
                            Buffer.BlockCopy(bsonData, 0, data, 4, bsonData.Length);

                            try
                            {
                                //Console.WriteLine("Releasing BSON...");
                                SocketError err;
                                socket.BeginSend(data, 0, data.Length, SocketFlags.None, out err, null, null);
#if DEBUG
                                
#endif
                                //Console.WriteLine("Sent packet: " + Encoding.ASCII.GetString(data));
                            }
                            catch (SocketException ex)
                            {
                                Console.WriteLine("Was unable to successfully send packet: " + ex.Message);
                            }
                            catch (ObjectDisposedException ex)
                            {
                                Console.WriteLine("At releasebson, client was disposed already?");
                            }
                        }

                        server.queuedPackets.Remove(socket); // All-set.
                    }
                }
            }
        
        }

        /*public static void SendBSON(this Socket socket, TCPServer server, BSONObject bsonObject) // Instantly adds a single bson object and releases it. Only works if there was no queued packet earlier.
        {

        }*/

        public static bool PingBSON(this Socket socket, ref TCPServer server)
        {
            lock (socket)
            {
                if (socket == null) return true;
                if (!socket.Connected)
                {
                    Console.WriteLine("Attempted to PingBSON while socket was disconnected.");
                    return true;
                }

                lock (server.queuedPackets)
                {
                    if (!server.queuedPackets.ContainsKey(socket))
                    {
                        BSONObject bObj = new BSONObject();
                        bObj["ID"] = "p";
                        AddBSON(socket, ref server, bObj);
                        ReleaseBSON(socket, ref server);
                        return true;
                    }
                }
            }
            return false;
        }

        public static void BroadcastBSONInWorld(this Socket socket, ref TCPServer server, BSONObject bsonObject, ClientData cld, bool doNotQueue, bool bcSelf = true)
        {
            if (cld.worldID < 1)
            {
                Console.WriteLine("World is -1!!");
                return;
            }
            foreach (Socket s in server.GetSockets())
            {

                if (s == null) continue;
                //if (s == socket) continue;
                if (!s.Connected)
                {
                    //Console.WriteLine("Socket not connected...");
                    continue;
                }

                ClientData cData = (ClientData)s.TryGetData(ref server);
                if (cData == null) continue;
                if (cData.userID == cld.userID && !bcSelf) continue;

                if (cData.worldID == cld.worldID)
                {
                    World w = cld.GetWorld(ref server);
                    //Console.WriteLine($"Broadcasting packet to userID: {cData.userID}");
                    if (!doNotQueue) w.AddPacketToQueue(cData.userID, bsonObject);
                    else s.AddBSON(ref server, bsonObject);
                }
            }
        }
        public static void AddBSON(this Socket socket, ref TCPServer server, BSONObject bsonObject)
        {

            if (socket == null) return;


            if (!socket.Connected)
            {
                //socket.Close();
                Console.WriteLine("Attempted to AddBSON while socket was disconnected.");
                return;
            }

            lock (server.queuedPackets)
            {
                //Console.WriteLine("current (before) packets in queue: " + server.queuedPackets.Count.ToString());
                BSONObject bObj;

                if (!server.queuedPackets.ContainsKey(socket)) // Freshly new queue for this packet in this case
                {
                    //Console.WriteLine("Key not found in queued packets, creating new one...");
                    bObj = new BSONObject();
                    bObj["m0"] = bsonObject;
                    bObj["mc"] = 1; // Start with 1
                }
                else // And in this case if it already exists, just add a BSONObject and increase messagecount as how pw wants it.
                {
                    bObj = server.queuedPackets[socket];
                    int msgCount = bObj["mc"].int32Value + 1;
                    bObj["m" + (msgCount - 1).ToString()] = bsonObject;
                    bObj["mc"] = msgCount;
                }
                //if (server.queuedPackets.ContainsKey(socket)) server.queuedPackets[socket].Clear();
                server.queuedPackets[socket] = bObj;

            }
        
        }


        public static void ResetBSON(this Socket socket, ref TCPServer server)  // only use this to abort packet delivery
        {
            lock (socket)
            {
                if (socket == null) return;
                lock (server.queuedPackets)
                {
                    if (server.queuedPackets.ContainsKey(socket))
                        server.queuedPackets.Remove(socket);
                }
            }
        }

        public static bool SetData(this Socket socket, ref TCPServer server, object data = null) // Returns whether data has been freshly set or overwritten.
        {
            bool overriden = false;
            if (socket != null)
            {
                lock (server.socketData)
                {
                    overriden = server.socketData.ContainsKey(socket);
                    server.socketData[socket] = data;
                }
            }
            return overriden;
        }

        public static bool RemoveData(this Socket socket, TCPServer server)
        {

            lock (socket)
            {
                if (server.socketData.ContainsKey(socket))
                {
                    object o;
                    server.socketData.Remove(socket, out o);
                    return true;
                }
            }
            
            return false;
        }

        public static bool IsSocketConnected(this Socket socket)
        {
            try
            {
                return !(socket.Poll(10, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException ex) { Console.WriteLine($"at IsSocketConnected {ex.Message}"); return false; } // timed out
            catch (ObjectDisposedException ex) { Console.WriteLine($"at IsSocketConnected {ex.Message}"); return false; }
        }
    }
}
