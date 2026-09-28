using System.Globalization;
using System.Text.RegularExpressions;

namespace SurfTimer;

/// <summary>
/// Admin panel - Map tab: ranked, tiers, stages / bonuses, authors, workshop id, map settings, zones and
/// resetting records. Replaces !amt / !amn / !amr / !mapsetting / !triggers.
/// </summary>
public partial class SurfTimer
{
	private static readonly string[] TierNames = ["", "beginner", "easy", "medium", "hard", "very hard", "extreme", "death", "impossible"];

	private AdminPage AdminMapRoot() => new("Map", ctx =>
	{
		var map = CurrentMap;
		if (map == null || map.ID <= 0)
			return [AdminContext.Info("No map loaded")];

		string layout = map.Stages > 0 ? "staged" : map.TotalCheckpoints > 0 ? $"linear · {map.TotalCheckpoints} checkpoints" : "linear";
		var rows = new List<HudMenuItem>
		{
			AdminContext.Info(map.Name!, AdminFormat.Tier((byte)map.Tier), $"{map.Stages} stages · {map.Bonuses} bonuses · {layout}"),
			ctx.Toggle("Ranked", map.Ranked, "gives points", ranked => AdminSetRanked(ctx, ranked)),
			ctx.Nav("Map tier", AdminFormat.Tier((byte)map.Tier), "", () => AdminTierPage(CourseKind.Map, 0)),
		};

		if (map.Stages > 0)
			rows.Add(ctx.Nav("Stages", map.Stages.ToString(), "tiers, names, points", () => AdminCourseListPage(CourseKind.Stage)));
		if (map.Bonuses > 0)
			rows.Add(ctx.Nav("Bonuses", map.Bonuses.ToString(), "tiers, names, points", () => AdminCourseListPage(CourseKind.Bonus)));

		rows.Add(ctx.Nav("Authors", map.Author ?? "none", "", AdminAuthorsPage));
		rows.Add(ctx.Ask("Workshop id", map.WorkshopId?.ToString() ?? "none", "type in chat",
			LocalizationService.LocalizerNonNull["prompt_workshop_id"], text => AdminSetWorkshopId(ctx, text)));
		rows.Add(ctx.Nav("Map settings", $"{map.Settings.Count} set", "", AdminMapSettingsPage));
		rows.Add(ctx.Nav("Zones", map.Zones.Values.Sum(z => z.Count).ToString(), "triggers", AdminZonesPage));
		rows.Add(ctx.Danger("Reset map records", "times, replays, history", () =>
			AdminWipeConfirm("Reset map records", new TimeRepository.WipeScope(MapId: map.ID), $"map {map.Name}")));
		return rows;
	});

	private void AdminSetRanked(AdminContext ctx, bool ranked)
	{
		var map = CurrentMap;
		map.Ranked = ranked;
		int mapId = map.ID;
		ctx.Audit("ranked", "map", mapId, $"{map.Name}: {(ranked ? "ranked" : "unranked")}");
		ctx.Run(ranked ? "Ranked - recalculating points…" : "Unranked - recalculating points…", async () =>
		{
			await MapRepository.SetRankedAsync(mapId, ranked);
			foreach (int style in Config.Styles)
				await PointsService.RecalculateMapAsync(mapId, style);
			return ranked ? "Map is ranked, points recalculated" : "Map is unranked, points removed";
		});
	}

	private static string CourseLabel(CourseKind kind, short number) => kind switch
	{
		CourseKind.Map => "Map",
		CourseKind.Stage => $"Stage {number}",
		CourseKind.Bonus => $"Bonus {number}",
		CourseKind.Checkpoint => $"Checkpoint {number}",
		_ => $"Course {number}",
	};

	// ---- Tiers ----

	private AdminPage AdminTierPage(CourseKind kind, short number) => new($"{CourseLabel(kind, number)} tier", ctx =>
	{
		var course = CurrentMap.Course(kind, number);
		if (course == null)
			return [AdminContext.Info("Course not found")];

		var rows = new List<HudMenuItem>();
		for (byte tier = 1; tier <= 8; tier++)
		{
			byte value = tier;
			rows.Add(ctx.Act($"Tier {tier}", course.Tier == tier ? "current" : "", TierNames[tier], () => AdminSetTier(ctx, kind, number, value)));
		}
		rows.Add(ctx.Act(kind == CourseKind.Map ? "No tier" : "Use map tier", course.Tier == null ? "current" : "", "",
			() => AdminSetTier(ctx, kind, number, null)));
		return rows;
	});

