-- SurfTimer schema v1 - applied automatically by the plugin (MigrationRunner), never by hand.
-- {p} is the table prefix from database.json. MariaDB 10.6+ / MySQL 8.0+, InnoDB, utf8mb4.
-- Dates are DATETIME(3) in UTC. Applied migrations must never change - add a new NNNN_*.sql instead.

-- ---------------------------------------------------------------- lookups

CREATE TABLE IF NOT EXISTS `{p}styles` (
    `id`         TINYINT UNSIGNED NOT NULL,
    `name`       VARCHAR(32)      NOT NULL,
    `short_name` VARCHAR(8)       NOT NULL,
    `enabled`    BOOLEAN          NOT NULL DEFAULT TRUE,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_styles_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO `{p}styles` (`id`, `name`, `short_name`) VALUES (0, 'normal', 'N');

CREATE TABLE IF NOT EXISTS `{p}course_kinds` (
    `id`   TINYINT UNSIGNED NOT NULL,
    `name` VARCHAR(16)      NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_course_kinds_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT IGNORE INTO `{p}course_kinds` (`id`, `name`) VALUES (1, 'map'), (2, 'stage'), (3, 'bonus'), (4, 'checkpoint');

-- ---------------------------------------------------------------- players

CREATE TABLE IF NOT EXISTS `{p}players` (
    `id`            INT UNSIGNED    NOT NULL AUTO_INCREMENT,
    `steam_id`      BIGINT UNSIGNED NOT NULL,
    `name`          VARCHAR(64)     NOT NULL,
    `country_code`  CHAR(2)         NULL,
    `first_seen_at` DATETIME(3)     NOT NULL,
    `last_seen_at`  DATETIME(3)     NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_players_steam_id` (`steam_id`),
    KEY `ix_players_name` (`name`),
    KEY `ix_players_last_seen_at` (`last_seen_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `{p}player_names` (
    `player_id`     INT UNSIGNED NOT NULL,
    `name`          VARCHAR(64)  NOT NULL,
    `first_used_at` DATETIME(3)  NOT NULL,
    `last_used_at`  DATETIME(3)  NOT NULL,
    PRIMARY KEY (`player_id`, `name`),
    KEY `ix_player_names_name` (`name`),
    CONSTRAINT `fk_{p}player_names_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `{p}player_settings` (
    `player_id`   INT UNSIGNED NOT NULL,
    `setting_key` VARCHAR(64)  NOT NULL,
    `value`       VARCHAR(255) NOT NULL,
    `updated_at`  DATETIME(3)  NOT NULL,
    PRIMARY KEY (`player_id`, `setting_key`),
    CONSTRAINT `fk_{p}player_settings_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `{p}player_stats` (
    `player_id`  INT UNSIGNED     NOT NULL,
    `style_id`   TINYINT UNSIGNED NOT NULL,
    `points`     INT UNSIGNED     NOT NULL DEFAULT 0,
    `updated_at` DATETIME(3)      NOT NULL,
    PRIMARY KEY (`player_id`, `style_id`),
    KEY `ix_player_stats_rank` (`style_id`, `points`),
    CONSTRAINT `fk_{p}player_stats_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}player_stats_style` FOREIGN KEY (`style_id`) REFERENCES `{p}styles` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------- maps and courses

CREATE TABLE IF NOT EXISTS `{p}maps` (
    `id`             INT UNSIGNED    NOT NULL AUTO_INCREMENT,
    `name`           VARCHAR(128)    NOT NULL,
    `workshop_id`    BIGINT UNSIGNED NULL,
    `ranked`         BOOLEAN         NOT NULL DEFAULT FALSE,
    `created_at`     DATETIME(3)     NOT NULL,
    `last_played_at` DATETIME(3)     NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_maps_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `{p}map_authors` (
    `map_id`   INT UNSIGNED     NOT NULL,
    `position` TINYINT UNSIGNED NOT NULL,
    `name`     VARCHAR(64)      NOT NULL,
    PRIMARY KEY (`map_id`, `position`),
    CONSTRAINT `fk_{p}map_authors_map` FOREIGN KEY (`map_id`) REFERENCES `{p}maps` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `{p}map_settings` (
    `map_id`      INT UNSIGNED NOT NULL,
    `setting_key` VARCHAR(64)  NOT NULL,
    `value`       VARCHAR(255) NOT NULL,
    `updated_at`  DATETIME(3)  NOT NULL,
    PRIMARY KEY (`map_id`, `setting_key`),
    CONSTRAINT `fk_{p}map_settings_map` FOREIGN KEY (`map_id`) REFERENCES `{p}maps` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- A map (number 0), each stage, bonus and linear checkpoint segment of a map
CREATE TABLE IF NOT EXISTS `{p}courses` (
    `id`         INT UNSIGNED      NOT NULL AUTO_INCREMENT,
    `map_id`     INT UNSIGNED      NOT NULL,
    `kind_id`    TINYINT UNSIGNED  NOT NULL,
    `number`     SMALLINT UNSIGNED NOT NULL,
    `tier`       TINYINT UNSIGNED  NULL,
    `name`       VARCHAR(64)       NULL,
    `created_at` DATETIME(3)       NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_courses_map_kind_number` (`map_id`, `kind_id`, `number`),
    KEY `ix_courses_kind` (`kind_id`),
    CONSTRAINT `fk_{p}courses_map` FOREIGN KEY (`map_id`) REFERENCES `{p}maps` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}courses_kind` FOREIGN KEY (`kind_id`) REFERENCES `{p}course_kinds` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Played / visited maps - playtime and visits come from here
CREATE TABLE IF NOT EXISTS `{p}player_sessions` (
    `id`                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    `player_id`         INT UNSIGNED    NOT NULL,
    `map_id`            INT UNSIGNED    NULL,
    `joined_at`         DATETIME(3)     NOT NULL,
    `last_heartbeat_at` DATETIME(3)     NOT NULL,
    `left_at`           DATETIME(3)     NULL,
    `duration_seconds`  INT UNSIGNED    NULL,
    PRIMARY KEY (`id`),
    KEY `ix_player_sessions_player` (`player_id`, `joined_at`),
    KEY `ix_player_sessions_map` (`map_id`),
    KEY `ix_player_sessions_open` (`left_at`, `last_heartbeat_at`),
    CONSTRAINT `fk_{p}player_sessions_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}player_sessions_map` FOREIGN KEY (`map_id`) REFERENCES `{p}maps` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------- replays and times

-- Replay data (ReplayCodec binary format) - kept apart so time queries never read blobs
CREATE TABLE IF NOT EXISTS `{p}replays` (
    `id`             INT UNSIGNED      NOT NULL AUTO_INCREMENT,
    `format_version` SMALLINT UNSIGNED NOT NULL,
    `tick_rate`      SMALLINT UNSIGNED NOT NULL,
    `frame_count`    INT UNSIGNED      NOT NULL,
    `raw_size`       INT UNSIGNED      NOT NULL,
    `data`           LONGBLOB          NOT NULL,
    `created_at`     DATETIME(3)       NOT NULL,
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Personal bests: one per player, course and style
CREATE TABLE IF NOT EXISTS `{p}times` (
    `id`               INT UNSIGNED     NOT NULL AUTO_INCREMENT,
    `player_id`        INT UNSIGNED     NOT NULL,
    `course_id`        INT UNSIGNED     NOT NULL,
    `style_id`         TINYINT UNSIGNED NOT NULL,
    `run_time_ticks`   INT UNSIGNED     NOT NULL,
    `sync`             DECIMAL(5,2)     NULL,
    `start_velocity_x` FLOAT            NOT NULL DEFAULT 0,
    `start_velocity_y` FLOAT            NOT NULL DEFAULT 0,
    `start_velocity_z` FLOAT            NOT NULL DEFAULT 0,
    `end_velocity_x`   FLOAT            NOT NULL DEFAULT 0,
    `end_velocity_y`   FLOAT            NOT NULL DEFAULT 0,
    `end_velocity_z`   FLOAT            NOT NULL DEFAULT 0,
    `replay_id`        INT UNSIGNED     NULL,
    `created_at`       DATETIME(3)      NOT NULL,
    `updated_at`       DATETIME(3)      NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_times_player_course_style` (`player_id`, `course_id`, `style_id`),
    KEY `ix_times_leaderboard` (`course_id`, `style_id`, `run_time_ticks`),
    KEY `ix_times_player_style` (`player_id`, `style_id`),
    KEY `ix_times_replay` (`replay_id`),
    CONSTRAINT `fk_{p}times_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}times_course` FOREIGN KEY (`course_id`) REFERENCES `{p}courses` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}times_style` FOREIGN KEY (`style_id`) REFERENCES `{p}styles` (`id`),
    CONSTRAINT `fk_{p}times_replay` FOREIGN KEY (`replay_id`) REFERENCES `{p}replays` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Checkpoint / stage splits of a map PB (replaced with every new PB)
CREATE TABLE IF NOT EXISTS `{p}time_splits` (
    `time_id`          INT UNSIGNED      NOT NULL,
    `split_number`     SMALLINT UNSIGNED NOT NULL,
    `enter_ticks`      INT UNSIGNED      NOT NULL,
    `exit_ticks`       INT UNSIGNED      NOT NULL DEFAULT 0,
    `enter_velocity_x` FLOAT             NOT NULL DEFAULT 0,
    `enter_velocity_y` FLOAT             NOT NULL DEFAULT 0,
    `enter_velocity_z` FLOAT             NOT NULL DEFAULT 0,
    `exit_velocity_x`  FLOAT             NOT NULL DEFAULT 0,
    `exit_velocity_y`  FLOAT             NOT NULL DEFAULT 0,
    `exit_velocity_z`  FLOAT             NOT NULL DEFAULT 0,
    `attempts`         SMALLINT UNSIGNED NOT NULL DEFAULT 1,
    PRIMARY KEY (`time_id`, `split_number`),
    CONSTRAINT `fk_{p}time_splits_time` FOREIGN KEY (`time_id`) REFERENCES `{p}times` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Every finished (non-practice) run, PB or not
CREATE TABLE IF NOT EXISTS `{p}run_history` (
    `id`             BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
    `player_id`      INT UNSIGNED     NOT NULL,
    `course_id`      INT UNSIGNED     NOT NULL,
    `style_id`       TINYINT UNSIGNED NOT NULL,
    `run_time_ticks` INT UNSIGNED     NOT NULL,
    `sync`           DECIMAL(5,2)     NULL,
    `is_pb`          BOOLEAN          NOT NULL DEFAULT FALSE,
    `finished_at`    DATETIME(3)      NOT NULL,
    PRIMARY KEY (`id`),
    KEY `ix_run_history_player` (`player_id`, `finished_at`),
    KEY `ix_run_history_course` (`course_id`, `style_id`, `finished_at`),
    CONSTRAINT `fk_{p}run_history_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}run_history_course` FOREIGN KEY (`course_id`) REFERENCES `{p}courses` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}run_history_style` FOREIGN KEY (`style_id`) REFERENCES `{p}styles` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Leaderboard cache per course and style - kept in sync by the save transaction
CREATE TABLE IF NOT EXISTS `{p}course_stats` (
    `course_id`   INT UNSIGNED     NOT NULL,
    `style_id`    TINYINT UNSIGNED NOT NULL,
    `completions` INT UNSIGNED     NOT NULL DEFAULT 0,
    `wr_time_id`  INT UNSIGNED     NULL,
    `updated_at`  DATETIME(3)      NOT NULL,
    PRIMARY KEY (`course_id`, `style_id`),
    KEY `ix_course_stats_wr` (`wr_time_id`),
    CONSTRAINT `fk_{p}course_stats_course` FOREIGN KEY (`course_id`) REFERENCES `{p}courses` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}course_stats_style` FOREIGN KEY (`style_id`) REFERENCES `{p}styles` (`id`),
    CONSTRAINT `fk_{p}course_stats_wr` FOREIGN KEY (`wr_time_id`) REFERENCES `{p}times` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------- points and attempts

-- Points per player and course (CS:GO SurfTimer formula, see PointsCalculator.cs).
-- rank_bucket: 0 none, 1 wr, 2 top 10, 3 group
CREATE TABLE IF NOT EXISTS `{p}player_course_points` (
    `player_id`         INT UNSIGNED     NOT NULL,
    `course_id`         INT UNSIGNED     NOT NULL,
    `style_id`          TINYINT UNSIGNED NOT NULL,
    `completion_points` INT UNSIGNED     NOT NULL DEFAULT 0,
    `rank_points`       INT UNSIGNED     NOT NULL DEFAULT 0,
    `rank_bucket`       TINYINT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (`player_id`, `course_id`, `style_id`),
    KEY `ix_player_course_points_course` (`course_id`, `style_id`),
    CONSTRAINT `ck_{p}player_course_points_bucket` CHECK (`rank_bucket` <= 3),
    CONSTRAINT `fk_{p}player_course_points_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}player_course_points_course` FOREIGN KEY (`course_id`) REFERENCES `{p}courses` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}player_course_points_style` FOREIGN KEY (`style_id`) REFERENCES `{p}styles` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Runs started per player and course - finished runs are counted from run_history
CREATE TABLE IF NOT EXISTS `{p}player_course_attempts` (
    `player_id` INT UNSIGNED     NOT NULL,
    `course_id` INT UNSIGNED     NOT NULL,
    `style_id`  TINYINT UNSIGNED NOT NULL,
    `started`   INT UNSIGNED     NOT NULL DEFAULT 0,
    PRIMARY KEY (`player_id`, `course_id`, `style_id`),
    KEY `ix_player_course_attempts_course` (`course_id`),
    CONSTRAINT `fk_{p}player_course_attempts_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}player_course_attempts_course` FOREIGN KEY (`course_id`) REFERENCES `{p}courses` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}player_course_attempts_style` FOREIGN KEY (`style_id`) REFERENCES `{p}styles` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
