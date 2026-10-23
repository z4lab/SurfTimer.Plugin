<h1 align="center">
    <br>
    <img src="assets/logo.svg" width="128px" alt="z4lab logo"/>
    <br>
	SurfTimer for CS2
</h1>

<p align="center">A fork of <b><a href="https://github.com/CS2Surf/Timer">CS2Surf/Timer</a></b></p>

<hr>

Click a section to expand it.

Every command works with `!` or `/` in chat, or with `css_` in the console (e.g. `!r`, `/r`, `css_r`). Commands aren't case sensitive: `!R` works like `!r`.
If popup menus are on, menus open as a clickable popup in the centre of the screen, with the mouse cursor on. Otherwise they show as a chat menu you use with `!1`, `!2`, …

- `!cursor` turns the cursor on or off: turn it off to move with the popup still shown, and on again to click. It also works without a popup. Bind it: `bind <key> css_cursor`.
- `!close` closes the popup. **E** (use) closes it too, and so does starting to move (W / A / S / D / jump) while the cursor is on. ESC can't be detected by the server.
Some menu rows ask you to type something in chat. Type `!cancel` to abort.

# Players

<details>
<summary><b>Movement & runs</b></summary>

| Command | Aliases | What it does |
|---|---|---|
| `!help [command]` | | Every command in a popup, a tab per topic from basic to advanced: **Basics**, **Practice**, **Records**, **Spectate**, **Options**, **Admin** (only with admin permissions, only the commands you may use). Addons (map chooser, paint, admin plugin) add theirs. Each command opens its details, with **Run** for commands that need no arguments. With a command (`!help saveloc`, aliases work too): its details in chat. New players get a hint once. |
| `!r` | | Back to the map start. Also takes you out of spectator. During a run you type it twice within 3 seconds: the first one only warns, with a quiet sound (turn this off in Options › Gameplay). |
| `!rs` | `!back` | Back to the start of the stage or bonus you're on. Otherwise the map start. From spectator: back to where you were (see **Joining, resuming & spectating**). |
| `!s [n]` | `!stage` | Teleports you to stage *n* and resets your timer (stage mode). Staged maps only. Without a number it opens a picker with every stage and its tier. |
| `!b [n]` | `!bonus` | Teleports you to bonus *n*. You stay on that bonus until you use `!r`, `!s` or `!b`. Without a number: bonus 1 if the map has only one, otherwise a picker with every bonus and its tier. |
| `!end` | | Teleports you to the end of the course you're on (map, bonus, or the stage in stage mode). Only from that course's start zone. The timer stops and doesn't finish there. With several end zones you pick one from a menu. |
| `!repeat` | | Turns repeat mode on or off: after each stage you go back to that stage's start. Staged maps only. Not saved, so it's off every time you join. |
| `!noclip` | `!nc` | Fly through everything. Your run is reset and your timer stays **off** until `!r` or `!rs`, which also end noclip: no zones, no runs, no savelocs. `!noclip` again lands you where you are (the timer stays off); `!s`, `!b` and `!end` still teleport you. Dying ends noclip, not the turned-off timer. The HUD shows **Noclip**. Map teleports: Options › Gameplay › Noclip teleports. |

</details>

<details>
<summary><b>Joining, resuming & spectating</b></summary>

- **Joining a map** puts you in the map start zone, like `!r`, not at the map's spawn point.
- **Your run is kept.** While your timer runs, it's saved every 5 seconds, and fully (with its replay so far) when you leave or the map changes. Join the same map again, on this server or any other server of ours, and you continue where you were: same spot, view, speed, time, stage and splits. It still counts for PBs and records.
  - After a server crash you continue from at most 5 seconds earlier, but that run has no replay.
  - A saved run is kept for **3 days** (**7 days** for VIPs). The server can change both.
  - It isn't restored if the map's zones changed since. `!r`, `!rs`, `!s`, `!b` or loading a saveloc before it's restored starts you over instead.
  - Practice runs (from a saveloc) come back as practice.
