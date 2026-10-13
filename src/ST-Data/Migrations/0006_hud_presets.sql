-- SurfTimer schema v6 - shared HUD setups: an 8 character code for a set of HUD options (!options - HUD - Export / Import,
-- !hudexport / !hudimport). Codes stay forever and the same setup always gets the same code (settings_hash).
-- settings: a JSON object of player_settings keys -> values, only the HUD's (PlayerOptions.HudKeys) - keys left out are the
-- defaults. Written by the plugin, or by anything else (e.g. a web interface) with the same keys and a code that isn't taken
-- (Crockford base32: 0-9 and A-Z without I, L, O, U). No JSON column type - MariaDB stores it as text anyway.
CREATE TABLE IF NOT EXISTS `{p}hud_presets` (
    `code`          CHAR(8)      NOT NULL,
    `settings`      TEXT         NOT NULL,
    `settings_hash` CHAR(64)     NOT NULL,
    `created_by`    INT UNSIGNED NULL,
    `created_at`    DATETIME(3)  NOT NULL,
    `imports`       INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (`code`),
    UNIQUE KEY `ux_hud_presets_settings_hash` (`settings_hash`),
    CONSTRAINT `fk_{p}hud_presets_created_by` FOREIGN KEY (`created_by`) REFERENCES `{p}players` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
