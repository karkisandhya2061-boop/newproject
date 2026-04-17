-- ============================================================
-- News Content Module - Migration (Fixed Version)
-- Compatible with MySQL 5.7+ and 8.x
-- ============================================================

-- ── STORIES TABLE ───────────────────────────────────────────

CREATE TABLE IF NOT EXISTS stories (
    id                 INT AUTO_INCREMENT PRIMARY KEY,

    bucket             ENUM('heroStory', 'featuredSideStories', 'trendingStories')
                           NOT NULL DEFAULT 'featuredSideStories',

    category           VARCHAR(100)  NULL,
    title              VARCHAR(500)  NOT NULL,
    excerpt            TEXT          NULL,
    author             VARCHAR(200)  NULL,
    read_time_minutes  INT           NOT NULL DEFAULT 1,
    tone               VARCHAR(100)  NULL,

    -- Added directly here (so no need for ALTER later on fresh install)
    is_pinned          TINYINT(1)    NOT NULL DEFAULT 0,
    pin_order          INT           NOT NULL DEFAULT 0,

    status             ENUM('draft', 'published', 'archived')
                           NOT NULL DEFAULT 'draft',

    published_at       DATETIME      NULL,
    created_at         DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at         DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP
                                     ON UPDATE CURRENT_TIMESTAMP,

    INDEX idx_bucket        (bucket),
    INDEX idx_status        (status),
    INDEX idx_bucket_status (bucket, status),
    INDEX idx_category      (category),
    INDEX idx_published_at  (published_at)
);

-- ── TICKER ITEMS TABLE ──────────────────────────────────────

CREATE TABLE IF NOT EXISTS ticker_items (
    id          INT AUTO_INCREMENT PRIMARY KEY,
    text        VARCHAR(1000) NOT NULL,
    status      ENUM('draft', 'published', 'archived')
                    NOT NULL DEFAULT 'published',
    sort_order  INT           NOT NULL DEFAULT 0,
    created_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP
                              ON UPDATE CURRENT_TIMESTAMP,

    INDEX idx_status     (status),
    INDEX idx_sort_order (sort_order)
);

-- ── SAFE INDEX CREATION (run separately if needed) ───────────

-- This may fail if already exists → safe to ignore error
CREATE INDEX idx_pinned_order 
ON stories (is_pinned, pin_order, updated_at);

-- ── OPTIONAL: ADD COLUMNS IF TABLE ALREADY EXISTS ───────────
-- (Run ONLY if your stories table was created earlier without these)

-- Check first:
-- DESCRIBE stories;

-- Then run manually ONLY if missing:
-- ALTER TABLE stories ADD COLUMN is_pinned TINYINT(1) NOT NULL DEFAULT 0;
-- ALTER TABLE stories ADD COLUMN pin_order INT NOT NULL DEFAULT 0;

-- ── SAMPLE SEED DATA ───────────────────────────────────────

INSERT INTO stories 
(bucket, category, title, excerpt, author, read_time_minutes, tone, status, published_at)
VALUES
('heroStory', 'Politics', 'Breaking: National Budget Announced',
 'The government unveiled the annual budget with record infrastructure spending.',
 'Jane Doe', 4, 'serious', 'published', UTC_TIMESTAMP()),

('featuredSideStories', 'Technology', 'AI Transforms Healthcare Diagnostics',
 'New AI tools are helping doctors detect diseases earlier than ever before.',
 'John Smith', 3, 'optimistic', 'published', UTC_TIMESTAMP()),

('featuredSideStories', 'Environment', 'Reforestation Drive Hits 1 Million Trees',
 'Community volunteers celebrate a major milestone in the green initiative.',
 'Sara Lee', 2, 'positive', 'published', UTC_TIMESTAMP()),

('trendingStories', 'Sports', 'National Team Advances to Semi-Finals',
 'A stunning comeback secured their place in the tournament''s final four.',
 'Mike Ray', 2, 'exciting', 'published', UTC_TIMESTAMP()),

('trendingStories', 'Science', 'Mars Rover Sends Unprecedented Images',
 'Scientists are analysing the clearest photographs of the Martian surface yet.',
 'Ann Cole', 5, 'wonder', 'published', UTC_TIMESTAMP());

INSERT INTO ticker_items (text, status, sort_order)
VALUES
('Markets close higher as investors await Fed decision', 'published', 1),
('Weather alert: Heavy rain expected across southern provinces', 'published', 2),
('Election commission confirms voter registration deadline', 'published', 3);