	private void AdminSetTier(AdminContext ctx, CourseKind kind, short number, byte? tier)
	{
		var map = CurrentMap;
		var course = map.Course(kind, number);
		if (course == null)
			return;

		map.SetCourseTier(kind, number, tier);
		int courseId = course.Id;
		int mapId = map.ID;
		string label = CourseLabel(kind, number);
		ctx.Audit("tier", "course", courseId, $"{map.Name} {label}: {AdminFormat.Tier(tier)}");
		ctx.Run($"{label} tier set to {AdminFormat.Tier(tier)} - recalculating points…", async () =>
		{
			await MapRepository.SetCourseTierAsync(courseId, tier);
			foreach (int style in Config.Styles)
				await PointsService.RecalculateMapAsync(mapId, style);
			return $"{label} tier set to {AdminFormat.Tier(tier)}";
		}, status => AdminBackFrom(ctx.Session, ctx.Page, status));
	}

	// ---- Stages / bonuses ----

	private AdminPage AdminCourseListPage(CourseKind kind) => new(kind == CourseKind.Stage ? "Stages" : "Bonuses", ctx =>
		CurrentMap.Courses.Where(c => c.Kind == kind).OrderBy(c => c.Number).Select(c =>
		{
			string sub = string.Join(" · ", new[] { c.Name, c.PointsEnabled ? null : "points off" }.Where(s => !string.IsNullOrEmpty(s)));
			return ctx.Nav(CourseLabel(kind, c.Number), c.Tier is > 0 ? $"T{c.Tier}" : "map tier", sub, () => AdminCoursePage(kind, c.Number));
		}).ToList());

	private AdminPage AdminCoursePage(CourseKind kind, short number) => new(CourseLabel(kind, number), ctx =>
	{
		var map = CurrentMap;
		var course = map.Course(kind, number);
		if (course == null)
			return [AdminContext.Info("Course not found")];

		string label = CourseLabel(kind, number);
		return
		[
			AdminContext.Info(label, course.Tier is > 0 ? $"T{course.Tier}" : $"map tier ({AdminFormat.Tier((byte)map.Tier)})", course.Name ?? ""),
			ctx.Nav("Tier", course.Tier is > 0 ? $"T{course.Tier}" : "map tier", "overrides the map tier", () => AdminTierPage(kind, number)),
			ctx.Ask("Name", course.Name ?? "none", "type in chat", LocalizationService.LocalizerNonNull["prompt_course_name"], text =>
			{
				string? name = text == "-" ? null : text;
				if (name != null && (name.Length > 64 || !Regex.IsMatch(name, @"^[\w\s\-\.'!?&()]+$")))
					return "Up to 64 letters, digits, spaces and - . ' ! ? & ( )";

				course.Name = name;
				int courseId = course.Id;
				_ = Task.Run(() => MapRepository.SetCourseNameAsync(courseId, name));
				ctx.Audit("course name", "course", courseId, $"{map.Name} {label}: {name ?? "(none)"}");
				ctx.Session.Status = name == null ? $"{label} name removed" : $"{label} is now \"{name}\"";
				return null;
			}),
			ctx.Toggle("Gives points", course.PointsEnabled, "leave out e.g. a broken bonus", enabled =>
			{
				course.PointsEnabled = enabled;
				int courseId = course.Id;
				int mapId = map.ID;
				ctx.Audit("course points", "course", courseId, $"{map.Name} {label}: {(enabled ? "on" : "off")}");
				ctx.Run("Recalculating points…", async () =>
				{
					await MapRepository.SetCoursePointsEnabledAsync(courseId, enabled);
					foreach (int style in Config.Styles)
						await PointsService.RecalculateMapAsync(mapId, style);
					return $"{label} {(enabled ? "gives points again" : "gives no points")}";
				});
			}),
			ctx.Danger($"Reset {label.ToLowerInvariant()} records", "times, replays, history", () =>
				AdminWipeConfirm($"Reset {label.ToLowerInvariant()}", new TimeRepository.WipeScope(CourseId: course.Id), $"{map.Name} {label}")),
		];
	});

