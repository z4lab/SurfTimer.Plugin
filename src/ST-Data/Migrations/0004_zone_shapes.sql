-- SurfTimer schema v4 - zone shapes. shape: 0 box (min / max), 1 prism (points + height: a polygon footprint whose
-- points each have their own height, extended straight up), 2 trigger (linked to a map trigger_multiple by name
-- and origin - detected by the trigger's own touches; points + height hold its rotated bounds for drawing).
-- points: JSON [[x,y,z],...]. min_* / max_* stay the bounds of every shape.
ALTER TABLE `{p}zones`
    ADD COLUMN `shape`            TINYINT UNSIGNED NOT NULL DEFAULT 0 AFTER `number`,
    ADD COLUMN `points`           TEXT             NULL AFTER `max_z`,
    ADD COLUMN `height`           FLOAT            NULL AFTER `points`,
    ADD COLUMN `trigger_name`     VARCHAR(64)      NULL AFTER `height`,
    ADD COLUMN `trigger_origin_x` FLOAT            NULL AFTER `trigger_name`,
    ADD COLUMN `trigger_origin_y` FLOAT            NULL AFTER `trigger_origin_x`,
    ADD COLUMN `trigger_origin_z` FLOAT            NULL AFTER `trigger_origin_y`;