- **Spectating** (`!spec` or the M menu) pauses your run: the time stops. `!rs` / `!back` or joining a team again puts you at the start of the stage or bonus you were on, and the run goes on. `!r` starts over instead. Without a running timer, coming back puts you at the start of the bonus you were on, or on staged maps the stage you were on (stage mode). Otherwise the map start.

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
<summary><b>Map filters</b></summary>

Some maps only let you through a teleport or push after you passed an earlier part: a trigger gives you a context
(`!activator AddContext pass:1`, the CS2 way) or a name (targetname, CS:GO ports), and a filter checks for it later.
The timer keeps that in line:

- **Savelocs** store the contexts and name of the saved player (when spectating: the player you watch). Loading one
  gives you exactly that state. Replay savelocs have none and give you the course's start state.
- **Resumed runs** come back with the state they were saved with.
- **`!r`, `!s`, `!b`** and coming back from spectator put you at a start with the start state: no contexts and
  no name, or the map's start name (see the admin panel). The map's own triggers in the start zone still give theirs.
- **`!rs` / `!back`** during a map run gives the state you left that stage start with.
- Walking into a start zone changes nothing (the map handles that).
- On maps with a start name set, a run that starts with another name gets the start name (logged).

Not covered: attributes (`filter_activator_attribute_int`), classnames, context expiry times and the map's delayed
outputs still pending when you teleport.

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
| `!top` | | Players by server rank (normal style). A player opens their profile. |

</details>

<details>
<summary><b>Replays & spectating</b></summary>

| Command | Aliases | What it does |
|---|---|---|
| `!replay` | | Replay menu. Tabs: **Map** (WR, best segments, your PB), **Stages**, **Bonuses**, **Checkpoints**, **Your PBs**. |
| `!spec [name]` | | Spectates a player or bot by (partial) name. Without a name it opens a picker. Your run is paused: `!back` continues it. |
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
| `!quake` | Options › Sound › Sounds (all timer sounds on / off) |
| `!hudexport` | Options › HUD › Share HUD › Export HUD |
| `!hudimport <code>` | Options › HUD › Share HUD › Import HUD |

</details>

## Options (`!options` / `!settings`)

Everything here is saved to your profile. The menu reopens on the tab and page where you left it.

<details>
<summary><b>Visibility</b></summary>

