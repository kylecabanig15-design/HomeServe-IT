using System;
using MySqlConnector;

class Program
{
    static void Main()
    {
        string connStr = "Server=127.0.0.1;Port=3306;Database=HomeServeIT;User=root;Password=;";
        using var conn = new MySqlConnection(connStr);
        conn.Open();
        using var cmd = new MySqlCommand("ALTER TABLE ServiceRequests DROP COLUMN IsArchived;", conn);
        try {
            cmd.ExecuteNonQuery();
            Console.WriteLine("Column dropped successfully.");
        } catch(Exception ex) {
            Console.WriteLine(ex.Message);
        }
    }
}
