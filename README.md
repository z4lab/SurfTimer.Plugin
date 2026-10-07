<h1 align="center">
    <br>
    <img src="https://api.xace.ch/z4lab/z4lab-logo-new-transparent.webp" width="128px"/>
    <br>
	SurfTimer for CS2
</h1>

<p align="center">A fork of <b><a href="https://github.com/CS2Surf/Timer">CS2Surf/Timer</a></b></p>

<hr>

Click a section to expand it.

Every command works with `!` or `/` in chat, or with `css_` in the console (e.g. `!r`, `/r`, `css_r`). Commands aren't case sensitive: `!R` works like `!r`.
If popup menus are on, menus open as a clickable popup in the centre of the screen. Otherwise they show as a chat menu you use with `!1`, `!2`, …
Some menu rows ask you to type something in chat. Type `!cancel` to abort.

# Players

<details>
<summary><b>Movement & runs</b></summary>

| Command | Aliases | What it does |
|---|---|---|
| `!r` | | Back to the map start. Also takes you out of spectator. |
| `!rs` | | Back to the start of the stage or bonus you're on. Otherwise the map start. |
| `!s <n>` | `!stage` | Teleports you to stage *n* and resets your timer (stage mode). Staged maps only. |
| `!b <n>` | `!bonus` | Teleports you to bonus *n*. You stay on that bonus until you use `!r`, `!s` or `!b`. |
| `!repeat` | | Turns repeat mode on or off: after each stage you go back to that stage's start. Staged maps only. Not saved, so it's off every time you join. |
| `!startpos` | | Saves where `!r` / `!rs` / `!s` / `!b` put you in the start zone you're standing in, and which way you face. Stand still on the ground, not crouched. Kept until the map changes. **Deprecated soon:** will be replaced by saving a saveloc (`!saveloc`) in the start zone. |
| `!clearstartpos [all]` | | Removes your start position for this start zone. `all` removes every one. **Deprecated soon**, together with `!startpos`. |

</details>

<details>
<summary><b>Savelocs</b></summary>

