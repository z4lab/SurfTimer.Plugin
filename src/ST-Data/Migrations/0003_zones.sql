-- SurfTimer schema v3 - zones stored per map. A map's zones are imported from its trigger_multiple
-- entities on its first load; from then on these rows are the zones (edited in !admin - Map - Zones).

-- One box per row (axis-aligned, world space). Several rows may share a type + number (a zone made of
-- several boxes). zone_type is ZoneType (0 map end, 1 map start, 2 stage start, 3 checkpoint, 4 bonus
-- start, 5 bonus end, 6 stop, 7 teleport back, 8 speed cap); number as the map names it (stage /
-- checkpoint / bonus number, 1 for the map start, 0 for unnumbered zones). tp_* is where players are
-- teleported into the zone (NULL = the box's bottom center). value: the speed cap of a speed cap zone.
CREATE TABLE IF NOT EXISTS `{p}zones` (
    `id`         INT UNSIGNED     NOT NULL AUTO_INCREMENT,
    `map_id`     INT UNSIGNED     NOT NULL,
    `zone_type`  TINYINT UNSIGNED NOT NULL,
    `number`     SMALLINT         NOT NULL DEFAULT 0,
    `name`       VARCHAR(64)      NOT NULL DEFAULT '',
    `min_x`      FLOAT            NOT NULL,
    `min_y`      FLOAT            NOT NULL,
    `min_z`      FLOAT            NOT NULL,
    `max_x`      FLOAT            NOT NULL,
    `max_y`      FLOAT            NOT NULL,
    `max_z`      FLOAT            NOT NULL,
    `tp_x`       FLOAT            NULL,
    `tp_y`       FLOAT            NULL,
    `tp_z`       FLOAT            NULL,
    `tp_pitch`   FLOAT            NULL,
    `tp_yaw`     FLOAT            NULL,
    `value`      FLOAT            NULL,
    `source`     TINYINT UNSIGNED NOT NULL DEFAULT 0,
    `created_at` DATETIME(3)      NOT NULL,
    `updated_at` DATETIME(3)      NOT NULL,
    PRIMARY KEY (`id`),
    KEY `ix_zones_map` (`map_id`),
    CONSTRAINT `fk_{p}zones_map` FOREIGN KEY (`map_id`) REFERENCES `{p}maps` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
