-- SurfTimer schema v5 - a player's in-progress run per map ("resume runs"): saved while it runs, restored when the
-- player spawns on that map again (any server on this database), deleted when the run ends.
-- state: versioned JSON written and read by the plugin only (no JSON column type - MariaDB stores it as text anyway).
-- replay: the run's replay frames so far (ReplayCodec), only on full saves (leaving / map end) - lightweight saves
-- every few seconds have none. revision: newer writes win, older ones arriving late don't overwrite them.
CREATE TABLE IF NOT EXISTS `{p}player_run_states` (
    `player_id`       INT UNSIGNED      NOT NULL,
    `map_id`          INT UNSIGNED      NOT NULL,
    `format_version`  SMALLINT UNSIGNED NOT NULL,
    `state`           MEDIUMTEXT        NOT NULL,
    `replay`          LONGBLOB          NULL,
    `replay_frames`   INT UNSIGNED      NOT NULL DEFAULT 0,
    `is_full`         BOOLEAN           NOT NULL DEFAULT FALSE,
    `practice`        BOOLEAN           NOT NULL DEFAULT FALSE,
    `run_ticks`       INT UNSIGNED      NOT NULL DEFAULT 0,
    `server_instance` CHAR(32)          NOT NULL,
    `revision`        BIGINT            NOT NULL,
    `created_at`      DATETIME(3)       NOT NULL,
    `updated_at`      DATETIME(3)       NOT NULL,
    PRIMARY KEY (`player_id`, `map_id`),
    KEY `ix_player_run_states_map` (`map_id`),
    KEY `ix_player_run_states_updated_at` (`updated_at`),
    CONSTRAINT `fk_{p}player_run_states_player` FOREIGN KEY (`player_id`) REFERENCES `{p}players` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_{p}player_run_states_map` FOREIGN KEY (`map_id`) REFERENCES `{p}maps` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