| Command | Aliases | What it does |
|---|---|---|
| `!saveloc` | `!sl` | Saves your position as a server-wide `#id`. While spectating, it saves the player or bot you watch. In a start zone you must be standing still. |
| `!tele [#id]` | `!tp` | No id: back to your current saveloc. With an id: loads that saveloc, including someone else's (it's added to your set). |
| `!teleprev` / `!telenext` | | Previous / next saveloc in your set. |
| `!saveloclist` | `!slm` | Saveloc menu. Tabs: **My set**, **Recent** (everyone's latest), **Import**, **Info**. |

</details>

<details>
<summary><b>Records & stats</b></summary>

| Command | Aliases | What it does |
|---|---|---|
| `!map [map]` | `!mapinfo` `!mi` `!tier` `!difficulty` | Map panel for this map or another one. Tabs: **Map**, **Records**, **Stages**, **Checkpoints**, **Bonuses** (only the ones the map has). |
| `!maptop [map]` | `!mtop` | Opens the map panel on the records board. |
| `!stagetop [map] [n]` | `!stop` | Stage records. With a number, opens that stage's board. |
| `!cptop [map] [n]` | `!ctop` | Checkpoint records. |
| `!btop [map] [n]` | `!bonustop` | Bonus records. |
| `!rank` | | Your rank on this map for your current style. |
| `!profile [name]` | `!p` | Your profile, or another player's (online or offline). Tabs: **Overview**, **Points**, **Records**, **Recent**, **This map**, **Tiers**. |

</details>

<details>
<summary><b>Replays & spectating</b></summary>

| Command | Aliases | What it does |
|---|---|---|
| `!replay` | | Replay menu. Tabs: **Map** (WR, best segments, your PB), **Stages**, **Bonuses**, **Checkpoints**, **Your PBs**. |
| `!spec [name]` | | Spectates a player or bot by (partial) name. Without a name it opens a picker. |
| `!rbpause` | `!replaybotpause` | Pauses the replay bot you're spectating. |
| `!rbplay` | | Restarts the spectated replay from the beginning. |
| `!rbflip` | `!replaybotflip` | Plays the spectated replay forwards or backwards. |

</details>

<details>
<summary><b>Weapons</b></summary>

`!knife` · `!pistol` / `!gun` (your team's default pistol) · `!usp` · `!p2000` · `!glock`

</details>

<details>
<summary><b>Options shortcuts</b> (can be bound to keys)</summary>

| Command | Same as |
|---|---|
| `!hideself` | Options › Visibility › Hide own legs |
| `!hide` | Options › Visibility › Hide players |
| `!hidebots` | Options › Visibility › Hide replay bots |
| `!repeat` | Options › Gameplay › Repeat mode |

</details>

## Options (`!options` / `!settings`)

Everything here is saved to your profile, except **Repeat mode**. The menu reopens on the tab and page where you left it.

<details>
<summary><b>Visibility</b></summary>

| Option | Default | Description |
|---|---|---|
| Hide own legs | **On** | Hides your first-person legs. Other players still see your model. |
| Hide players | Off | Hides other players for you only. The player you spectate stays visible. |
| Hide replay bots | Off | Hides replay bots for you only. The bot you spectate stays visible. |
| Trails › | | Opens the trail settings below. |
| Show zones | Off | Zone outlines. Each press switches: **off → start & end → all** (all includes checkpoints and special zones). |

<details>
<summary>Visibility › Trails</summary>

| Option | Default | Description |
|---|---|---|
| Your trail | (info) | Your trail color, or "none". Trails go to the **top 100** and to **VIP / admin**. |
| My trail | On | *Only shown if you have a trail.* Off means nobody sees your trail. |
| Other players' trails | On | |
| My own trail | On | Whether you see your own trail. |
| Spectated trail | On | Trail of the player or bot you spectate. |
| Replay bot trails | On | |
| My trail color › | group color | *Only for **top 3, VIP and admin**.* Choose **Group color** (your rank or role color), **Rainbow**, a **Hex color** (type `#RRGGBB` in chat) or a color from the palette. |

</details>
</details>

<details>
<summary><b>HUD</b></summary>

If the server has the custom HUD turned off, a note at the top says so. Your settings take effect once it's turned on.

| Option | Default | Description |
|---|---|---|
| **Default** preset | ✔ | Timer, Speed \| Prespeed, Keys, Sync |
| **Compact** preset | | Timer, Speed, Keys |
| **Minimal** preset | | Timer, Speed |
| **Keys first** preset | | Keys, Timer, Speed \| Prespeed, Sync |
| Custom layout › | | Set each field yourself (see below). |
| Speed | XY | Each press switches: **XY** (horizontal) → **XYZ** (3D) → **Z** (vertical, negative = falling). Applies to the HUD speed, prespeed and split speeds. |
| Top bar | On | Rank, PB and WR at the top. |
| Splits panel › | PB | Left-side panel that compares your run against a target (see below). |
| Keep last splits | On | *Only shown when the splits panel is on.* Keeps the last run's splits after a fail or reset, until your next run starts. |
| Spectator list | On | Right side. |

<details>
<summary>HUD › Custom layout</summary>

The bottom HUD has up to **2 rows** of **4 slots** each. The **Timer takes 2 slots**. Each field can be used once.

For each field (**Timer, Speed, Prespeed, Keys, Sync**):
- **Show**: turn the field on or off. It goes into the first row with room.
- **Move earlier / Move later**: moves it left or right. At the end of a row it wraps to the row above or below.
- **Move to row 1 / 2**
- **Reset to default**: restores the default layout.

</details>

<details>
<summary>HUD › Splits panel</summary>

What the panel compares your run against:
- **Off**
- **Personal best**: your own PB run
- **World record**
- **Top 10**: the #10 time, or the time one rank above you
- **Group 1 – 5**: the points groups of this map. The menu shows each group's rank range. Groups appear once a map has 11 or more completions.

</details>
</details>

<details>
<summary><b>Chat</b></summary>

All on by default.

| Option | Description |
|---|---|
| Own split messages | Your checkpoint and stage comparisons. |
| Others' PBs | Personal bests of other players. |
| Others' records | WRs of other players. |
| Connect messages | Players joining. |
| Saveloc messages | "Teleported to #N" when you load a saveloc. |

</details>

<details>
<summary><b>Gameplay</b></summary>

| Option | Description |
|---|---|
| Repeat mode | Sends you back to the start of a stage after you finish it. Staged maps only. **Not saved.** Same as `!repeat`. |

</details>

# Admins

<details>
<summary><b>Permissions</b></summary>

Permissions are granted through CounterStrikeSharp (`admins.json` / admin groups). **`@css/root` has everything.**

| Permission | Grants |
|---|---|
| `@css/root` | Every admin panel tab, `!changemap`, the root name and trail color |
| `@surftimer/map` | Admin panel › **Map** tab (map data, zones, map settings) |
| `@surftimer/records` | Admin panel › **Records** tab |
| `@surftimer/players` | Admin panel › **Players** tab |
| `@surftimer/server` | Admin panel › **Server** tab (timer settings, chat, trails, bots) |
| `@surftimer/database` | Admin panel › **Database** tab |
| `@surftimer/audit` | Admin panel › **Audit** tab |
| `@css/changemap` | `!changemap` and Server › Change map |

**Role flags** for chat name colors, trail colors and the anti-spam bypass. They are set in `cfg/SurfTimer/chat_settings.json` → `flags`:

| Role | Default flag | Effect |
|---|---|---|
| Root | `@css/root` | Root name color and trail color. Can pick a custom trail color. Not limited by anti-spam. |
| Admin | `@css/generic` | Admin name color and trail color. Can pick a custom trail color. Not limited by anti-spam. |
| VIP | `@css/reservation` | VIP name color and trail color. Can pick a custom trail color. |

</details>

<details>
<summary><b>Commands</b></summary>

| Command | Aliases | Permission | What it does |
|---|---|---|---|
| `!surfadmin` | `!timeradmin` | at least one `@surftimer/*` section (or root) | Opens the admin panel with the tabs you have access to. It reopens on the last tab and page. |
| `!changemap [map]` | | `@css/changemap` (the server console can always use it) | Changes to a map from the map list. Part of a name is enough when it's unique. Without a name it opens Server › Change map. |

Every change made in the panel is written to the **Audit** log.

</details>

## Admin panel (`!surfadmin`)

<details>
<summary><b>Map</b> — <code>@surftimer/map</code></summary>

| Row | Description |
|---|---|
| Ranked | Whether the map gives points. |
| Map tier › | Tier 1–N, or no tier. |
| Stages › / Bonuses › | Per course: **Tier** (overrides the map tier), **Name**, **Gives points** (leave out e.g. a broken bonus), **Reset records** (danger). |
| Authors › | Set authors (comma separated) or remove them. |
| Workshop id | Type it in chat. |
| Map settings › | See below. |
| Zones › | See below. |
| Reset map records | **Danger.** Deletes times, replays and history. Points are recalculated. A preview shows the counts first. |

<details>
<summary>Map › Map settings</summary>

| Row | Description |
|---|---|
| Staged linear | *Only on staged maps.* Keeps the stages, with no speed cap in stage starts 2+. |
| Stages as checkpoints | Counts stages as checkpoints. A confirm page shows what gets deleted, and the **map restarts**. |
| Start speed cap › | Bhop cap in start zones: ±10 / ±50, No cap, or Use default. |
| Exit speed limit | Caps your speed when you leave a run start. |
| Exit limit value › | ±10 / ±50, or Use default. |
| Record replays | Stores replays of new PBs. |
| Cvars › | Movement cvars for this map only. Each one shows its current and server value. You can **Set for this map** or **Remove override**. Server values come back at map end. |
| Custom keys › | Free key/value notes. Add (`key value` in chat), change or remove. |

</details>

<details>
<summary>Map › Zones</summary>

Shows where zones are stored (database or map only), the stage/bonus/checkpoint counts and the edit history.
- **Edit zones** opens the zone editor. Only one admin can edit at a time.
- **Reload from map** (danger): replaces the stored zones with the map's own `trigger_multiple` zones.
- If the stage, bonus or checkpoint counts change, you're offered **Restart map now**. New numbered zones only work after a restart.

**Zone editor.** You stay alive in **noclip** with your timer stopped. Map teleports are undone and zones don't fire for you.

| Row | Description |
|---|---|
| Step | 1 / 4 / 16 / 64 units, used by every move or resize. |
| Aim mode | **Shoot** to set box corners or draw shapes. |
| Add zone › | Puts a 64³ box where you are. Pick a type: Map start/end, Stage N start, Checkpoint N, Bonus N start/end, Stop zone, Teleport back, Speed cap. |
| New shape / Finish shape / Undo point / Cancel shape | Aim mode: shoot the floor points in order to draw a polygon. |
| Save | Writes the zones to the database. Shows the number of changes. |
| Discard | Back to the saved zones. |
| Exit editor | Asks **Save and exit** or **Discard and exit** if you have unsaved changes. |
| *(zone list)* | Opens a zone (see below). |

**Editing a zone:** Step, Teleport here, Move ±X/±Y/up/down
- **Box zones:** Width± / Length± / Height±, **Faces ›** (grow or shrink one side), Set corner 1/2 here
- **Polygon zones:** Height±
- **Points ›**: move, add, delete (at least 3 stay) or re-shoot points
- **Set teleport here / Clear teleport**: where players land when they're sent to this zone
- **Speed cap zones:** Cap ±10/±50, or type a value
- **Type / number ›**, **Duplicate**, **Delete zone** (danger)
- Zones linked to a map trigger turn into polygon zones when you edit their shape.

</details>
</details>

<details>
<summary><b>Records</b> — <code>@surftimer/records</code></summary>

Lists the map, each stage, bonus and checkpoint with its completion count and WR. Open a course to see its leaderboard (**Load more** for more rows). Open a time to see:
- Date, sync, start/end speed, and whether a replay is stored
- **Play replay**, **Player ›** (opens Players)
- **Delete time** (danger): deletes the time and its replay, then recalculates points

</details>

<details>
<summary><b>Players</b> — <code>@surftimer/players</code></summary>

**Search** (online and offline, by name). Online players are listed below the search. Open a player to see:

| Row | Description |
|---|---|
| Points / Playtime / Last seen / Times | Info. Hidden times are counted separately. |
| Open profile | The normal `!profile` view. |
| Spectate | *Only when they're online.* You leave your run. |
| Names › / Sessions › | Name history and their latest visits. |
| Timer ban › | **1 day / 7 days / 30 days / Permanent**. Type the reason in chat. While banned they can still play, but their runs aren't saved and their times are hidden. |
| Unban | Shown while banned. Their times show again. |
| Wipe times on this map | Danger. |
| Wipe all times | Danger. Every map. |
| Reset settings | Puts their `!options` back to the defaults. |

</details>

<details>
<summary><b>Server</b> — <code>@surftimer/server</code></summary>

Info rows: SurfTimer version/commit, players and tickrate, memory.

| Row | Description |
|---|---|
| Change map › | *Needs `@css/changemap`.* Type a map name or workshop id, search, refresh the map list, or pick from the list. |
| Replay bots › | Each slot and what it's playing. Click a slot to stop it, or use **Stop all replays**. |
| Timer settings › | Saved to `timer_settings.json` (see below). |
| Chat › | See below. |
| Trails › | See below. |
| Restart map | Danger. Everyone's running times are lost. |

<details>
<summary>Server › Timer settings</summary>

| Setting | Description |
|---|---|
| Popup menus | Clickable centre menus instead of chat menus. |
| Block map chat | Blocks the map scripts' `say` messages. |
| Protect replay bots | Blocks map bot kicks. |
| Direct bot spawn | CreateBot instead of bot_quota. |
| Replays | Record and play replays. |
| Permanent map replay bot | Loops the map WR. Not counted in the max. |
| Clan tags | Players' clan tags on the scoreboard. |
| Country tag | `[DE]` in front of the clan tag. |
| Idle threshold | Stops replay recording for idle players (0 = off, max 3600 s). |
| Replay bots max | How many requested bots can run at a time (1–10). |
| Saveloc limit | Savelocs per map, all players combined. |
| Start speed cap | Default for all maps (0 = no cap). |
| Exit speed limit | Default for maps that turn it on. |
| Stage / CP WR points | Points per segment WR. |

</details>

<details>
<summary>Server › Chat</summary>

- **Chat processor**: on/off. Off means normal engine chat.
- **Format**: placeholders `{rank} {ranknum} {points} {name} {message} {country} {team} {prefix} {grey}` … You can also **Reset format**.
- **Rank colors ›**: #1, #2, #3, #4–#10, other ranks, no points.
- **Name colors ›**: Root, Admin, VIP, everyone else (can be the team color).
- **Anti-spam ›**: on/off, the minimum gap between messages (±0.5 s), and how long the same message is blocked (±5 s). Admins aren't limited.

</details>

<details>
<summary>Server › Trails</summary>

- **Trails**: on/off. Trails are for the top 100, VIP/admin/root and replay bots.
- Length ±0.5 s, Width ±1, how often a segment is drawn (±2 ticks; more often = smoother but more entities).
- **Colors ›**: #1, #2, #3, Top 10, Top 50, Top 100, VIP, Admin, Root, Replay bots. Each can be Rainbow, a hex color or a palette color.

</details>
</details>

<details>
<summary><b>Database</b> — <code>@surftimer/database</code></summary>

Info rows: engine and ping, schema version and migrations, database/prefix/pool, size. **Refresh** reloads them.
- **Totals ›**: players, maps, courses, times, replays, run history, open sessions, total playtime.
- **Tables ›**: rows and size per table. **Exact row counts** is available because InnoDB's counts are estimates.
- **Maintenance ›**
  - Recalculate all points (every map and style)
  - Close stale sessions (left open by a crash)
  - Delete orphaned replays (danger)
  - Purge run history › older than 30 / 90 / 365 days (danger; PBs are kept)
  - Optimize tables (danger; locks tables briefly)

</details>

<details>
<summary><b>Audit</b> — <code>@surftimer/audit</code></summary>

The last 100 admin actions: who did what, and when. Use **This map only** to filter to the current map.

</details>
