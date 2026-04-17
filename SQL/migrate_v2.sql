-- ============================================================
-- Run this script in your MySQL news_portal database
-- ============================================================

-- Add username and is_active columns to existing users table
ALTER TABLE users 
  ADD COLUMN IF NOT EXISTS username VARCHAR(100) NULL UNIQUE AFTER last_name,
  ADD COLUMN IF NOT EXISTS is_active TINYINT(1) DEFAULT 1 AFTER role;

-- Create refresh_tokens table
CREATE TABLE IF NOT EXISTS refresh_tokens (
    id         INT AUTO_INCREMENT PRIMARY KEY,
    user_id    INT NOT NULL,
    token      VARCHAR(512) NOT NULL,
    expires_at DATETIME NOT NULL,
    is_revoked TINYINT(1) DEFAULT 0,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);
