/* Creation Date: 05.12.2020 */
/* Author: @playingo */

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;

namespace PixelWorldsServer.DataManagement
{
    public struct DBCredentials
    {
        public string server,
            database,
            user,
            password;

        public DBCredentials(string _server, string _database, string _user, string _password)
        {
            server = _server;
            database = _database;
            user = _user;
            password = _password;
        }
    }

    class MysqlManager
    {
        private DBCredentials mysqlAccount;
        private MySqlConnection connection;
        public MysqlManager(string server, string db, string user, string password)
        {
            mysqlAccount.server = server;
            mysqlAccount.database = db;
            mysqlAccount.user = user;
            mysqlAccount.password = password;
        }

        private void PrintMsg(string txt)
        {
            Console.WriteLine("[MysqlManager]: " + txt);
        }

        public MysqlManager(DBCredentials credentials)
        {
            mysqlAccount = credentials;
        }

        public int Initialize() // returns status code
        {
            if (string.IsNullOrEmpty(mysqlAccount.server))
            {
                PrintMsg("MySql server ip/dns was null! Aborted initialization.");
                return -1;
            }

            connection = new MySqlConnection($"SERVER={mysqlAccount.server};DATABASE={mysqlAccount.database};UID={mysqlAccount.user};PASSWORD={mysqlAccount.password};");

            PrintMsg("Successfully initialized MySql connection!");
            return 0;
        }

        private bool OpenConnection(MySqlConnection connection)
        {

            try
            {
                connection.Open();
                return true;
            }
            catch (MySqlException ex)
            {

                switch (ex.Number)
                {
                    case 0:
                        PrintMsg("Cannot connect to server.  Contact administrator");
                        break;

                    case 1045:
                        PrintMsg("Invalid username/password, please try again");
                        break;

                    default:
                        PrintMsg("Unknown error while connecting to MySql server!");
                        break;
                }
                return false;
            }
        
        }

        private bool CloseConnection()
        {

            try
            {
                connection.Close();
                return true;
            }
            catch (MySqlException ex)
            {
                PrintMsg(ex.Message);
                return false;
            }
        
        }
        public bool RequestSendQuery(string query, bool secondAttempt = false) // A query that is not expected to return any data
        {
            Console.WriteLine("[RequestSendQuery]");

            using (MySqlConnection conn = new MySqlConnection($"SERVER={mysqlAccount.server};DATABASE={mysqlAccount.database};UID={mysqlAccount.user};PASSWORD={mysqlAccount.password};"))
            {
                OpenConnection(conn);

                if (IsConnected(conn))
                {
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = query;
                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
                else if (!secondAttempt)
                {
                    Console.WriteLine("Second attempt of connecting to sql server...");
                    if (OpenConnection(conn))
                        return RequestSendQuery(query, true);
                }
                else
                {
                    PrintMsg("An error occured, was unable to perform a RequestSendQuery!");
                    
                }
            }
            return false;
        
        }

        public List<Dictionary<string, string>> RequestFetchQuery(string query, bool secondAttempt = false) // A query that is expected to return data
        {
            //Create a list to store the result
            Console.WriteLine("[RequestFetchQuery]");

            using (MySqlConnection conn = new MySqlConnection($"SERVER={mysqlAccount.server};DATABASE={mysqlAccount.database};UID={mysqlAccount.user};PASSWORD={mysqlAccount.password};"))
            {
                OpenConnection(conn);

                if (IsConnected(conn))
                {
                    List<Dictionary<string, string>> table = new List<Dictionary<string, string>>();
                    //Create Command
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = query;
                        cmd.EnableCaching = true;

                        using (MySqlDataReader dataReader = cmd.ExecuteReader())
                        {

                            if (!dataReader.HasRows)
                            {
                                PrintMsg("No rows available, skipping...");
                                goto LABEL_SKIP;
                            }

                            //Read the data and store them in the list

                            while (dataReader.Read())
                            {
                                Dictionary<string, string> dict = new Dictionary<string, string>();

                                for (int i = 0; i < dataReader.FieldCount; i++)
                                {
                                    string columnName = dataReader.GetName(i);
                                    dict[columnName] = dataReader[columnName] + "";
                                }

                                table.Add(dict);
                            }

                        LABEL_SKIP:
                            return table;
                        }
                    }
                }
                else if (!secondAttempt)
                {
                    Console.WriteLine("Second attempt of connecting to sql server...");
                    if (OpenConnection(conn))
                        return RequestFetchQuery(query, true);
                }
                else
                {
                    PrintMsg("An error occured, was unable to perform a RequestFetchQuery!");
                }
            }
        
            return null;
        }

        public static bool HasIllegalChar(string q)
        {
            Console.WriteLine("Checking for illegal chars...");
            return !Regex.IsMatch(q, @"^[a-zA-Z0-9{}:\-.=/+]+$");
        }

        public static bool ContainsAnyChars(string str, string chars)
        {
            foreach (char c in str)
            {
                if (chars.Contains(c))
                    return true;
            }
            return false;
        }
        public bool IsConnected(MySqlConnection connection)
        {

            if (connection == null) return false;
            if (!connection.Ping()) return false;
            if (connection.State == System.Data.ConnectionState.Open) return true;
        
            return false;
        }
    }
}