	// ---- Authors / workshop id ----

	private AdminPage AdminAuthorsPage() => new("Authors", ctx =>
	{
		var map = CurrentMap;
		var authors = (map.Author ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries);
		var rows = authors.Select((a, i) => AdminContext.Info($"{i + 1}", a)).ToList();
		rows.Add(ctx.Ask("Set authors", "", "comma separated", LocalizationService.LocalizerNonNull["prompt_authors"], text =>
		{
			var list = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
			if (list.Count == 0 || list.Count > 16 || list.Any(a => a.Length > 64 || !Regex.IsMatch(a, @"^[\w\s\-\.]+$")))
				return "Up to 16 names of letters, digits, spaces, - and . (max 64 each)";

			AdminSetAuthors(ctx, list);
			return null;
		}));
		if (authors.Length > 0)
			rows.Add(ctx.Act("Remove authors", "", "", () => AdminSetAuthors(ctx, [])));
		return rows;
	});

	private void AdminSetAuthors(AdminContext ctx, List<string> authors)
	{
		var map = CurrentMap;
		map.Author = authors.Count > 0 ? string.Join(", ", authors) : null;
		int mapId = map.ID;
		_ = Task.Run(() => MapRepository.SetAuthorsAsync(mapId, authors));
		ctx.Audit("authors", "map", mapId, $"{map.Name}: {map.Author ?? "(none)"}");
		ctx.Session.Status = authors.Count > 0 ? $"Authors: {map.Author}" : "Authors removed";
	}

