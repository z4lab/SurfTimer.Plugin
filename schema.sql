-- CS2Surf Timer - Database schema (MySQL / MariaDB)
--
-- Generated from the project's DBML design, cross-checked against the raw SQL in
-- SurfTimer.Shared/Sql/Queries.cs and the entity/DTO classes in SurfTimer.Shared/Entities
-- and SurfTimer.Shared/DTO, so table/column names match exactly what Dapper queries
-- (Dapper is configured with `DefaultTypeMap.MatchNamesWithUnderscores = true`, so
-- PascalCase C# properties map to snake_case columns automatically).
--
-- `PlayerStats`, `PlayerSettings` and `MapTimeInsights` are part of the design but are
-- not read/written by any query in Queries.cs yet - they're included for completeness
-- but are effectively reserved for future use.
--
-- Create the database yourself before running this file, e.g.:
--   CREATE DATABASE surftimer CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
--   USE surftimer;

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- --------------------------------------------------------------------------
-- Player
-- --------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `Player` (
    `id`          INT UNSIGNED NOT NULL AUTO_INCREMENT,
    `steam_id`    BIGINT UNSIGNED NOT NULL COMMENT 'Unique SteamID64',
    `name`        VARCHAR(32) NOT NULL DEFAULT '',
    `country`     VARCHAR(2) NULL COMMENT 'ISO 3166-1 alpha-2',
    `join_date`   INT UNSIGNED NULL COMMENT 'Unix timestamp',
    `last_seen`   INT UNSIGNED NULL COMMENT 'Unix timestamp',
    `connections` INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_player_steam_id` (`steam_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------------------------
-- PlayerStats (reserved - not yet queried by the plugin)
-- --------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `PlayerStats` (
    `player_id` INT UNSIGNED NOT NULL,
    `style`     TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `points`    INT UNSIGNED NOT NULL DEFAULT 0,
    `play_time` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Minutes played',
    PRIMARY KEY (`player_id`, `style`),
    CONSTRAINT `fk_playerstats_player`
        FOREIGN KEY (`player_id`) REFERENCES `Player` (`id`)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------------------------
-- PlayerSettings (reserved - not yet queried by the plugin)
-- --------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `PlayerSettings` (
    `player_id`     INT UNSIGNED NOT NULL,
    `setting`       VARCHAR(32) NOT NULL COMMENT 'time_format_type, velocity_type, ...',
    `value`         VARCHAR(64) NOT NULL,
    `created_date`  INT UNSIGNED NULL COMMENT 'Unix timestamp',
    `last_modified` INT UNSIGNED NULL COMMENT 'Unix timestamp',
    PRIMARY KEY (`player_id`, `setting`),
    CONSTRAINT `fk_playersettings_player`
        FOREIGN KEY (`player_id`) REFERENCES `Player` (`id`)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------------------------
-- Maps
-- --------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `Maps` (
    `id`          INT UNSIGNED NOT NULL AUTO_INCREMENT,
    `name`        VARCHAR(64) NOT NULL,
    `tier`        TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `author`      VARCHAR(64) NULL,
    `stages`      TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = linear, 1+ = count of stages',
    `bonuses`     TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = none, 1+ = count of bonuses',
    `ranked`      TINYINT(1) NOT NULL DEFAULT 0,
    `staged_linear` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '1 = stage starts (except stage 1) allow bhop without the start-zone speed cap',
    `date_added`  INT UNSIGNED NULL COMMENT 'Unix timestamp',
    `last_played` INT UNSIGNED NULL COMMENT 'Unix timestamp',
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_maps_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------------------------
-- MapTimes
-- --------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `MapTimes` (
    `id`             INT UNSIGNED NOT NULL AUTO_INCREMENT,
    `player_id`      INT UNSIGNED NOT NULL,
    `map_id`         INT UNSIGNED NOT NULL,
    `style`          TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `type`           TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = map time, 1+ = bonus no. Must be 0 if stages > 0 - no stages in bonuses.',
    `stage`          TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'if type = 0: 0 = not staged, 1+ = stage no. Must be 0 if type > 0 - no stages in bonuses.',
    `run_time`       INT UNSIGNED NOT NULL,
    `start_vel_x`    DECIMAL(8,3) NOT NULL DEFAULT 0,
    `start_vel_y`    DECIMAL(8,3) NOT NULL DEFAULT 0,
    `start_vel_z`    DECIMAL(8,3) NOT NULL DEFAULT 0,
    `end_vel_x`      DECIMAL(8,3) NOT NULL DEFAULT 0,
    `end_vel_y`      DECIMAL(8,3) NOT NULL DEFAULT 0,
    `end_vel_z`      DECIMAL(8,3) NOT NULL DEFAULT 0,
    `run_date`       INT UNSIGNED NULL COMMENT 'Unix timestamp',
    `replay_frames`  LONGBLOB NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_maptimes_player_map_style_type_stage` (`player_id`, `map_id`, `style`, `type`, `stage`),
    KEY `ix_maptimes_map_id` (`map_id`),
    CONSTRAINT `fk_maptimes_player`
        FOREIGN KEY (`player_id`) REFERENCES `Player` (`id`)
        ON DELETE CASCADE,
    CONSTRAINT `fk_maptimes_map`
        FOREIGN KEY (`map_id`) REFERENCES `Maps` (`id`)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------------------------
-- MapTimeInsights (reserved - not yet queried by the plugin)
-- --------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `MapTimeInsights` (
    `maptime_id` INT UNSIGNED NOT NULL,
    PRIMARY KEY (`maptime_id`),
    CONSTRAINT `fk_maptimeinsights_maptime`
        FOREIGN KEY (`maptime_id`) REFERENCES `MapTimes` (`id`)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------------------------
-- Checkpoints
-- --------------------------------------------------------------------------
-- `maptime_id` + `cp` form the primary key: DB_QUERY_CR_INSERT_CP in Queries.cs relies on
-- an `ON DUPLICATE KEY UPDATE` against this pair to upsert a checkpoint's best attempt.
CREATE TABLE IF NOT EXISTS `Checkpoints` (
    `maptime_id`  INT UNSIGNED NOT NULL,
    `cp`          TINYINT UNSIGNED NOT NULL,
    `run_time`    INT UNSIGNED NOT NULL COMMENT 'start_touch',
    `start_vel_x` DECIMAL(8,3) NOT NULL DEFAULT 0,
    `start_vel_y` DECIMAL(8,3) NOT NULL DEFAULT 0,
    `start_vel_z` DECIMAL(8,3) NOT NULL DEFAULT 0,
    `end_vel_x`   DECIMAL(8,3) NOT NULL DEFAULT 0,
    `end_vel_y`   DECIMAL(8,3) NOT NULL DEFAULT 0,
    `end_vel_z`   DECIMAL(8,3) NOT NULL DEFAULT 0,
    `end_touch`   INT UNSIGNED NOT NULL DEFAULT 0,
    `attempts`    INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (`maptime_id`, `cp`),
    CONSTRAINT `fk_checkpoints_maptime`
        FOREIGN KEY (`maptime_id`) REFERENCES `MapTimes` (`id`)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET FOREIGN_KEY_CHECKS = 1;
