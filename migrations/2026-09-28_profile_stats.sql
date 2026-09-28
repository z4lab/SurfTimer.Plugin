-- SurfTimer migration 2026-09-28: !profile stats (run sync, points per map, attempts).
-- Run once on an existing database BEFORE deploying the plugin version that uses it - new runs are
-- saved with a `sync` value, which fails without the column. New installs get this from schema.sql.

-- Strafe sync (%) of every saved run - NULL for runs saved before it was tracked
ALTER TABLE `MapTimes` ADD COLUMN `sync` DECIMAL(5,2) NULL AFTER `run_time`;

-- Points per player, map and style (CS:GO SurfTimer formula, see PointsCalculator.cs), split into
-- the buckets shown in !profile. PlayerStats.points is the sum over all maps.
CREATE TABLE IF NOT EXISTS `PlayerMapPoints` (
    `player_id`         INT UNSIGNED NOT NULL,
    `map_id`            INT UNSIGNED NOT NULL,
    `style`             TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `points`            INT UNSIGNED NOT NULL DEFAULT 0,
    `map_points`        INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Map completion (by tier)',
    `wr_points`         INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Map WR',
    `top10_points`      INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Map rank 2-10',
    `group_points`      INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Map rank 11+ (G1-G5)',
    `bonus_points`      INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Bonus rank 2+',
    `bonus_wr_points`   INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Bonus WR',
    `segment_wr_points` INT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Stage / checkpoint segment WR',
    PRIMARY KEY (`player_id`, `map_id`, `style`),
    KEY `ix_playermappoints_map` (`map_id`, `style`),
    CONSTRAINT `fk_playermappoints_player`
        FOREIGN KEY (`player_id`) REFERENCES `Player` (`id`)
        ON DELETE CASCADE,
    CONSTRAINT `fk_playermappoints_map`
        FOREIGN KEY (`map_id`) REFERENCES `Maps` (`id`)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Runs started / finished per player, map, style and run type (0 map, 1 bonus, 2 stage)
CREATE TABLE IF NOT EXISTS `PlayerAttempts` (
    `player_id` INT UNSIGNED NOT NULL,
    `map_id`    INT UNSIGNED NOT NULL,
    `style`     TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `type`      TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `stage`     TINYINT UNSIGNED NOT NULL DEFAULT 0 COMMENT 'Bonus / stage number, 0 for map runs',
    `started`   INT UNSIGNED NOT NULL DEFAULT 0,
    `finished`  INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (`player_id`, `map_id`, `style`, `type`, `stage`),
    CONSTRAINT `fk_playerattempts_player`
        FOREIGN KEY (`player_id`) REFERENCES `Player` (`id`)
        ON DELETE CASCADE,
    CONSTRAINT `fk_playerattempts_map`
        FOREIGN KEY (`map_id`) REFERENCES `Maps` (`id`)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- PlayerStats (points, play_time in minutes) already exists and is used from now on.
-- Afterwards run !recalcpoints once in game (root admin) to fill points for existing runs.
