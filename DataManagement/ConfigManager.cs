using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PixelWorldsServer.DataManagement
{
    interface ConfigManager
    {

        // 8 bit settings:
        // 1st bit -> host server locally (127.0.0.1) yes/no
        // 2nd bit -> toggle subserver support
        // 3rd bit -> toggle locking support
        // 4th bit -> toggle NPC support
        // 5th bit -> toggle world generation support
        // 6th bit -> reserved
        // 7th bit -> reserved
        // 8th bit -> reserved
        struct ServerConfiguration 
        {
            public ushort serverPort;
            public ushort gameVersion; // is necessary to even allow for login
            public short playerLimit; // if -1, allow infinite / have no block, but will be capped by general IPV4/TCP standards to 65535 anyway and bottlenecked even earlier by hardware
            public byte serversCount; // amount of external servers deployed.
            public byte serverIndex; // If 0, then its master server.
            public byte settingsflags8bit; // 8 bit additional settings/flags to use, enabled/disabled.
            public string[] serverRegions;
        };

        public enum SettingsFlag
        {
            HOST_LOCALLY,
            MULTI_SERVER_SUPPORT,
            TILE_LOCKING,
            NPC_SUPPORT,
            UNIQUE_WORLD
        }

        static string GetAsPrintable(ServerConfiguration config)
        {
            string toPrint = string.Empty;

            toPrint += $"\n\tServer Port: {config.serverPort}\n";
            toPrint += $"\n\tGame Version: {config.gameVersion}\n";
            toPrint += $"\n\tPlayer Max Limit: {config.playerLimit}\n";
            toPrint += $"\n\tDeployed Servers: {config.serversCount}\n";
            toPrint += $"\n\tCurrent Server Index: {config.serverIndex}\n";
            toPrint += $"\n\tIs localhost: {BinaryHelper.GetBitTF(ref config.settingsflags8bit, (byte)SettingsFlag.HOST_LOCALLY)}\n";
            toPrint += $"\n\tEnable multiserver support: {BinaryHelper.GetBitTF(ref config.settingsflags8bit, (byte)SettingsFlag.MULTI_SERVER_SUPPORT)}\n";
            toPrint += $"\n\tEnable NPC: {BinaryHelper.GetBitTF(ref config.settingsflags8bit, (byte)SettingsFlag.NPC_SUPPORT)}\n";
            toPrint += $"\n\tEnable Locking: {BinaryHelper.GetBitTF(ref config.settingsflags8bit, (byte)SettingsFlag.TILE_LOCKING)}\n";
            toPrint += $"\n\tEnable Seed World Gen: {BinaryHelper.GetBitTF(ref config.settingsflags8bit, (byte)SettingsFlag.UNIQUE_WORLD)}\n";

            string regionsRepresentable = string.Empty;
            if (config.serverRegions != null)
            {
                if (config.serverRegions.Length < 1)
                {
                    Console.WriteLine("Encountered 1 config error: 'serverRegions.Length' is zero! Continuing anyway...");
                }
                else
                {
                    regionsRepresentable = string.Join(", ", config.serverRegions);
                }
            }
            else
            {
                Console.WriteLine("Encountered 1 config error: 'serverRegions' is null! Continuing anyway...");
            }

            toPrint += $"\n\tServer Regions: {regionsRepresentable}\n";
           
            return toPrint;
        }
        static string GetAsString(ServerConfiguration config)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"port={config.serverPort}\n"); // default 10001
            sb.Append($"gameversion={config.gameVersion}\n");
            sb.Append($"maxplayers={config.playerLimit}\n");
            sb.Append($"totalservers={config.serversCount}\n");
            sb.Append($"local={BinaryHelper.GetBitTF(ref config.settingsflags8bit, 0)}\n"); // True or False as string
            if (BinaryHelper.GetBit(ref config.settingsflags8bit, 1))
                sb.Append($"enable=subserver\n");
            if (BinaryHelper.GetBit(ref config.settingsflags8bit, 2))
                sb.Append($"enable=locks\n");
            if (BinaryHelper.GetBit(ref config.settingsflags8bit, 3))
                sb.Append($"enable=npc\n");

            if (config.serverRegions != null)
            {
                int len = config.serverRegions.Length;
                if (len > 0)
                {
                    sb.Append($"regions={string.Join("|", config.serverRegions)}\n");
                }
            }
            return sb.ToString();
        }

        static ServerConfiguration LoadFromFile(string path)
        {
            ServerConfiguration config = new ServerConfiguration();

            string[] lines = File.ReadAllLines(path);
            foreach (string line in lines)
            {
                int index = line.IndexOf('=');
                if (index < 0) continue;

                string key = line.Substring(0, index);
                string value = line.Substring(index + 1);

                switch (key.ToLower())
                {
                    case "port":
                        if (!ushort.TryParse(value, out config.serverPort))
                            Console.WriteLine($"Invalid conversion from config value '{value}' by key '{key}'.");

                        break;

                    case "gameversion":
                        if (!ushort.TryParse(value, out config.gameVersion))
                            Console.WriteLine($"Invalid conversion from config value '{value}' by key '{key}'.");

                        break;

                    case "maxplayers":
                        if (!short.TryParse(value, out config.playerLimit))
                            Console.WriteLine($"Invalid conversion from config value '{value}' by key '{key}'.");

                        break;

                    case "totalservers":
                        if (!byte.TryParse(value, out config.serversCount))
                            Console.WriteLine($"Invalid conversion from config value '{value}' by key '{key}'.");

                        break;

                    case "serverindex":
                        if (!byte.TryParse(value, out config.serverIndex))
                            Console.WriteLine($"Invalid conversion from config value '{value}' by key '{key}'.");

                        break;
                    case "local":
                        if (value == "true")
                            BinaryHelper.SetBit(ref config.settingsflags8bit, 0);
                        break;

                    case "enable": // otherwise, disabled.
                        {
                            string val = value.ToLower();
                            if (val.StartsWith("subserver"))
                                BinaryHelper.SetBit(ref config.settingsflags8bit, 1);
                            else if (val.StartsWith("locks"))
                                BinaryHelper.SetBit(ref config.settingsflags8bit, 2);
                            else if (val.StartsWith("npc"))
                                BinaryHelper.SetBit(ref config.settingsflags8bit, 3);
                            else if (val.StartsWith("worldgen"))
                                BinaryHelper.SetBit(ref config.settingsflags8bit, 4);
                        }
                        break;

                    case "regions": // IMPORTANT! make sure its the same sequence as you are providing the server ip's
                        {
                            string[] regs = value.ToLower().Split("|");
                            
                            foreach (string s in regs)
                            {
                                switch (s)
                                {
                                    case "europe":
                                    case "asia":
                                    case "australia":
                                    case "oceania":
                                    case "america":
                                        break;
                                    default:
                                        Console.WriteLine($"Weird tinyworldsserver deployment region detected '{s}' in config file!");
                                        break;
                                }
                            }
                            config.serverRegions = regs;
                        }
                        break;

                    default:
                        Console.WriteLine("Unknown config key: " + key);
                        break;
                }
            }

            return config;
        }
    }
}
