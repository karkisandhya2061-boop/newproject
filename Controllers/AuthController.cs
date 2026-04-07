using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Security.Cryptography;
using System.Text;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        // getting connection string from appsettings.json
        private readonly IConfiguration _config;

        public AuthController(IConfiguration config)
        {
            _config = config;
        }

        // signup api
        [HttpPost("signup")]
        public IActionResult Signup(User user)
        {
            // hash password before saving
            string hashedPassword = HashPassword(user.Password);

            // open mysql connection
            using (MySqlConnection conn = new MySqlConnection(
                _config.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // insert user query
                string query = @"INSERT INTO users 
                (first_name,last_name,email,phone,address,country,password,role)
                VALUES 
                (@FirstName,@LastName,@Email,@Phone,@Address,@Country,@Password,@Role)";

                MySqlCommand cmd = new MySqlCommand(query, conn);

                // map values from request
                cmd.Parameters.AddWithValue("@FirstName", user.FirstName);
                cmd.Parameters.AddWithValue("@LastName", user.LastName);
                cmd.Parameters.AddWithValue("@Email", user.Email);
                cmd.Parameters.AddWithValue("@Phone", user.Phone);
                cmd.Parameters.AddWithValue("@Address", user.Address);
                cmd.Parameters.AddWithValue("@Country", user.Country);
                cmd.Parameters.AddWithValue("@Password", hashedPassword);
                cmd.Parameters.AddWithValue("@Role", user.Role ?? "user"); // default role

                try
                {
                    cmd.ExecuteNonQuery(); // execute insert
                }
                catch (MySqlException)
                {
                    return BadRequest("email already exists"); // duplicate email
                }
            }

            return Ok("user registered successfully");
        }


        // login api
        [HttpPost("login")]
        public IActionResult Login(User user)
        {
            // hash password for comparison
            string hashedPassword = HashPassword(user.Password);

            using (MySqlConnection conn = new MySqlConnection(
                _config.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // check email + password + role
                string query = @"SELECT * FROM users 
                                 WHERE email=@Email 
                                 AND password=@Password 
                                 AND role=@Role";

                MySqlCommand cmd = new MySqlCommand(query, conn);

                cmd.Parameters.AddWithValue("@Email", user.Email);
                cmd.Parameters.AddWithValue("@Password", hashedPassword);
                cmd.Parameters.AddWithValue("@Role", user.Role);

                var reader = cmd.ExecuteReader();

                // if user found
                if (reader.Read())
                {
                    return Ok(new
                    {
                        message = "login successful",
                        role = reader["role"].ToString(),
                        email = reader["email"].ToString()
                    });
                }
            }

            return Unauthorized("invalid credentials");
        }


        // password hashing method
        private string HashPassword(string password)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);

                return Convert.ToBase64String(hash);
            }
        }
    }
}
