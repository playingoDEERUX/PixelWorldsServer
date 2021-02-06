using System;
using System.Collections.Generic;
using System.Text;
using Kernys.Bson;
using PixelWorldsServer.DataManagement;

namespace PixelWorldsServer.Networking.Pixel_Worlds
{
    public class Chat
    {
        public static BSONObject CreateChatMessage(string nickname, string userID, string channel, int channelIndex, string message)
        {
            BSONObject bObj = new BSONObject();
            bObj[MessageLabels.ChatMessage.Nickname] = nickname;
            bObj[MessageLabels.ChatMessage.UserID] = userID;
            bObj[MessageLabels.ChatMessage.Channel] = channel;
            bObj["channelIndex"] = channelIndex;
            bObj[MessageLabels.ChatMessage.Message] = message;
            bObj[MessageLabels.ChatMessage.ChatTime] = DateTime.UtcNow;
            return bObj;
        }
            
    }
}
