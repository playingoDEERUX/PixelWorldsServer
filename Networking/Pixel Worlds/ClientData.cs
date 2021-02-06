using BasicTypes;
using PixelWorldsServer.WorldSystem;
using System;
using System.Collections.Generic;
using System.Text;

namespace PixelWorldsServer.Networking.Pixel_Worlds
{
    class ClientData
    {
        public enum OPLevel
        {
            PLAYER,
            SUPPORTER, // or influencer
            MODERATOR,
            ADMIN
        }
        public enum AnimationHotSpots
        {
            // Token: 0x04002293 RID: 8851
            None,
            // Token: 0x04002294 RID: 8852
            Face,
            // Token: 0x04002295 RID: 8853
            Eyebrows,
            // Token: 0x04002296 RID: 8854
            Eyeballs,
            // Token: 0x04002297 RID: 8855
            PupilLeft,
            // Token: 0x04002298 RID: 8856
            Mouth,
            // Token: 0x04002299 RID: 8857
            Torso,
            // Token: 0x0400229A RID: 8858
            TopArm,
            // Token: 0x0400229B RID: 8859
            BottomArm,
            // Token: 0x0400229C RID: 8860
            Legs,
            // Token: 0x0400229D RID: 8861
            Hat,
            // Token: 0x0400229E RID: 8862
            Hair,
            // Token: 0x0400229F RID: 8863
            Glasses,
            // Token: 0x040022A0 RID: 8864
            ContactLensLeft,
            // Token: 0x040022A1 RID: 8865
            TopEarRing,
            // Token: 0x040022A2 RID: 8866
            BottomEarRing,
            // Token: 0x040022A3 RID: 8867
            Beard,
            // Token: 0x040022A4 RID: 8868
            Mask,
            // Token: 0x040022A5 RID: 8869
            Pants,
            // Token: 0x040022A6 RID: 8870
            Shoes,
            // Token: 0x040022A7 RID: 8871
            Neck,
            // Token: 0x040022A8 RID: 8872
            Shirt,
            // Token: 0x040022A9 RID: 8873
            TopArmSleeve,
            // Token: 0x040022AA RID: 8874
            TopArmGlove,
            // Token: 0x040022AB RID: 8875
            TopArmItem,
            // Token: 0x040022AC RID: 8876
            BottomArmSleeve,
            // Token: 0x040022AD RID: 8877
            BottomArmGlove,
            // Token: 0x040022AE RID: 8878
            BottomArmItem,
            // Token: 0x040022AF RID: 8879
            Back,
            // Token: 0x040022B0 RID: 8880
            PupilRight,
            // Token: 0x040022B1 RID: 8881
            ContactLensRight,
            // Token: 0x040022B2 RID: 8882
            Eyelashes,
            // Token: 0x040022B3 RID: 8883
            Tights,
            // Token: 0x040022B4 RID: 8884
            Tail,
            // Token: 0x040022B5 RID: 8885
            END_OF_THE_ENUM
        }


        public string IP;
        public ushort sessionID;
        public string session;
        public short[] clothing;

        public long lastPing = 0;
        public bool needPing = false;
        public bool hasLogon = false;
        public bool isRegistered = false;
        public bool hasCheckedVersion = false;
        public bool stopReceiving = false;
        public bool isEnteringWorld = false;
        public bool hasGottenPacketsUntilWorldEnter = false;
        public bool hasSpawned = false;
        public int worldEnterTimeout = 0;
        public string region = "";

        public int GetUID()
        {
            return Convert.ToInt32(userID, 16);
        }

        public string OS;
        public string userID = "00000000";
        public string nickName;
        public string userName;
        public long worldID = -1;
        public string cognito_ID;
        public string cognito_Token;
        public long tryingToJoinWorld = -1;
        public short[] spots;
        public string recentIP = "";

        public double posX, posY;
        public byte currentAnimation = 0, currentDirection = 0;
        
        public OPLevel opLevel = OPLevel.PLAYER;
        public World GetWorld(ref TCPServer server)
        {
            World world = null;
            if (worldID > 0) 
            {
                world = server.worldManager.GetWorld(TCPServer.externalServerIP, "", worldID.ToString("X8"));
            }
            return world;
        }

        public void SetDataFromTable(List<Dictionary<string, string>> table)
        {
            Console.WriteLine("Setting data from table...");
            userID = int.Parse(table[0]["ID"]).ToString("X8");
            string username = table[0]["UserName"];

            region = table[0]["region"];
            recentIP = table[0]["IP"];
            
            if (!ClientData.IsValidUsername(username))
            {
                Console.WriteLine("Invalid username (unregistered)...");
                userName = userID;
                nickName = "<color=#800080>Subject_" + userID;
                isRegistered = false;
            }
            else
            {
                userName = username;
                nickName = "<color=#800080>" + username;
                isRegistered = true;
            }

            gems = int.Parse(table[0]["gems"]);
            bytecoins = int.Parse(table[0]["bytecoins"]);
            opLevel = (OPLevel)short.Parse(table[0]["powerStatus"]);

            Console.WriteLine("Parsing clothing data...");

            string[] clothingData = table[0]["spots"].Split("|");
            int cloLen = clothingData.Length;

            if (cloLen > spots.Length) return;

            for (int i = 0; i < cloLen; i++)
            {
                spots[i] = short.Parse(clothingData[i]);
            }
        }

        public string SerializeSpotsData()
        {
            return string.Join("|", spots);
        }

        public static bool IsValidUsername(string username)
        {
            if (username != "" && username != "Dummy" && username != "Player") return true;
            return false;
        }

        public int gems = 999999999;
        public int bytecoins = 999999999;

        public ClientData(string playerName, string cogID, string cogToken, int OpLevel, string regionStr) // the more or less "essential" data
        {
            spots = new short[35];
            userName = playerName;
            cognito_ID = cogID;
            cognito_Token = cogToken;
            opLevel = (OPLevel)OpLevel;
            region = regionStr;
        }
    }
}