| Option | Default | Description |
|---|---|---|
| Hide own legs | **On** | Hides your first-person legs. Other players still see your model. Spectating a player in first person shows their own setting (it's one value per player); replay bots never show legs. |
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

The HUD uses the [Nord](https://www.nordtheme.com) colors. Its accent (field borders, side panels, menus) is blue on CT and yellow on T - while spectating, the team of who you watch.

One page per part of the HUD. Each part's **Position** moves it one step per press (0-10). After 10 it goes back to the server's default.

| Option | Default | Description |
|---|---|---|
| Bottom HUD › | Default | The timer and speed fields at the bottom: a preset or your own layout, and its position (see below). |
| Center HUD › | Off | Fields in three lines around the crosshair (custom HUD): by default your speed just above it (see below). |
| Top bar › | On | Map, tier and stage, then a smaller row with rank, PB and WR. With [SurfTimer.MapChooser](https://github.com/z4lab/SurfTimer.MapChooser), a third row shows the time left, the running vote and the map's end (needs the HUD addon with four top rows). **Position**: default (4), 40-240px from the top. |
| Splits panel › | | The left-side panel. What it compares against is set in the **Compare** tab. **Keep last splits** (on, only shown when the panel is on): keeps the last run's splits after a fail or reset, until your next run starts. **Position**: default (5), 200-600px from the top. |
| Spectator list › | On | Right side. **Position**: default (5), 200-600px from the top. |
| Speed › | XY · Gradient | **Axes**: each press switches **XY** (horizontal) → **XYZ** (3D) → **Z** (vertical, negative = falling). Applies to the HUD speed, prespeed and split speeds. **Color** of the speed and prespeed (the center HUD too, unless it has its own color): **Gradient** (Nord colors, slow blue → fast red), **Gain / loss** (white, green while gaining speed, red while losing), or one Nord color (snow, frost, teal, blue, deep blue, red, orange, yellow, green, purple). |
| Share HUD › | | **Export HUD** makes a code for **all** your HUD and Compare settings (layouts, center HUD, colors, positions, splits, pace, ghost & path), shown in chat - e.g. `7K2M9QPX`. The same settings always get the same code, and codes stay forever. Same as `!hudexport`. **Import HUD**: type a code in chat, every HUD and Compare setting becomes the code's, settings the code doesn't have go back to their defaults. Same as `!hudimport <code>`. |

<details>
<summary>HUD › Bottom HUD</summary>

| Option | Default | Description |
|---|---|---|
| **Default** preset | ✔ | Timer, Speed \| Prespeed, Keys, Sync |
| **Compact** preset | | Timer, Speed, Keys |
| **Minimal** preset | | Timer, Speed |
| **Keys first** preset | | Keys, Timer, Speed \| Prespeed, Sync |
| Custom layout › | | Set each field yourself (see below). |
| Position | default (3) | 100-300px from the bottom. |

</details>

<details>
<summary>HUD fields</summary>

Every field below can go into the bottom HUD and the center HUD:

| Field | Shows |
|---|---|
| Timer | Run time. Green running, yellow once slower than your PB / WR (Compare › Timer warning), purple in practice. |
| Segment time | Time in the current stage / checkpoint. |
| Speed / Prespeed | On your speed axes. Prespeed: live in a start zone, then the speed you left it with. |
| Keys | W A S D, jump (J), crouch (C). The center shows held keys, `_` for released ones. |
| Sync / Segment sync | Strafe sync of the run / of the current stage or checkpoint. |
| Energy | See below. |
| Pace / Pace delta | Projected finish and how far ahead (green) or behind (red), against the splits panel's target (see Compare › Pace). |
| Energy vs ref / Speed vs ref | Your energy / speed compared to the target run at the same spot. |
| Last split | Your last checkpoint / stage and its difference to the splits panel's target. |
| Stage / CP | Stage 3/7, CP 4/12 or Bonus 2. |
| PB / WR / Rank | For the run you're on (map, stage or bonus). |
| Jumps / Strafes | In the current run. A strafe is switching to the other strafe key in the air. |
| Addon fields | Added by addons, e.g. **Time left** from [SurfTimer.MapChooser](https://github.com/z4lab/SurfTimer.MapChooser). |

A field without a value (no PB yet, no target run, spectating a replay) shows N/A in the bottom HUD and is left out in the center.

</details>

<details>
<summary>HUD › Bottom HUD › Custom layout</summary>

The bottom HUD has up to **2 rows** of **4 slots** each. The **Timer takes 2 slots**. Each field can be used once.

For each field:
- **Show**: turn the field on or off. It goes into the first row with room.
- **Move earlier / Move later**: moves it left or right. At the end of a row it wraps to the row above or below.
- **Move to row 1 / 2**
- **Reset to default**: restores the default layout.

**Energy** (not in the presets) is your mechanical energy, as in Momentum Mod: the height your speed could carry you to plus how high you are above where the run started - `speed² / (2 · gravity) + height`. Air strafing raises it; ramp hits, walls and friction lower it, so it shows how clean your surf is. Green while it rises, red while it drops.

</details>

<details>
<summary>HUD › Center HUD</summary>

Three lines around the crosshair: **line 1** above it, **line 2** at it, **line 3** below it. Each line shows up to **3 fields** in one text (`1234 · 01:23.456`), colored like its first field.

| Row | Description |
|---|---|
| Center HUD | On / off. |
| Fields › | Which fields, on which line - the same pages as the custom layout (**Show**, **Move earlier / later**, **Move to line N**). Default: Speed on line 1. |
| Line 1 / 2 / 3 › | **Size** 1–5 (default 3), **Move up** / **Move down** (10 steps each way, 10px each), **Reset position**. |
| Labels | Off: only the values. On: `Speed 1234`. |
| Monospace | On: digits keep their place. |
| Speed color › | The same as the HUD speed (default), or its own gradient, gain / loss or Nord color. |

Addons can show a message in a line for a few seconds (e.g. a kick notice from Admin.Plugin). The line shows the message instead of its fields, then goes back to them.

</details>
</details>

<details>
<summary><b>Compare</b></summary>

What your run races against.

| Option | Default | Description |
|---|---|---|
| Splits panel › | PB | The left-side panel that compares your run against a target (see below). |
| Pace | Off | *Only shown when the splits panel is on.* Under the splits: your projected finish time, live, and how far ahead (green) or behind (red) you are - against the splits panel's target (in stage / bonus mode: that stage's / bonus's PB, or its WR for any other target). It matches your position on the target run's replay every update, so it moves while you surf, not only at checkpoints. Without a replay it's worked out from the splits (shown with a `~`). The splits panel shows 4 splits instead of 6 then. |
| Energy vs reference | Off | *Only shown with Pace on.* Under the pace: your energy and speed compared to the target run at the same spot (+ green / − red). |
| Timer warning | PB | The running timer is green, and turns yellow once your run is slower than your **PB** of it (map, stage or bonus) - or the **WR**. Practice runs stay purple. |
| Ghost & path › | Off | A run's path ahead of you and a ghost racing you - see below. |

<details>
<summary>Compare › Splits panel</summary>

What the panel compares your run against:
- **Off**
- **Personal best**: your own PB run
- **World record**
- **Top 10**: the #10 time, or the time one rank above you
- **Group 1 – 5**: the points groups of this map. The menu shows each group's rank range. Groups appear once a map has 11 or more completions.

</details>

<details>
<summary>Compare › Ghost & path</summary>

Only you and the players spectating you see these.

| Row | Description |
|---|---|
| Path | The path of the run you follow, the next few seconds of it ahead of where you are on it, colored by its speed there (Nord gradient, slow blue → fast red). In a start zone it shows the start of the run, so you can preview the route. Shown in runs and practice. |
| Jump & crouch markers | On the path: a flat **square** where the run jumped, a **triangle pointing down** where it crouched and **pointing up** where it stood up again. |
| Path length | 2 / 4 / 6 / 8 seconds of the run ahead of you (default 4). |
| Ghost | An orb where the run was at your current run time - ahead of you when it was faster. Shown while your timer runs, colored by its speed. |
| Run › | Which run: the **splits target** (default), your **PB**, the **WR**, the **next rank** (the time one rank above yours), **top 10**, or the last rank of **Group 1 – 5**. In stage / bonus mode: that stage's / bonus's PB (Personal best) or WR (anything else). |
| Following | The run's time - or why there's nothing to show (no such run yet, no replay stored for it). |

Both need the run's stored replay.

</details>
</details>

<details>
<summary><b>Chat</b></summary>

Every finish and split message shows the time's rank after it, `1:23.456 [3/120]`: the rank on that course (map, stage, bonus or checkpoint segment) and how many players have finished it. A run that isn't a PB shows where it would place, practice runs too. Splits inside a map run (CP / stage splits) are ranked among the splits of every player's map PB. The times are kept in memory (loaded with the map's records), so the rank is there right away.

| Option | Description |
|---|---|
| Own messages › | Run messages about **your** runs. They also count for whoever you **spectate**: a player's messages, or a replay bot's splits and finish (compared with your own PB there). All on by default. |
| Others' messages › | The same messages about **other players'** runs. PBs and WRs on by default, finishes and splits off. |
| Compact messages | Run messages in a short form, e.g. `Player CP1 WR 11.609 [1/6] -00.046 (was 11.656)` instead of `Player has set a new Checkpoint 1 record with a time of …`. Splits drop the speeds. Off by default. |
| Connect messages | Players joining. |
| Saveloc messages | "Teleported to #N" when you load a saveloc. |
| Show my role | Only for players with a role (e.g. `[Admin]` and a name color from Admin.Plugin's groups): show the tag before your name and the name color in chat, or hide both. |

Both message pages have a toggle for each kind, plus **All on** / **All off**:

| Message | What it is |
|---|---|
| Splits vs map PB / WR | In a map run, at each checkpoint / stage: the time there against the map PB's / WR's time there. |
| Splits vs stage / CP PB / WR | The time of one stage / checkpoint segment against its own PB / WR. |
| Map finish / Bonus finish | A finish without a PB. |
| Map / Stage / CP / Bonus PB | A new personal best. |
| Map / Stage / CP / Bonus WR | A new record. |

</details>

<details>
<summary><b>Gameplay</b></summary>

| Option | Default | Description |
|---|---|---|
| Confirm !r | **On** | During a run, `!r` has to be typed twice within 3 seconds. The first one only warns (chat and the Reset warning sound). Not for practice runs, the first 5 seconds of a run, or from spectator. |
| Noclip teleports | **Set back** | What map teleports (`trigger_teleport`) do to you in noclip (`!noclip` and the zone editor). **Normal**: they work. **Set back**: you're put back right away. **Disabled**: they don't touch you at all. Disabled hooks `trigger_teleport` with the offsets from CS2Fixes' gamedata (`addons/cs2fixes/gamedata/cs2fixes.jsonc`); without CS2Fixes it works like set back. |

</details>

<details>
<summary><b>Sound</b></summary>

Every sound plays only for you, at your volume. Turning a sound on plays a sample.

| Option | Default | Description |
|---|---|---|
| Sounds | On | Main switch for every sound below. Same as `!quake`. |
| Volume | 20% | Each press goes one step up (off, 10, 20, 30, 50, 75, 100%), then back to off, and plays a sample. |
| Timer start | On | Your run starts (map, stage or bonus). |
| Ahead of PB | On | You reach a checkpoint or stage faster than in your PB. |
| New PB | On | A new personal best on the map, a stage, a bonus or a checkpoint. |
| Your record | On | Your WR on the map, a stage, a bonus or a checkpoint. |
| Others' records | Off | Another player sets a WR. |
| Reset warning | On | The first `!r` during a run (Options › Gameplay › Confirm !r). |
| Missed PB | On | Your run (map, stage or bonus) got slower than your PB of it. Once per run. |
| Missed WR | On | Your run got slower than the WR. Once per run. |
| Map vote / Vote countdown | On | *Only with [SurfTimer.MapChooser](https://github.com/z4lab/SurfTimer.MapChooser).* A vote starts / its last 3 seconds. Addons add their sounds here. |

The server can change which CS2 sound event each kind plays in `timer_settings.json`: `sound_timer_start`, `sound_ahead_pb`, `sound_pb`, `sound_record`, `sound_others_record`, `sound_reset_confirm`, `sound_missed_pb`, `sound_missed_wr` (`""` = no sound).

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
| VIP | `@z4lab/vip` | VIP name color and trail color. Can pick a custom trail color. |

</details>

<details>
<summary><b>Commands</b></summary>

| Command | Aliases | Permission | What it does |
|---|---|---|---|
| `!surfadmin` | `!timeradmin` | at least one `@surftimer/*` section (or root) | Opens the admin panel with the tabs you have access to. It reopens on the last tab and page. |
| `!changemap [map]` | | `@css/changemap` (the server console can always use it) | Changes to a map from the map list. Part of a name is enough when it's unique. Without a name it opens Server › Change map. |
| `!cleartrails` | | `@surftimer/server` (the server console can always use it) | Removes every trail and ghost & path beam, stuck pieces too. Trails start over, ghosts are drawn again. Trail beams no trail tracks any more are also removed by themselves every 5 seconds. |

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
| Workshop id | Type it in chat. Filled in by itself: after every map start the timer reads which workshop addon each map comes from and stores missing or different ids for every map in the database (maps not stored yet get theirs on their first load). |
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
| Prehop per start zone › | The anti-prehop cap for single start zones: map start, each stage start, each bonus start. Each press cycles **capped** → **free** → **map rule**. Map rule is the old behavior: map and bonus starts capped, stage starts capped unless **Staged linear** is on. |
| Exit speed limit | Caps your speed when you leave a run start. |
| Exit limit value › | ±10 / ±50, or Use default. |
| Record replays | Stores replays of new PBs. |
| Filters › | Map filter names (see **Map filters**). **Reset on start teleports**: `!r` / `!s` / `!b` clear your contexts and set the start name (default). Turn it off for maps that give the state only once at the very start. **Start name**: the name runs start with, also checked when a run starts. Per stage / bonus as custom keys `filter_start_name_s3`, `filter_start_name_b1`. |
| Cvars › | Movement cvars for this map only. Each one shows its current and server value. You can **Set for this map** or **Remove override**. Server values come back at map end. A map without a stored `sv_maxvelocity` gets the one active 10 seconds after it loaded (after its settings came from the database), stored as its override. |
| Custom keys › | Free key/value notes. Add (`key value` in chat), change or remove. |

</details>

<details>
<summary>Map › Zones</summary>

Shows where zones are stored (database or map only), the stage/bonus/checkpoint counts and the edit history.
- **Edit zones** opens the zone editor. Only one admin can edit at a time.
- **Reload from map** (danger): replaces the stored zones with the map's own `trigger_multiple` zones.
- If the stage, bonus or checkpoint counts change, you're offered **Restart map now**. New numbered zones only work after a restart.

**Zone editor.** You stay alive in **noclip** with your timer stopped. Zones don't fire for you, and map teleports follow your Options › Gameplay › Noclip teleports (normal / set back / disabled, like `!noclip`).

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
| Map chooser › | *Only with [SurfTimer.MapChooser](https://github.com/z4lab/SurfTimer.MapChooser) installed. Needs its admin flag (`@css/changemap` by default).* Time, votes, next map and per-map length and cooldown. Documented in the map chooser's README. Addons add their pages here through SurfTimer.Api. |
| Restart map | Danger. Running runs are saved and resume after the restart, unless the zones changed. |

<details>
<summary>Server › Timer settings</summary>

| Setting | Description |
|---|---|
| Popup menus | Clickable centre menus instead of chat menus. |
| Freeze round timer | CS2's round timer (top centre) stands still at 13:37. A round time that runs out would kick every player as idle. |
| Block map chat | Blocks the map scripts' `say` messages. |
| Protect replay bots | Blocks map bot kicks. |
| Replays | Record and play replays. |
| Permanent map replay bot | Loops the map WR. Not counted in the max. |
| Clan tags | Players' clan tags on the scoreboard. |
| Country tag | `[DE]` in front of the clan tag. |
| Rank badges | The scoreboard's Premier rating badge shows each player's server points, colored by the game (grey under 5,000, gold from 30,000). Unranked players and replay bots show 0. Needs `FollowCS2ServerGuidelines: false` in CounterStrikeSharp's `core.json` - with it on, the badges stay off (logged once). |
| Idle threshold | Stops replay recording for idle players (0 = off, max 3600 s). |
| Ghost orb respawn | Every how many ticks a player's ghost orb (Ghost & path) is spawned again where it moved: 1 is smoothest, higher spawns fewer entities (max 16). Default 1. `ghost_orb_ticks` |
| Resume expiry | Days a player's saved run is kept after they leave (0 = never expires, max 365). Default 3. `resume_expiry_days` |
| Resume expiry (VIP) | The same for VIPs (VIP, admin or root flag from chat_settings.json). Default 7. `resume_expiry_vip_days` |
| Replay bots max | How many requested bots can run at a time (1–10). |
| Saveloc limit | Savelocs per map, all players combined. |
| Start speed cap | Default for all maps (0 = no cap). |
| Exit speed limit | Default for maps that turn it on. |
| Stage / CP WR points | Points per segment WR. |

</details>

<details>
<summary>Server › Chat</summary>

- **Chat processor**: on/off. Off means normal engine chat.
- **Format**: placeholders `{rank} {ranknum} {points} {name} {message} {country} {team} {prefix} {role} {grey}` … You can also **Reset format**. `{role}` is the player's role tag from an addon (Admin.Plugin: the tag of their highest-priority permission group, set in `!admin` › Server › Roles); without `{role}` in the format the tag goes right before the name. A group's name color (same page) replaces the name colors below for its members.
- **Prefix ›**: SurfTimer's chat prefix, typed in chat with color tags, e.g. `[{blue}Surf{bluegrey}Timer{default}]`. `default` goes back to the language file's. A preview is printed to you.
- **Map chooser prefix ›** (one row per addon that registers its prefix): the same for that addon.
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
