using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("news")]
    public class NewsController : ControllerBase
    {
        private readonly IConfiguration _config;

        private static readonly string[] ValidBuckets =
            { "heroStory", "featuredSideStories", "trendingStories", "tickerItems" };

        private static readonly string[] ValidStatuses =
            { "draft", "published", "archived" };

        public NewsController(IConfiguration config)
        {
            _config = config;
        }

        // ─── PUBLIC CONTENT FEED ──────────────────────────────────
        // 3.3.2: Anonymous and authenticated users — always published only.
        // Trending ordering: pinned first (pin_order ASC), then latest updated_at DESC.

        /// <summary>
        /// GET /news/feed  — Public
        /// Returns grouped published content:
        ///   heroStory (single), featuredSideStories, trendingStories, tickerItems.
        /// </summary>
        [HttpGet("feed")]
        public IActionResult GetFeed()
        {
            using var conn = OpenConnection();

            var allStories = QueryStories(conn,
                where: "status = 'published'",
                parameters: null,
                orderBy: "published_at DESC, created_at DESC");

            var tickers = QueryTickers(conn, "status = 'published'");

            // heroStory: single item, latest published_at
            var hero = allStories
                .Where(s => s.Bucket == "heroStory")
                .OrderByDescending(s => s.PublishedAt)
                .FirstOrDefault();

            // featuredSideStories: latest published_at first
            var featured = allStories
                .Where(s => s.Bucket == "featuredSideStories")
                .OrderByDescending(s => s.PublishedAt)
                .ToList();

            // trendingStories: pinned first (pin_order ASC), then most recently updated
            var trending = allStories
                .Where(s => s.Bucket == "trendingStories")
                .OrderBy(s => s.IsPinned ? 0 : 1)
                .ThenBy(s => s.IsPinned ? s.PinOrder : int.MaxValue)
                .ThenByDescending(s => s.UpdatedAt)
                .ToList();

            return Ok(new
            {
                heroStory           = hero,
                featuredSideStories = featured,
                trendingStories     = trending,
                tickerItems         = tickers
            });
        }

        // ─── PUBLIC STORY ENDPOINTS ───────────────────────────────
        // 3.3.2: Public endpoints MUST only return status=published.

        /// <summary>
        /// GET /news/stories  — Public
        /// Published stories only. Optional ?bucket and ?category filters.
        /// Trending bucket applies pin_order -> updated_at ordering automatically.
        /// </summary>
        [HttpGet("stories")]
        public IActionResult GetStories(
            [FromQuery] string? bucket   = null,
            [FromQuery] string? category = null)
        {
            using var conn = OpenConnection();

            var conditions = new List<string> { "status = 'published'" };
            var parameters = new Dictionary<string, object?>();

            if (!string.IsNullOrWhiteSpace(bucket))
            {
                conditions.Add("bucket = @Bucket");
                parameters["@Bucket"] = bucket;
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                conditions.Add("category = @Category");
                parameters["@Category"] = category;
            }

            string where   = string.Join(" AND ", conditions);
            string orderBy = bucket == "trendingStories"
                ? "is_pinned DESC, pin_order ASC, updated_at DESC"
                : "published_at DESC, created_at DESC";

            return Ok(QueryStories(conn, where, parameters, orderBy));
        }

        /// <summary>
        /// GET /news/stories/{id}  — Public
        /// Returns 404 for any story that is not status=published.
        /// </summary>
        [HttpGet("stories/{id:int}")]
        public IActionResult GetStory(int id)
        {
            using var conn = OpenConnection();
            var story = GetStoryById(conn, id);

            // 3.3.2: non-published items are invisible to public callers
            if (story == null || story.Status != "published")
                return NotFound(new { message = "Story not found." });

            return Ok(story);
        }

        /// <summary>
        /// GET /news/ticker  — Public
        /// Published ticker items only, ordered by sort_order ASC then created_at ASC.
        /// </summary>
        [HttpGet("ticker")]
        public IActionResult GetTicker()
        {
            using var conn = OpenConnection();
            return Ok(QueryTickers(conn, "status = 'published'"));
        }

        // ─── ADMIN STORY ENDPOINTS ────────────────────────────────

        /// <summary>GET /news/admin/stories  [admin] — all statuses, full filters</summary>
        [Authorize(Roles = "admin")]
        [HttpGet("admin/stories")]
        public IActionResult AdminGetStories(
            [FromQuery] string? bucket   = null,
            [FromQuery] string? status   = null,
            [FromQuery] string? category = null)
        {
            using var conn = OpenConnection();

            var conditions = new List<string>();
            var parameters = new Dictionary<string, object?>();

            if (!string.IsNullOrWhiteSpace(status))
            {
                conditions.Add("status = @Status");
                parameters["@Status"] = status;
            }
            if (!string.IsNullOrWhiteSpace(bucket))
            {
                conditions.Add("bucket = @Bucket");
                parameters["@Bucket"] = bucket;
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                conditions.Add("category = @Category");
                parameters["@Category"] = category;
            }

            string where = conditions.Count > 0
                ? string.Join(" AND ", conditions)
                : "1=1";

            return Ok(QueryStories(conn, where, parameters,
                orderBy: "is_pinned DESC, pin_order ASC, updated_at DESC"));
        }

        /// <summary>GET /news/admin/stories/{id}  [admin] — any status</summary>
        [Authorize(Roles = "admin")]
        [HttpGet("admin/stories/{id:int}")]
        public IActionResult AdminGetStory(int id)
        {
            using var conn = OpenConnection();
            var story = GetStoryById(conn, id);
            if (story == null) return NotFound(new { message = "Story not found." });
            return Ok(story);
        }

        /// <summary>POST /news/admin/stories  [admin]</summary>
        [Authorize(Roles = "admin")]
        [HttpPost("admin/stories")]
        public IActionResult CreateStory([FromBody] CreateStoryRequest req)
        {
            if (!ValidBuckets.Contains(req.Bucket))
                return BadRequest(new { message = $"Invalid bucket. Must be one of: {string.Join(", ", ValidBuckets)}" });
            if (!ValidStatuses.Contains(req.Status))
                return BadRequest(new { message = $"Invalid status. Must be one of: {string.Join(", ", ValidStatuses)}" });

            using var conn = OpenConnection();
            if (req.Bucket == "heroStory" && req.Status == "published")
                ArchiveExistingHero(conn);

            var now = DateTime.UtcNow;
            var cmd = new MySqlCommand(@"
                INSERT INTO stories
                    (bucket, category, title, excerpt, author, read_time_minutes,
                     tone, is_pinned, pin_order, status, published_at, created_at, updated_at)
                VALUES
                    (@Bucket, @Category, @Title, @Excerpt, @Author, @ReadTime,
                     @Tone, @IsPinned, @PinOrder, @Status, @PublishedAt, @Now, @Now);
                SELECT LAST_INSERT_ID();", conn);

            BindStoryParams(cmd, req, now);
            var newId = Convert.ToInt32(cmd.ExecuteScalar());
            return CreatedAtAction(nameof(AdminGetStory), new { id = newId }, GetStoryById(conn, newId));
        }

        /// <summary>PUT /news/admin/stories/{id}  [admin]</summary>
        [Authorize(Roles = "admin")]
        [HttpPut("admin/stories/{id:int}")]
        public IActionResult UpdateStory(int id, [FromBody] UpdateStoryRequest req)
        {
            if (!ValidBuckets.Contains(req.Bucket))
                return BadRequest(new { message = $"Invalid bucket. Must be one of: {string.Join(", ", ValidBuckets)}" });
            if (!ValidStatuses.Contains(req.Status))
                return BadRequest(new { message = $"Invalid status. Must be one of: {string.Join(", ", ValidStatuses)}" });

            using var conn = OpenConnection();
            if (GetStoryById(conn, id) == null) return NotFound(new { message = "Story not found." });

            if (req.Bucket == "heroStory" && req.Status == "published")
                ArchiveExistingHero(conn, excludeId: id);

            var now = DateTime.UtcNow;
            var cmd = new MySqlCommand(@"
                UPDATE stories SET
                    bucket            = @Bucket,
                    category          = @Category,
                    title             = @Title,
                    excerpt           = @Excerpt,
                    author            = @Author,
                    read_time_minutes = @ReadTime,
                    tone              = @Tone,
                    is_pinned         = @IsPinned,
                    pin_order         = @PinOrder,
                    status            = @Status,
                    published_at      = @PublishedAt,
                    updated_at        = @Now
                WHERE id = @Id", conn);

            BindStoryParams(cmd, req, now);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();

            return Ok(GetStoryById(conn, id));
        }

        /// <summary>PATCH /news/admin/stories/{id}/status  [admin]</summary>
        [Authorize(Roles = "admin")]
        [HttpPatch("admin/stories/{id:int}/status")]
        public IActionResult PatchStoryStatus(int id, [FromBody] PatchStatusRequest req)
        {
            if (!ValidStatuses.Contains(req.Status))
                return BadRequest(new { message = $"Invalid status. Must be one of: {string.Join(", ", ValidStatuses)}" });

            using var conn = OpenConnection();
            var existing = GetStoryById(conn, id);
            if (existing == null) return NotFound(new { message = "Story not found." });

            if (existing.Bucket == "heroStory" && req.Status == "published")
                ArchiveExistingHero(conn, excludeId: id);

            var now   = DateTime.UtcNow;
            var pubAt = req.Status == "published" ? (existing.PublishedAt ?? now) : existing.PublishedAt;

            var cmd = new MySqlCommand(@"
                UPDATE stories
                SET status = @Status, published_at = @PublishedAt, updated_at = @Now
                WHERE id = @Id", conn);
            cmd.Parameters.AddWithValue("@Status",      req.Status);
            cmd.Parameters.AddWithValue("@PublishedAt", (object?)pubAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Now",         now);
            cmd.Parameters.AddWithValue("@Id",          id);
            cmd.ExecuteNonQuery();

            return Ok(GetStoryById(conn, id));
        }

        /// <summary>
        /// PATCH /news/admin/stories/{id}/pin  [admin]
        /// Sets is_pinned and pin_order. Bumps updated_at so pin changes are reflected in ordering.
        /// </summary>
        [Authorize(Roles = "admin")]
        [HttpPatch("admin/stories/{id:int}/pin")]
        public IActionResult PatchStoryPin(int id, [FromBody] PatchPinRequest req)
        {
            using var conn = OpenConnection();
            if (GetStoryById(conn, id) == null)
                return NotFound(new { message = "Story not found." });

            var cmd = new MySqlCommand(@"
                UPDATE stories
                SET is_pinned = @IsPinned, pin_order = @PinOrder, updated_at = @Now
                WHERE id = @Id", conn);
            cmd.Parameters.AddWithValue("@IsPinned", req.IsPinned);
            cmd.Parameters.AddWithValue("@PinOrder", req.PinOrder);
            cmd.Parameters.AddWithValue("@Now",      DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@Id",       id);
            cmd.ExecuteNonQuery();

            return Ok(GetStoryById(conn, id));
        }

        /// <summary>DELETE /news/admin/stories/{id}  [admin]</summary>
        [Authorize(Roles = "admin")]
        [HttpDelete("admin/stories/{id:int}")]
        public IActionResult DeleteStory(int id)
        {
            using var conn = OpenConnection();
            if (GetStoryById(conn, id) == null)
                return NotFound(new { message = "Story not found." });

            var cmd = new MySqlCommand("DELETE FROM stories WHERE id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
            return Ok(new { message = "Story deleted." });
        }

        // ─── ADMIN TICKER ENDPOINTS ───────────────────────────────

        /// <summary>GET /news/admin/ticker  [admin] — returns all statuses</summary>
        [Authorize(Roles = "admin")]
        [HttpGet("admin/ticker")]
        public IActionResult AdminGetTicker([FromQuery] string? status = null)
        {
            using var conn = OpenConnection();

            var cmd = new MySqlCommand(string.IsNullOrWhiteSpace(status)
                ? "SELECT id, text, status, sort_order, created_at, updated_at FROM ticker_items ORDER BY sort_order ASC, created_at ASC"
                : "SELECT id, text, status, sort_order, created_at, updated_at FROM ticker_items WHERE status = @Status ORDER BY sort_order ASC, created_at ASC",
                conn);

            if (!string.IsNullOrWhiteSpace(status))
                cmd.Parameters.AddWithValue("@Status", status);

            var list = new List<TickerItem>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(MapTicker(reader));
            return Ok(list);
        }

        /// <summary>POST /news/admin/ticker  [admin]</summary>
        [Authorize(Roles = "admin")]
        [HttpPost("admin/ticker")]
        public IActionResult CreateTicker([FromBody] CreateTickerItemRequest req)
        {
            using var conn = OpenConnection();
            var now = DateTime.UtcNow;
            var cmd = new MySqlCommand(@"
                INSERT INTO ticker_items (text, status, sort_order, created_at, updated_at)
                VALUES (@Text, @Status, @SortOrder, @Now, @Now);
                SELECT LAST_INSERT_ID();", conn);
            cmd.Parameters.AddWithValue("@Text",      req.Text);
            cmd.Parameters.AddWithValue("@Status",    req.Status);
            cmd.Parameters.AddWithValue("@SortOrder", req.SortOrder);
            cmd.Parameters.AddWithValue("@Now",       now);
            var newId = Convert.ToInt32(cmd.ExecuteScalar());
            return Ok(GetTickerById(conn, newId));
        }

        /// <summary>PUT /news/admin/ticker/{id}  [admin]</summary>
        [Authorize(Roles = "admin")]
        [HttpPut("admin/ticker/{id:int}")]
        public IActionResult UpdateTicker(int id, [FromBody] UpdateTickerItemRequest req)
        {
            using var conn = OpenConnection();
            if (GetTickerById(conn, id) == null)
                return NotFound(new { message = "Ticker item not found." });

            var cmd = new MySqlCommand(@"
                UPDATE ticker_items
                SET text = @Text, status = @Status, sort_order = @SortOrder, updated_at = @Now
                WHERE id = @Id", conn);
            cmd.Parameters.AddWithValue("@Text",      req.Text);
            cmd.Parameters.AddWithValue("@Status",    req.Status);
            cmd.Parameters.AddWithValue("@SortOrder", req.SortOrder);
            cmd.Parameters.AddWithValue("@Now",       DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@Id",        id);
            cmd.ExecuteNonQuery();
            return Ok(GetTickerById(conn, id));
        }

        /// <summary>DELETE /news/admin/ticker/{id}  [admin]</summary>
        [Authorize(Roles = "admin")]
        [HttpDelete("admin/ticker/{id:int}")]
        public IActionResult DeleteTicker(int id)
        {
            using var conn = OpenConnection();
            if (GetTickerById(conn, id) == null)
                return NotFound(new { message = "Ticker item not found." });

            var cmd = new MySqlCommand("DELETE FROM ticker_items WHERE id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.ExecuteNonQuery();
            return Ok(new { message = "Ticker item deleted." });
        }

        // ─── HELPERS ─────────────────────────────────────────────

        private MySqlConnection OpenConnection()
        {
            var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();
            return conn;
        }

        private static List<Story> QueryStories(
            MySqlConnection conn,
            string where,
            Dictionary<string, object?>? parameters,
            string orderBy = "published_at DESC, created_at DESC")
        {
            var cmd = new MySqlCommand($@"
                SELECT id, bucket, category, title, excerpt, author,
                       read_time_minutes, tone, is_pinned, pin_order,
                       status, published_at, created_at, updated_at
                FROM stories
                WHERE {where}
                ORDER BY {orderBy}", conn);

            if (parameters != null)
                foreach (var (k, v) in parameters)
                    cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);

            var list = new List<Story>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(MapStory(reader));
            return list;
        }

        private static List<TickerItem> QueryTickers(MySqlConnection conn, string where)
        {
            var cmd = new MySqlCommand($@"
                SELECT id, text, status, sort_order, created_at, updated_at
                FROM ticker_items
                WHERE {where}
                ORDER BY sort_order ASC, created_at ASC", conn);

            var list = new List<TickerItem>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(MapTicker(reader));
            return list;
        }

        private static Story? GetStoryById(MySqlConnection conn, int id)
        {
            var cmd = new MySqlCommand(@"
                SELECT id, bucket, category, title, excerpt, author,
                       read_time_minutes, tone, is_pinned, pin_order,
                       status, published_at, created_at, updated_at
                FROM stories WHERE id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapStory(reader) : null;
        }

        private static TickerItem? GetTickerById(MySqlConnection conn, int id)
        {
            var cmd = new MySqlCommand(@"
                SELECT id, text, status, sort_order, created_at, updated_at
                FROM ticker_items WHERE id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapTicker(reader) : null;
        }

        private static void ArchiveExistingHero(MySqlConnection conn, int excludeId = 0)
        {
            var cmd = new MySqlCommand(@"
                UPDATE stories
                SET status = 'archived', updated_at = @Now
                WHERE bucket = 'heroStory' AND status = 'published' AND id != @ExcludeId", conn);
            cmd.Parameters.AddWithValue("@Now",       DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@ExcludeId", excludeId);
            cmd.ExecuteNonQuery();
        }

        private static void BindStoryParams(MySqlCommand cmd, CreateStoryRequest req, DateTime now)
        {
            cmd.Parameters.AddWithValue("@Bucket",      req.Bucket);
            cmd.Parameters.AddWithValue("@Category",    req.Category);
            cmd.Parameters.AddWithValue("@Title",       req.Title);
            cmd.Parameters.AddWithValue("@Excerpt",     req.Excerpt);
            cmd.Parameters.AddWithValue("@Author",      req.Author);
            cmd.Parameters.AddWithValue("@ReadTime",    req.ReadTimeMinutes);
            cmd.Parameters.AddWithValue("@Tone",        req.Tone);
            cmd.Parameters.AddWithValue("@IsPinned",    req.IsPinned);
            cmd.Parameters.AddWithValue("@PinOrder",    req.PinOrder);
            cmd.Parameters.AddWithValue("@Status",      req.Status);
            cmd.Parameters.AddWithValue("@PublishedAt", (object?)req.PublishedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Now",         now);
        }

        private static Story MapStory(MySqlDataReader r) => new Story
        {
            Id              = r.GetInt32("id"),
            Bucket          = r.GetString("bucket"),
            Category        = r.IsDBNull(r.GetOrdinal("category"))    ? "" : r.GetString("category"),
            Title           = r.GetString("title"),
            Excerpt         = r.IsDBNull(r.GetOrdinal("excerpt"))     ? "" : r.GetString("excerpt"),
            Author          = r.IsDBNull(r.GetOrdinal("author"))      ? "" : r.GetString("author"),
            ReadTimeMinutes = r.GetInt32("read_time_minutes"),
            Tone            = r.IsDBNull(r.GetOrdinal("tone"))        ? "" : r.GetString("tone"),
            IsPinned        = r.GetBoolean("is_pinned"),
            PinOrder        = r.GetInt32("pin_order"),
            Status          = r.GetString("status"),
            PublishedAt     = r.IsDBNull(r.GetOrdinal("published_at")) ? null : r.GetDateTime("published_at"),
            CreatedAt       = r.GetDateTime("created_at"),
            UpdatedAt       = r.GetDateTime("updated_at"),
        };

        private static TickerItem MapTicker(MySqlDataReader r) => new TickerItem
        {
            Id        = r.GetInt32("id"),
            Text      = r.GetString("text"),
            Status    = r.GetString("status"),
            SortOrder = r.GetInt32("sort_order"),
            CreatedAt = r.GetDateTime("created_at"),
            UpdatedAt = r.GetDateTime("updated_at"),
        };
    }

    // ─── Request DTOs ─────────────────────────────────────────────
    public record PatchStatusRequest(string Status);
    public record PatchPinRequest(bool IsPinned, int PinOrder);
}
