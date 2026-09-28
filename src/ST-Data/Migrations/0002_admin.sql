-- SurfTimer schema v2 - admin panel: hidden times (timer bans), replay sizes, per-course points,
-- bans and the admin audit log. One ALTER per table so MySQL (no ADD COLUMN IF NOT EXISTS) works too.

-- ---------------------------------------------------------------- times / replays / courses

-- Hidden times (banned players) are kept but left out of every leaderboard, rank and points query.
-- The leaderboard index gets `hidden` after the equality columns: rank / WR lookups stay range scans.
ALTER TABLE `{p}times`
    ADD COLUMN `hidden` BOOLEAN NOT NULL DEFAULT FALSE AFTER `replay_id`,
    ADD KEY `ix_times_board` (`course_id`, `style_id`, `hidden`, `run_time_ticks`),
    DROP KEY `ix_times_leaderboard`;

-- Compressed size of `data`, so replay storage can be summed without reading the blobs
ALTER TABLE `{p}replays`
    ADD COLUMN `data_size` INT UNSIGNED NOT NULL DEFAULT 0 AFTER `raw_size`;

UPDATE `{p}replays` SET `data_size` = LENGTH(`data`);

-- Courses can be left out of points (e.g. a broken bonus)
ALTER TABLE `{p}courses`
    ADD COLUMN `points_enabled` BOOLEAN NOT NULL DEFAULT TRUE AFTER `name`;

-- ---------------------------------------------------------------- bans

-- Timer bans: the player can play, but runs aren't saved and their times are hidden until the ban
-- ends (expires_at, NULL = permanent) or is lifted
CREATE TABLE IF NOT EXISTS `{p}player_bans` (
    `id`              INT UNSIGNED  NOT NULL AUTO_INCREMENT,
    `player_id`       INT UNSIGNED  NOT NULL,
    `admin_player_id` INT UNSIGNED  NULL,
    `reason`          VARCHAR(255)  NOT NULL DEFAULT '',
    `created_at`      DATETIME(3)   NOT NULL,
    `expires_at`      DATETIME(3)   NULL,
    `lifted_at`       DATETIME(3)   NULL,
    `lifted_by`       INT UNSIGNED  NULL,
    PRIMARY KEY (`id`),
    KEY `ix_player_bans_active` (`player_id`, `lifted_at`, `expires_at`),
    KEY `ix_player_bans_expiry` (`lifted_at`, `expires_at`),
    CONSTRAINT `fk_{p}player_bans_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}player_bans_admin` FOREIGN KEY (`admin_player_id`) REFERENCES `{p}players` (`id`) ON DELETE SET NULL,
    CONSTRAINT `fk_{p}player_bans_lifted_by` FOREIGN KEY (`lifted_by`) REFERENCES `{p}players` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------- audit

-- Every change made through !admin (admin_player_id NULL = server console)
CREATE TABLE IF NOT EXISTS `{p}admin_actions` (
    `id`              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    `admin_player_id` INT UNSIGNED    NULL,
    `action`          VARCHAR(64)     NOT NULL,
    `target_type`     VARCHAR(32)     NOT NULL DEFAULT '',
    `target_id`       BIGINT UNSIGNED NULL,
    `map_id`          INT UNSIGNED    NULL,
    `details`         VARCHAR(512)    NOT NULL DEFAULT '',
    `created_at`      DATETIME(3)     NOT NULL,
    PRIMARY KEY (`id`),
    KEY `ix_admin_actions_created` (`created_at`),
    KEY `ix_admin_actions_admin` (`admin_player_id`, `created_at`),
    KEY `ix_admin_actions_map` (`map_id`, `created_at`),
    CONSTRAINT `fk_{p}admin_actions_admin` FOREIGN KEY (`admin_player_id`) REFERENCES `{p}players` (`id`) ON DELETE SET NULL,
    CONSTRAINT `fk_{p}admin_actions_map` FOREIGN KEY (`map_id`) REFERENCES `{p}maps` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
