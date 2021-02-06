using System;
using System.Collections.Generic;
using System.Text;

namespace PixelWorldsServer.Networking.Pixel_Worlds
{
    class KukouriTools
    {
        public static long GetTime()
        {
            return (DateTime.UtcNow - default(TimeSpan)).Ticks;
        }

        public static long GetTimeInternalMS()
        {
            return DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }
    }
}
