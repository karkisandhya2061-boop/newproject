using MySql.Data.MySqlClient;

namespace WebApplication1.Data
{
    public class DbConnection
    {
        private readonly IConfiguration _config;

        public DbConnection(IConfiguration config)
        {
            _config = config;
        }

        public MySqlConnection GetConnection()
        {
            return new MySqlConnection(
                _config.GetConnectionString("DefaultConnection")
            );
        }
    }
}