using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication.ExtendedProtection;
using System.Threading;
using System.Threading.Tasks;
using Kernys.Bson;
using PixelWorldsServer.DataManagement;
using PixelWorldsServer.Networking;
using SevenZip.Compression;
using static PixelWorldsServer.DataManagement.ConfigManager;

namespace PixelWorldsServer
{
    class Program
    {
        private static bool hostLocally = true;
        private static string configFilePath = "portalworlds.settings";
        public static string GetPublicIP()
        {
            string url = "http://checkip.dyndns.org";
            System.Net.WebRequest req = System.Net.WebRequest.Create(url);
            System.Net.WebResponse resp = req.GetResponse();
            System.IO.StreamReader sr = new System.IO.StreamReader(resp.GetResponseStream());
            string response = sr.ReadToEnd().Trim();
            string[] a = response.Split(':');
            string a2 = a[1].Substring(1);
            string[] a3 = a2.Split('<');
            string a4 = a3[0];
            return a4;
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Pixel Worlds Release Server v0.1.5 - playingo/DEERUX\nLoading server configuration...");

            //BSONObject bObj = SimpleBSON.Load(File.ReadAllBytes("extended.dat").Skip(4).ToArray())["m0"] as BSONObject;

            //File.WriteAllBytes("data.dat", LZMAHelper.DecompressLZMA(bObj["W"]["m0"].binaryValue));

            ServerConfiguration defaultConfig = new ServerConfiguration();
            defaultConfig.playerLimit = -1;
            defaultConfig.serverIndex = 0;
            defaultConfig.serverPort = 10001;
            defaultConfig.serversCount = 1;
            defaultConfig.settingsflags8bit = 0;
            defaultConfig.serverRegions = new string[1];
            defaultConfig.serverRegions[0] = "europe";

            if (File.Exists("portalworlds.settings"))
            {
                Console.WriteLine("Loading config file...");
                TCPServer.globalConfig = ConfigManager.LoadFromFile(configFilePath);
                

                Console.WriteLine($"Successfully loaded configurations from '{configFilePath}' [!]");
                TCPServer.isMaster = TCPServer.globalConfig.serverIndex == 0;
            }
            else
            {
                Console.WriteLine("No config file found, creating one...");
                File.WriteAllText(configFilePath, ConfigManager.GetAsString(defaultConfig));
            }

            Console.WriteLine("[INFO]: Config Settings => " + ConfigManager.GetAsPrintable(TCPServer.globalConfig));

            TCPServer tcpServer = new TCPServer(hostLocally ? "127.0.0.1" : GetPublicIP(), true);
            tcpServer.serverSocket.NoDelay = true;
           

            if (tcpServer.mysqlManager.Initialize() == 0)
            {
                ItemDB.Initialize();
                Console.WriteLine("Setting up server, server IP: " + TCPServer.externalServerIP + ", is master: " + TCPServer.isMaster.ToString());

                TCPEvents tcpEvents = new TCPEvents(tcpServer);

                tcpServer.OnConnect += tcpEvents.HandleOnConnect;
                tcpServer.OnDisconnect += tcpEvents.HandleOnDisconnect;

                tcpEvents.OnReceive += tcpEvents.HandleOnReceive;

                tcpServer.Setup(tcpEvents, hostLocally);


                Random rand = new Random();


                while (true)
                {
                    tcpServer.CheckAllClients();
                    Thread.Sleep(1000);
                }
            }
            else
            {
                Console.WriteLine("Initializing MysqlManager failed, aborting in a few seconds...");
                Thread.Sleep(4000);
            }
        }
    }
}
