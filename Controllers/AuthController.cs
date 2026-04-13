using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System;
using System.Security.Cryptography;
using System.Text;
using WebApplication1.Models;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        // config + jwt service
        private readonly IConfiguration _config;
        private readonly JwtService _jwt;

        public AuthController(IConfiguration config, JwtService jwt)
        {
            _config = config;
            _jwt = jwt;
        }

        // signup api
        [HttpPost("signup")]
        public IActionResult Signup(User user)
        {
            string hashedPassword = HashPassword(user.Password);

            using (MySqlConnection conn = new MySqlConnection(
                _config.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                string query = @"INSERT INTO users 
                (first_name,last_name,email,phone,address,country,password,role)
                VALUES 
                (@FirstName,@LastName,@Email,@Phone,@Address,@Country,@Password,@Role)";

                MySqlCommand cmd = new MySqlCommand(query, conn);

                cmd.Parameters.AddWithValue("@FirstName", user.FirstName);
                cmd.Parameters.AddWithValue("@LastName", user.LastName);
                cmd.Parameters.AddWithValue("@Email", user.Email);
                cmd.Parameters.AddWithValue("@Phone", user.Phone);
                cmd.Parameters.AddWithValue("@Address", user.Address);
                cmd.Parameters.AddWithValue("@Country", user.Country);
                cmd.Parameters.AddWithValue("@Password", hashedPassword);
                cmd.Parameters.AddWithValue("@Role", user.Role ?? "user");

                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (MySqlException)
                {
                    return BadRequest("email already exists");
                }
            }

            return Ok("user registered successfully");
        }

        // login api (JWT FIXED)
        [HttpPost("login")]
        public IActionResult Login(User user)
        {
            string hashedPassword = HashPassword(user.Password);

            using (MySqlConnection conn = new MySqlConnection(
                _config.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                string query = @"SELECT * FROM users 
                                 WHERE email=@Email 
                                 AND password=@Password";

                MySqlCommand cmd = new MySqlCommand(query, conn);

                cmd.Parameters.AddWithValue("@Email", user.Email);
                cmd.Parameters.AddWithValue("@Password", hashedPassword);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var userId = Convert.ToInt32(reader["id"]);
                        var email = reader["email"].ToString();
                        var role = reader["role"].ToString();

                        // generate jwt token
                        var token = _jwt.GenerateToken(userId, email, role);

                        return Ok(new
                        {
                            message = "login successful",
                            token = token,
                            role = role,
                            email = email
                        });
                    }
                }
            }

            return Unauthorized("invalid credentials");
        }

        // password hashing
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