	private string? AdminSetWorkshopId(AdminContext ctx, string text)
	{
		var map = CurrentMap;
		ulong? id = null;
		if (text != "-")
		{
			if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) || parsed == 0)
				return "A workshop id is a number (\"-\" removes it)";
			id = parsed;
		}

		map.WorkshopId = id;
		int mapId = map.ID;
		_ = Task.Run(() => MapRepository.SetWorkshopIdAsync(mapId, id));
		ctx.Audit("workshop id", "map", mapId, $"{map.Name}: {id?.ToString() ?? "(none)"}");
		ctx.Session.Status = id == null ? "Workshop id removed" : $"Workshop id set to {id}";
		return null;
	}

	// ---- Map settings ----

	private static readonly HashSet<string> KnownMapSettings = new(StringComparer.OrdinalIgnoreCase)
	{
		Map.SettingStagedLinear, Map.SettingStartSpeedCap, Map.SettingReplays,
	};

	private static bool IsCustomMapSetting(string key) =>
		!KnownMapSettings.Contains(key) && !key.StartsWith(MapCvars.SettingPrefix, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Stores (value) or removes (null) a map setting and applies the map's settings again.
	/// </summary>
	private void AdminSetMapSetting(AdminContext ctx, string key, string? value)
	{
		var map = CurrentMap;
		int mapId = map.ID;
		if (value == null)
		{
			map.Settings.Remove(key);
			_ = Task.Run(() => MapRepository.DeleteSettingAsync(mapId, key));
		}
		else
		{
			map.Settings[key] = value;
			_ = Task.Run(() => MapRepository.SetSettingAsync(mapId, key, value));
		}
		map.ApplySettings();
		ctx.Audit("map setting", "map", mapId, $"{map.Name}: {key} = {value ?? "(removed)"}");
	}

	private AdminPage AdminMapSettingsPage() => new("Map settings", ctx =>
	{
		var map = CurrentMap;
		string cap = map.StartSpeedCap is float own
			? own <= 0 ? "no cap" : $"{own:0} u/s"
			: $"default {Config.StartSpeedCap}";
		int cvars = map.Settings.Keys.Count(k => k.StartsWith(MapCvars.SettingPrefix, StringComparison.OrdinalIgnoreCase));
		int custom = map.Settings.Keys.Count(IsCustomMapSetting);

		return
		[
			ctx.Toggle("Staged linear", map.StagedLinear, "checkpoints count as stages", on =>
			{
				AdminSetMapSetting(ctx, Map.SettingStagedLinear, on ? "1" : null);
				ctx.Session.Status = on ? "Staged linear on (applies to new runs)" : "Staged linear off";
			}),
			ctx.Nav("Start speed cap", cap, "bhop cap in start zones", AdminSpeedCapPage),
			ctx.Toggle("Record replays", map.RecordReplays, "store replays of new PBs", on =>
			{
				AdminSetMapSetting(ctx, Map.SettingReplays, on ? null : "0");
				ctx.Session.Status = on ? "Replays are recorded again" : "No replays are stored on this map";
			}),
			ctx.Nav("Cvars", cvars == 0 ? "none" : $"{cvars} set", "movement cvars for this map", AdminCvarsPage),
			ctx.Nav("Custom keys", custom.ToString(), "free key / value notes", AdminCustomKeysPage),
		];
	});

	private AdminPage AdminSpeedCapPage() => new("Start speed cap", ctx =>
	{
		var map = CurrentMap;
		float current = map.StartSpeedCap ?? Config.StartSpeedCap;
		string source = map.StartSpeedCap == null ? "timer_settings.json default" : "this map";

		void Set(float value)
		{
			value = Math.Clamp(value, 0, 10000);
			AdminSetMapSetting(ctx, Map.SettingStartSpeedCap, value.ToString("0", CultureInfo.InvariantCulture));
			ctx.Session.Status = value <= 0 ? "No start speed cap on this map" : $"Start speed cap {value:0} u/s";
		}

		return
		[
			AdminContext.Info("Current", current <= 0 ? "no cap" : $"{current:0} u/s", source),
			ctx.Act("+10", "", "", () => Set(current + 10)),
			ctx.Act("-10", "", "", () => Set(current - 10)),
			ctx.Act("+50", "", "", () => Set(current + 50)),
			ctx.Act("-50", "", "", () => Set(current - 50)),
			ctx.Act("No cap", "", "bhop freely in the start zone", () => Set(0)),
			ctx.Act("Use default", $"{Config.StartSpeedCap}", "timer_settings.json", () =>
			{
				AdminSetMapSetting(ctx, Map.SettingStartSpeedCap, null);
				ctx.Session.Status = $"Start speed cap follows the default ({Config.StartSpeedCap})";
			}),
		];
	});

	private AdminPage AdminCvarsPage() => new("Cvars", ctx =>
		MapCvars.Whitelist.Select(name =>
		{
			bool overridden = CurrentMap.Settings.ContainsKey(MapCvars.SettingKey(name));
			string sub = overridden ? $"override · server {MapCvars.Default(name)}" : "server value";
			return ctx.Nav(name, MapCvars.Current(name), sub, () => AdminCvarPage(name));
		}).ToList());

	private AdminPage AdminCvarPage(string name) => new(name, ctx =>
	{
		string key = MapCvars.SettingKey(name);
		bool overridden = CurrentMap.Settings.TryGetValue(key, out var value);
		var rows = new List<HudMenuItem>
		{
			AdminContext.Info("Current", MapCvars.Current(name)),
			AdminContext.Info("Server value", MapCvars.Default(name), "restored on map end"),
			ctx.Ask("Set for this map", overridden ? value! : "", "type in chat", LocalizationService.LocalizerNonNull["prompt_cvar", name], text =>
			{
				if (!MapCvars.IsValidValue(text))
					return "A number, e.g. 150 or 0.5";
				AdminSetMapSetting(ctx, key, text);
				ctx.Session.Status = $"{name} {text} on this map";
				return null;
			}),
		};
		if (overridden)
		{
			rows.Add(ctx.Act("Remove override", "", "back to the server value", () =>
			{
				AdminSetMapSetting(ctx, key, null);
				ctx.Session.Status = $"{name} is back to the server value";
			}));
		}
		return rows;
	});

	private AdminPage AdminCustomKeysPage() => new("Custom keys", ctx =>
	{
		var rows = CurrentMap.Settings.Where(s => IsCustomMapSetting(s.Key)).OrderBy(s => s.Key)
			.Select(s => ctx.Nav(s.Key, s.Value, "", () => AdminCustomKeyPage(s.Key))).ToList();
		rows.Add(ctx.Ask("Add key", "", "\"key value\" in chat", LocalizationService.LocalizerNonNull["prompt_custom_key"], text =>
		{
			var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 2 || !Regex.IsMatch(parts[0], "^[a-z0-9_]{1,64}$") || parts[1].Length > 255)
				return "Type a key (a-z, 0-9, _) and a value, e.g. note cp 4 is a skip";
			if (!IsCustomMapSetting(parts[0]))
				return "That key is set on the other pages";

			AdminSetMapSetting(ctx, parts[0], parts[1]);
			ctx.Session.Status = $"{parts[0]} = {parts[1]}";
			return null;
		}));
		return rows;
	});

	private AdminPage AdminCustomKeyPage(string key) => new(key, ctx =>
	{
		if (!CurrentMap.Settings.TryGetValue(key, out var value))
			return [AdminContext.Info("Removed")];

		return
		[
			AdminContext.Info(key, value),
			ctx.Ask("Change value", "", "type in chat", LocalizationService.LocalizerNonNull["prompt_setting_value", key], text =>
			{
				if (text.Length > 255)
					return "Up to 255 characters";
				AdminSetMapSetting(ctx, key, text);
				ctx.Session.Status = $"{key} = {text}";
				return null;
			}),
			ctx.Act("Remove", "", "", () =>
			{
				AdminSetMapSetting(ctx, key, null);
				AdminBack(ctx.Session, $"{key} removed");
			}),
		];
	});

	// ---- Zones ----

	private AdminPage AdminZonesPage() => new("Zones", ctx =>
	{
		var map = CurrentMap;
		var rows = new List<HudMenuItem>
		{
			AdminContext.Info("Layout", map.Stages > 0 ? "staged" : "linear", map.StagedLinear ? "staged linear" : ""),
			AdminContext.Info("Stages / bonuses", $"{map.Stages} / {map.Bonuses}", $"{map.TotalCheckpoints} checkpoints"),
		};

		rows.AddRange(map.Zones.OrderBy(z => z.Key.Type).ThenBy(z => z.Key.Number).Select(z =>
		{
			string label = z.Key.Type switch
			{
				ZoneType.MapStart => "Map start",
				ZoneType.MapEnd => "Map end",
				ZoneType.StageStart => $"Stage {z.Key.Number} start",
				ZoneType.Checkpoint => $"Checkpoint {z.Key.Number}",
				ZoneType.BonusStart => $"Bonus {z.Key.Number} start",
				ZoneType.BonusEnd => $"Bonus {z.Key.Number} end",
				_ => z.Value.FirstOrDefault()?.Name ?? "Zone",
			};
			string names = string.Join(", ", z.Value.Select(t => t.Name).Distinct());
			return AdminContext.Info(label, z.Value.Count == 1 ? "1 trigger" : $"{z.Value.Count} triggers", names);
		}));
		return rows;
	});

	// ---- Wiping records (map / course / time / player) ----

	/// <summary>
	/// Confirmation page for deleting times: counts, then the wipe, points and reloads.
	/// </summary>
	private AdminPage AdminWipeConfirm(string title, TimeRepository.WipeScope scope, string what) => AdminContext.Confirm(title, ctx =>
	{
		var preview = ctx.Load("preview", () => TimeRepository.PreviewWipeAsync(scope));
		if (preview == null)
			return [AdminContext.LoadingRow()];

		var rows = new List<HudMenuItem>
		{
			AdminContext.Info("Deletes", "", what),
			AdminContext.Info("Times", AdminFormat.Number(preview.Times), $"{AdminFormat.Number(preview.Players)} players"),
			AdminContext.Info("Replays", AdminFormat.Number(preview.Replays), AdminFormat.Bytes(preview.ReplayBytes)),
		};
		if (scope.IncludesHistory)
			rows.Add(AdminContext.Info("Run history", AdminFormat.Number(preview.History)));
		rows.Add(AdminContext.Info("Points", "recalculated"));
		return rows;
	}, "Confirm", ctx =>
	{
		ctx.Audit(scope.TimeId != null ? "delete time" : "wipe", "times", scope.TimeId ?? scope.CourseId ?? scope.PlayerId ?? scope.MapId, what);
		ctx.Run("Deleting…", async () =>
		{
			var affected = await TimeRepository.WipeAsync(scope);
			await AdminAfterTimesChangedAsync(affected);
			return $"Deleted {AdminFormat.Number(affected.Times)} time(s) - {what}";
		}, status =>
		{
			// Lists showing deleted times load again
			foreach (var page in ctx.Session.Stack)
				page.State.Clear();
			if (scope.TimeId != null)
				AdminBackFrom(ctx.Session, ctx.Page, status); // The time's own page is gone
			else
				ctx.Done(status);
		});
	});
}
