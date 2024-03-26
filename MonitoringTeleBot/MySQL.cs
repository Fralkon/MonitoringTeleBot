using MySqlConnector;
using System.Data;

namespace MonitoringTeleBot
{
    public class MySQL
    {
        string z = "";
        MySqlConnectionStringBuilder builder;
        MySqlConnection connection;
        public MySQL()
        {
            builder = new MySqlConnectionStringBuilder()
            {
                UserID = userName,
                Password = password,
                Server = serverName,
                Database = dbName,
                Port = port
            };
            connection = new MySqlConnection(builder.ConnectionString);
        }
        public MySQL(string serverName, string userName, string dbName, uint port, string password)
        {
            builder = new MySqlConnectionStringBuilder()
            {
                UserID = userName,
                Password = password,
                Server = serverName,
                Database = dbName,
                Port = port
            };
            connection = new MySqlConnection(builder.ConnectionString);
        }
        public DataTable GetDataTableSQL(string sql)
        {
            lock (z)
            {
                DataTable dt = new DataTable();
                connection.Open();
                MySqlCommand sqlCom = new MySqlCommand()
                {
                    Connection = connection,
                    CommandText = sql
                };
                sqlCom.ExecuteNonQuery();
                MySqlDataAdapter dataAdapter = new MySqlDataAdapter(sqlCom);
                dataAdapter.Fill(dt);
                connection.Close();
                return dt;
            }
        }
        public void SendSQL(string sql)
        {
            lock (z)
            {
                connection.Open();
                MySqlCommand sqlCom = new MySqlCommand()
                {
                    Connection = connection,
                    CommandText = sql
                };
                sqlCom.ExecuteNonQuery();
                connection.Close();
            }
        }
        private string serverName = "127.0.0.1"; // Адрес сервера (для локальной базы пишите "localhost")
        private string userName = "root"; // Имя пользователя
        private string dbName = "ithelper"; //Имя базы данных
        private string password = ""; // Пароль для подключения
        private uint port = 3306; // Порт для подключения

        //private string serverName = "astf3-stp5"; // Адрес сервера (для локальной базы пишите "localhost")
        //private string userName = "root"; // Имя пользователя
        //private string dbName = "zabbix"; //Имя базы данных
        //private uint port = 3307; // Порт для подключения
        //private string password = "Fralkon"; // Пароль для подключения
        public void WaitConnectToBD()
        {
            Console.WriteLine("Wait connection BD.");
            //while (true)
            //{
            //    if (TeleBot.Ping(serverName))
            //    {
            //        Console.WriteLine("Connection"); break;
            //    }
            //    else
            //    {
            //        Console.WriteLine("Error connection BD. \nWait 5 sec.");
            //        Thread.Sleep(5000);
            //    }
            //}
        }
    }
}
