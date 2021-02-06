using System;
using System.Collections.Generic;
using System.Text;

namespace PixelWorldsServer.DataManagement
{
    struct ServerUnit
    {
        public string region { get; }
        public string ip { get; }

        public ServerUnit(string _ip, string _reg)
        {
            region = _reg;
            ip = _ip;
        }
    }
    class ServerEntryList
    {
        private static ServerUnit[] availableServers = { new ServerUnit("51.116.173.82", "europe"),
            new ServerUnit("13.67.40.131", "asia") };

        public static ServerUnit FindServerByRegion(string region)
        {
            if (region != "asia" && region != "europe")
            {
                Console.WriteLine("No supported server region found.");
                return availableServers[0];
            }

            List<ServerUnit> servers = new List<ServerUnit>();
            foreach (ServerUnit su in availableServers) 
            {
                if (su.region == region)
                    servers.Add(su);
            }

            return servers[Randomizer.RandomInt(0, servers.Count)];
        }

        public static string GetRegionFromIP(string ip)
        {
            foreach (ServerUnit su in availableServers)
            {
                if (su.ip == ip)
                    return su.region;
            }
            return "europe";
        }
    }
}
