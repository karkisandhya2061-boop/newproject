using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Security.Claims;
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
        private readonly IConfiguration _config;
        private readonly JwtService _jwt;

        public AuthController(IConfiguration config, JwtService jwt)
        {
            _config = config;
            _jwt = jwt;
        }

        // ─── SIGNUP ───────────────────────────────────────────────
        [HttpPost("signup")]
        public IActionResult Signup([FromBody] User user)
        {
            string hashedPassword = HashPassword(user.Password);

            using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            string query = @"INSERT INTO users 
                (first_name, last_name, email, phone, address, country, password, role)
                VALUES 
                (@FirstName, @LastName, @Email, @Phone, @Address, @Country, @Password, @Role)";

            var cmd = new MySqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@FirstName", user.FirstName);
            cmd.Parameters.AddWithValue("@LastName",  user.LastName);
            cmd.Parameters.AddWithValue("@Email",     user.Email);
            cmd.Parameters.AddWithValue("@Phone",     user.Phone);
            cmd.Parameters.AddWithValue("@Address",   user.Address);
            cmd.Parameters.AddWithValue("@Country",   user.Country);
            cmd.Parameters.AddWithValue("@Password",  hashedPassword);
            cmd.Parameters.AddWithValue("@Role",      user.Role ?? "user");

            try   { cmd.ExecuteNonQuery(); }
            catch (MySqlException) { return Conflict(new { message = "Email already exists." }); }

            return Ok(new { message = "User registered successfully." });
        }

        // ─── LOGIN ────────────────────────────────────────────────
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            string hashedPassword = HashPassword(req.Password);

            using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            var cmd = new MySqlCommand(
                "SELECT id, email, role FROM users WHERE email=@Email AND password=@Password AND is_active=1",
                conn);
            cmd.Parameters.AddWithValue("@Email",    req.Email);
            cmd.Parameters.AddWithValue("@Password", hashedPassword);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return Unauthorized(new { message = "Invalid credentials." });

            var userId = Convert.ToInt32(reader["id"]);
            var email  = reader["email"].ToString()!;
            var role   = reader["role"].ToString()!;
            reader.Close();

            var accessToken  = _jwt.GenerateToken(userId, email, role);
            var refreshToken = _jwt.GenerateRefreshToken();
            SaveRefreshToken(conn, userId, refreshToken);

            return Ok(new
            {
                accessToken,
                refreshToken,
                expiresInSeconds = 3600,
                user = new { userId, email, role }
            });
        }

        // ─── ADMIN LOGIN ──────────────────────────────────────────
        [HttpPost("admin-login")]
        public IActionResult AdminLogin([FromBody] AdminLoginRequest req)
        {
            string hashedPassword = HashPassword(req.Password);

            using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            var cmd = new MySqlCommand(
                @"SELECT id, email, role FROM users 
                  WHERE (email=@Login OR username=@Login) 
                  AND password=@Password AND role='admin' AND is_active=1",
                conn);
            cmd.Parameters.AddWithValue("@Login",    req.Login);
            cmd.Parameters.AddWithValue("@Password", hashedPassword);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return Unauthorized(new { message = "Invalid admin credentials." });

            var userId = Convert.ToInt32(reader["id"]);
            var email  = reader["email"].ToString()!;
            var role   = reader["role"].ToString()!;
            reader.Close();

            var accessToken  = _jwt.GenerateToken(userId, email, role);
            var refreshToken = _jwt.GenerateRefreshToken();
            SaveRefreshToken(conn, userId, refreshToken);

            return Ok(new
            {
                accessToken,
                refreshToken,
                expiresInSeconds = 3600,
                user = new { userId, email, role }
            });
        }

        // ─── REFRESH ──────────────────────────────────────────────
        [HttpPost("refresh")]
        public IActionResult Refresh([FromBody] RefreshRequest req)
        {
            using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            var cmd = new MySqlCommand(
                @"SELECT rt.user_id, u.email, u.role 
                  FROM refresh_tokens rt
                  JOIN users u ON u.id = rt.user_id
                  WHERE rt.token=@Token 
                    AND rt.is_revoked=0 
                    AND rt.expires_at > UTC_TIMESTAMP()",
                conn);
            cmd.Parameters.AddWithValue("@Token", req.RefreshToken);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return Unauthorized(new { message = "Invalid or expired refresh token." });

            var userId = Convert.ToInt32(reader["user_id"]);
            var email  = reader["email"].ToString()!;
            var role   = reader["role"].ToString()!;
            reader.Close();

            RevokeRefreshToken(conn, req.RefreshToken);
            var newAccessToken  = _jwt.GenerateToken(userId, email, role);
            var newRefreshToken = _jwt.GenerateRefreshToken();
            SaveRefreshToken(conn, userId, newRefreshToken);

            return Ok(new
            {
                accessToken  = newAccessToken,
                refreshToken = newRefreshToken,
                expiresInSeconds = 3600
            });
        }

        // ─── LOGOUT ───────────────────────────────────────────────
        [HttpPost("logout")]
        public IActionResult Logout([FromBody] RefreshRequest req)
        {
            using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();
            RevokeRefreshToken(conn, req.RefreshToken);
            return Ok(new { message = "Logged out successfully." });
        }

        // ─── ME ───────────────────────────────────────────────────
        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email  = User.FindFirstValue(ClaimTypes.Email);
            var role   = User.FindFirstValue(ClaimTypes.Role);
            return Ok(new { userId, email, role });
        }

        // ─── HELPERS ─────────────────────────────────────────────
        private void SaveRefreshToken(MySqlConnection conn, int userId, string token)
        {
            var cmd = new MySqlCommand(
                @"INSERT INTO refresh_tokens (user_id, token, expires_at)
                  VALUES (@UserId, @Token, @ExpiresAt)",
                conn);
            cmd.Parameters.AddWithValue("@UserId",    userId);
            cmd.Parameters.AddWithValue("@Token",     token);
            cmd.Parameters.AddWithValue("@ExpiresAt", DateTime.UtcNow.AddDays(7));
            cmd.ExecuteNonQuery();
        }

        private void RevokeRefreshToken(MySqlConnection conn, string token)
        {
            var cmd = new MySqlCommand(
                "UPDATE refresh_tokens SET is_revoked=1 WHERE token=@Token",
                conn);
            cmd.Parameters.AddWithValue("@Token", token);
            cmd.ExecuteNonQuery();
        }

        private string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash  = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }

    // ─── REQUEST DTOs ─────────────────────────────────────────────
    public record LoginRequest(string Email, string Password);
    public record AdminLoginRequest(string Login, string Password);
    public record RefreshRequest(string RefreshToken);
}
