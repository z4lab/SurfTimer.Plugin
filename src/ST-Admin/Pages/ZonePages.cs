using System.Globalization;

namespace SurfTimer;

/// <summary>
/// Admin panel - Map - Zones: the map's stored zones, "reload from map" and the live zone editor (spectator
/// free-cam; move / resize / corners / teleport points in steps, add / delete zones; Save / Discard).
/// </summary>
public partial class SurfTimer
{
	private static readonly ZoneType[] EditorZoneTypes =
	[
		ZoneType.MapStart, ZoneType.MapEnd, ZoneType.StageStart, ZoneType.Checkpoint, ZoneType.BonusStart, ZoneType.BonusEnd,
		ZoneType.Stop, ZoneType.TeleportBack, ZoneType.SpeedCap,
	];

	private static string ZoneSize(ZoneDefinition zone)
	{
		var size = zone.Size;
		return $"{size.X:0} × {size.Y:0} × {size.Z:0}";
	}

	private static IEnumerable<ZoneDefinition> OrderedZones(IEnumerable<ZoneDefinition> zones) =>
		zones.OrderBy(z => (byte)z.Type == 0 ? 99 : (byte)z.Type).ThenBy(z => z.Number).ThenBy(z => z.Key);

	// ---- Zones page ----

	private PanelPage AdminZonesPage() => new("Zones", ctx =>
	{
		var map = CurrentMap;
		if (map == null)
			return [PanelContext.Info("No map loaded")];

		var editor = ActiveZoneEditor;
		var rows = new List<HudMenuItem>
		{
			PanelContext.Info("Stored in", map.ZonesFromDatabase ? "database" : "map only", map.ZonesFromDatabase
				? "loaded from the database on map start"
				: "imported from the map - saved once the map is loaded"),
			PanelContext.Info("Stages / bonuses", $"{map.Stages} / {map.Bonuses}", $"{map.TotalCheckpoints} checkpoints"),
		};

		if (map.ID > 0)
		{
			var summary = ctx.Load("summary", () => ZoneRepository.GetSummaryAsync(map.ID));
			if (summary != null && summary.Zones > 0)
			{
				rows.Add(PanelContext.Info("History", summary.EditedAt is DateTime edited ? $"edited {AdminFormat.Date(edited)}" : "not edited",
					summary.ImportedAt is DateTime imported ? $"imported {AdminFormat.Date(imported)}" : ""));
			}
		}

		rows.Add(ctx.Nav("Edit zones", editor == null ? "" : editor.Editor.Controller.PlayerName,
			"spectator free-cam, changes apply live", () =>
			{
				string? error = EnterZoneEditor(ctx.Player);
				return error == null ? ZoneEditorPage() : new PanelPage("Zone editor", _ => [PanelContext.Info(error)]);
			}));

		rows.Add(ctx.Danger("Reload from map", "replace the stored zones with the map's own", () => PanelContext.Confirm("Reload from map",
			_ =>
			[
				PanelContext.Info("Stored zones", map.ZoneDefinitions.Count.ToString(), "are deleted"),
				PanelContext.Info("Map's zones", ZoneImport.FromTriggers().Count.ToString(), "trigger_multiple zones"),
			],
			"Reload zones", c => ReloadZonesFromMap(c, countsChanged => AfterZoneSave(c, countsChanged)))));

		foreach (var zone in OrderedZones(map.ZoneDefinitions))
			rows.Add(PanelContext.Info(zone.Label, ZoneSize(zone), zone.Name));
		return rows;
	});

	/// <summary>After a save / reload: a restart page when the counts changed, else just the status</summary>
	private void AfterZoneSave(PanelContext ctx, bool countsChanged)
	{
		if (countsChanged)
			PanelPush(ctx.Session, ZoneRestartPage());
		else
			PanelRefresh(ctx.Session);
	}

	private PanelPage ZoneRestartPage() => new("Restart map", ctx =>
	[
		PanelContext.Info("Stage / bonus / checkpoint counts changed", "", "new numbered zones work after a map restart"),
		ctx.Act("Restart map now", "", CurrentMap?.Name ?? "", () =>
		{
			EndZoneEditor();
			RestartCurrentMap();
		}, closes: true),
		ctx.Back("Later"),
	]);

	// ---- Editor ----

	/// <summary>The editor of this panel's player - null (and an info page) when they aren't editing</summary>
	private ZoneEditorSession? EditorOf(PanelContext ctx)
	{
		var editor = ActiveZoneEditor;
		return editor != null && ReferenceEquals(editor.Editor, ctx.Player) ? editor : null;
	}

	private static List<HudMenuItem> NotEditing() => [PanelContext.Info("Not in the zone editor", "", "Map › Zones › Edit zones")];

	private PanelPage ZoneEditorPage() => new("Zone editor", ctx =>
	{
		var editor = EditorOf(ctx);
		if (editor == null)
			return NotEditing();

		var rows = new List<HudMenuItem>
		{
			ctx.Act("Step", $"{editor.Step:0} units", "1 / 4 / 16 / 64", () => editor.StepIndex = (editor.StepIndex + 1) % ZoneEditorSession.Steps.Length),
			ctx.Toggle("Aim mode", editor.AimMode, "shoot to draw shapes / set box corners", on =>
			{
				editor.AimMode = on;
				editor.AimCorner = 0;
				editor.ReshootPoint = null;
				if (on && editor.Editor.Controller.PawnIsAlive)
					GivePistolItem(editor.Editor.Controller, DefaultPistol(editor.Editor.Controller.Team));
				if (!on)
				{
					editor.DrawingShape = false;
					editor.ShapePoints.Clear();
					RedrawDraftShape();
				}
				ctx.Session.Status = on ? "Aim mode: New shape, then shoot its points" : "Aim mode off";
			}),
			ctx.Nav("Add zone", "", "a 64³ box where you are - then draw its shape", () => ZoneTypePage((type, number) =>
			{
				var camera = EditorCamera(editor.Editor);
				if (camera == null)
					return "You aren't alive yet - try again";
				var zone = new ZoneDefinition
				{
					Type = type,
					Number = number,
					Source = ZoneSource.Editor,
					Value = type == ZoneType.SpeedCap ? ZoneDefinition.DefaultSpeedCap : null,
				};
				var position = camera.Value.Position;
				zone.SetCorners(new VectorT(position.X - 32, position.Y - 32, position.Z - 32), new VectorT(position.X + 32, position.Y + 32, position.Z + 32));
				editor.Draft.Add(zone);
				SelectZone(zone);
				ZoneDraftChanged(zone);
				PopZonePickers(ctx.Session, $"{zone.Label} added");
				PanelPush(ctx.Session, ZoneEditPage(zone.Key));
				ctx.Session.Status = $"{zone.Label} added";
				return null;
			})),
			ctx.Act("Save", editor.Changes == 0 ? "saved" : $"{editor.Changes} changes", "write the zones to the database", () =>
				SaveZoneDraft(ctx, countsChanged => AfterZoneSave(ctx, countsChanged))),
		};

		if (editor.AimMode)
		{
			if (!editor.DrawingShape)
			{
				rows.Insert(2, ctx.Act("New shape", "", "shoot the floor points in order", () =>
				{
					editor.DrawingShape = true;
					editor.ShapePoints.Clear();
					RedrawDraftShape();
					ctx.Session.Status = "Shoot the shape's points in order - then Finish shape";
				}));
			}
			else
			{
				rows.Insert(2, ctx.Act("Finish shape", $"{editor.ShapePoints.Count} points", editor.Selected?.Label ?? "select the zone first", () =>
					ctx.Session.Status = FinishDrawnShape() ?? "Shape set - use Height + to raise it"));
				rows.Insert(3, ctx.Act("Undo point", "", "", () =>
				{
					if (editor.ShapePoints.Count > 0)
						editor.ShapePoints.RemoveAt(editor.ShapePoints.Count - 1);
					RedrawDraftShape();
				}));
				rows.Insert(4, ctx.Act("Cancel shape", "", "", () =>
				{
					editor.DrawingShape = false;
					editor.ShapePoints.Clear();
					RedrawDraftShape();
				}));
			}
		}

		if (editor.Changes > 0)
		{
			rows.Add(ctx.Act("Discard", "", "back to the saved zones", () =>
			{
				DiscardZoneDraft();
				ctx.Session.Status = "Changes discarded";
			}));
		}

		rows.Add(editor.Changes > 0
			? ctx.Nav("Exit editor", "", "unsaved changes", ZoneExitPage)
			: ctx.Act("Exit editor", "", "back to your team", () =>
			{
				EndZoneEditor();
				PanelBack(ctx.Session, "Left the zone editor");
			}));

		foreach (var zone in OrderedZones(editor.Draft))
		{
			string sub = IsZoneActiveInMap(zone) ? zone.Name : "inactive until a map restart";
			int key = zone.Key;
			rows.Add(ctx.Nav(zone.Label, ZoneSize(zone), sub, () => ZoneEditPage(key)) with
			{
				Style = editor.SelectedKey == key ? HudMenuItemStyle.On : HudMenuItemStyle.Normal,
			});
		}
		return rows;
	});

	private PanelPage ZoneExitPage() => new("Exit editor", ctx =>
	{
		var editor = EditorOf(ctx);
		if (editor == null)
			return NotEditing();

		return
		[
			PanelContext.Info("Unsaved changes", editor.Changes.ToString()),
			ctx.Act("Save and exit", "", "", () => SaveZoneDraft(ctx, countsChanged =>
			{
				EndZoneEditor();
				PanelBack(ctx.Session);
				PanelBack(ctx.Session, "Zones saved");
				if (countsChanged)
					PanelPush(ctx.Session, ZoneRestartPage());
			})),
			ctx.Act("Discard and exit", "", "", () =>
			{
				EndZoneEditor();
				PanelBack(ctx.Session);
				PanelBack(ctx.Session, "Changes discarded");
			}),
			ctx.Back("Keep editing"),
		];
	});

	private const string ZonePickerTitle = "Zone type";
	private const string ZoneNumberTitle = "Number";

	/// <summary>Leaves the type / number pickers (back to the page they were opened from)</summary>
	private static void PopZonePickers(PanelSession session, string status)
	{
		while (session.Stack.Count > 1 && session.Top.Title is ZonePickerTitle or ZoneNumberTitle)
			session.Stack.RemoveAt(session.Stack.Count - 1);
		session.Status = status;
	}

	/// <summary>Picks a zone type, then a number for numbered types. apply returns an error, or null when done.</summary>
	private PanelPage ZoneTypePage(Func<ZoneType, short, string?> apply) => new(ZonePickerTitle, ctx =>
	{
		var editor = EditorOf(ctx);
		if (editor == null)
			return NotEditing();

		return EditorZoneTypes.Select(type =>
		{
			if (!ZoneDefinition.IsNumbered(type))
			{
				short number = type == ZoneType.MapStart ? (short)1 : (short)0;
				return ctx.Act(ZoneDefinition.Describe(type, number), "", "", () =>
				{
					string? error = apply(type, number);
					if (error != null)
						ctx.Session.Status = error;
				});
			}
			return ctx.Nav(ZoneDefinition.Describe(type, 0).Replace(" 0", " N"), "", "pick the number", () => ZoneNumberPage(type, apply));
		}).ToList();
	});

	private PanelPage ZoneNumberPage(ZoneType type, Func<ZoneType, short, string?> apply) => new(ZoneNumberTitle, ctx =>
	{
		var editor = EditorOf(ctx);
		if (editor == null)
			return NotEditing();

		var used = editor.Draft.Where(z => z.Type == type).Select(z => z.Number).ToHashSet();
		short next = (short)(type == ZoneType.StageStart ? 2 : 1);
		while (used.Contains(next))
			next++;

		void Pick(short number)
		{
			string? error = apply(type, number);
			if (error != null)
				ctx.Session.Status = error;
		}

		var rows = new List<HudMenuItem>
		{
			ctx.Act(ZoneDefinition.Describe(type, next), "", "next free number", () => Pick(next)),
			ctx.Ask("Other number", "", "typed in chat", "Type the number in chat (1-99)", text =>
			{
				if (!short.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out short number) || number < 1 || number > 99)
					return "A number from 1 to 99";
				Pick(number);
				return null;
			}),
		};
		foreach (short number in used.OrderBy(n => n))
			rows.Add(ctx.Act(ZoneDefinition.Describe(type, number), "exists", "another box of this zone", () => Pick(number)));
		return rows;
	});

	private PanelPage ZoneEditPage(int key) => new("Zone", ctx =>
	{
		var editor = EditorOf(ctx);
		if (editor == null)
			return NotEditing();

		var zone = editor.Draft.FirstOrDefault(z => z.Key == key);
		if (zone == null)
			return [PanelContext.Info("This zone was removed")];
		if (editor.SelectedKey != key)
			SelectZone(zone);

		float step = editor.Step;
		HudMenuItem Edit(string text, string sub, Action<ZoneDefinition> change) => ctx.Act(text, "", sub, () =>
		{
			change(zone);
			ZoneDraftChanged(zone);
		});

		var center = zone.Center;
		string shape = zone.Shape switch
		{
			ZoneShape.Prism => $"polygon · {zone.Points.Count} points",
			ZoneShape.Trigger => "map trigger (exact shape)",
			_ => "box",
		};
		var rows = new List<HudMenuItem>
		{
			PanelContext.Info(zone.Label, ZoneSize(zone), $"{shape} · center {center.X:0} {center.Y:0} {center.Z:0}{(IsZoneActiveInMap(zone) ? "" : " · inactive until a map restart")}"),
			ctx.Act("Step", $"{step:0} units", "", () => editor.StepIndex = (editor.StepIndex + 1) % ZoneEditorSession.Steps.Length),
			ctx.Act("Teleport here", "", "", () => MoveEditorCamera(editor.Editor, zone)),
		};

		if (zone.Shape == ZoneShape.Trigger)
			rows.Add(PanelContext.Info("Linked to map trigger", zone.TriggerName ?? "", "editing its shape turns it into a polygon zone"));

		rows.AddRange(
		[
			Edit("Move +X", "east", z => Move(z, step, 0, 0)),
			Edit("Move -X", "west", z => Move(z, -step, 0, 0)),
			Edit("Move +Y", "north", z => Move(z, 0, step, 0)),
			Edit("Move -Y", "south", z => Move(z, 0, -step, 0)),
			Edit("Move up", "+Z", z => Move(z, 0, 0, step)),
			Edit("Move down", "-Z", z => Move(z, 0, 0, -step)),
		]);

		if (zone.Shape == ZoneShape.Box)
		{
			rows.AddRange(
			[
				Edit("Width +", "X, both sides", z => Grow(z, step, 0, 0, centered: true)),
				Edit("Width -", "X, both sides", z => Grow(z, -step, 0, 0, centered: true)),
				Edit("Length +", "Y, both sides", z => Grow(z, 0, step, 0, centered: true)),
				Edit("Length -", "Y, both sides", z => Grow(z, 0, -step, 0, centered: true)),
				Edit("Height +", "top, the floor stays", z => Grow(z, 0, 0, step, centered: false)),
				Edit("Height -", "top, the floor stays", z => Grow(z, 0, 0, -step, centered: false)),
				ctx.Nav("Faces", "", "grow / shrink one side", () => ZoneFacesPage(key)),
				ctx.Act("Set corner 1 here", editor.Corner1 != null ? "set" : "", editor.AimMode ? "or shoot it (aim mode)" : "your position", () =>
				{
					var camera = EditorCamera(editor.Editor);
					if (camera == null)
						return;
					var point = camera.Value.Position;
					editor.Corner1 = point;
					zone.SetCorners(point, FarthestCorner(zone, point));
					ZoneDraftChanged(zone);
				}),
				ctx.Act("Set corner 2 here", "", "spans the zone from corner 1", () =>
				{
					var camera = EditorCamera(editor.Editor);
					if (camera == null)
						return;
					var point = camera.Value.Position;
					zone.SetCorners(editor.Corner1 ?? FarthestCorner(zone, point), point);
					ZoneDraftChanged(zone);
				}),
			]);
		}
		else
		{
			rows.Add(Edit("Height +", "straight up from every point", z => { z.ToPrism(); z.Height += step; z.UpdateBounds(); }));
			rows.Add(Edit("Height -", "straight up from every point", z => { z.ToPrism(); z.Height = MathF.Max(MinZoneSize, z.Height - step); z.UpdateBounds(); }));
		}

		rows.Add(ctx.Nav("Points", $"{zone.Footprint.Count}", zone.Shape == ZoneShape.Prism ? "move / insert / delete / re-shoot" : "editing makes it a polygon", () => ZonePointsPage(key)));
		rows.AddRange(
		[
			ctx.Act("Set teleport here", zone.Teleport != null ? "set" : "center", "your position and view", () =>
			{
				var camera = EditorCamera(editor.Editor);
				if (camera == null)
					return;
				zone.Teleport = camera.Value.Position;
				zone.TeleportAngles = camera.Value.Angles;
				ZoneDraftChanged(zone);
			}),
		]);

		if (zone.Teleport != null)
		{
			rows.Add(Edit("Clear teleport", "use the zone's bottom center", z =>
			{
				z.Teleport = null;
				z.TeleportAngles = null;
			}));
		}

		if (zone.Type == ZoneType.SpeedCap)
		{
			float cap = zone.Value ?? ZoneDefinition.DefaultSpeedCap;
			rows.Add(PanelContext.Info("Speed cap", $"{cap:0} u/s", "horizontal speed inside"));
			rows.Add(Edit("Cap +50", "", z => z.Value = cap + 50));
			rows.Add(Edit("Cap -50", "", z => z.Value = MathF.Max(0, cap - 50)));
			rows.Add(Edit("Cap +10", "", z => z.Value = cap + 10));
			rows.Add(Edit("Cap -10", "", z => z.Value = MathF.Max(0, cap - 10)));
			rows.Add(ctx.Ask("Cap", $"{cap:0}", "typed in chat", "Type the speed cap in chat (u/s)", text =>
			{
				if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < 0 || value > 10000)
					return "A speed from 0 to 10000";
				zone.Value = value;
				ZoneDraftChanged(zone);
				return null;
			}));
		}

		rows.Add(ctx.Nav("Type / number", zone.Label, "what this zone is", () => ZoneTypePage((type, number) =>
		{
			zone.Type = type;
			zone.Number = number;
			zone.Source = ZoneSource.Editor;
			if (type == ZoneType.SpeedCap)
				zone.Value ??= ZoneDefinition.DefaultSpeedCap;
			ZoneDraftChanged(zone);
			PopZonePickers(ctx.Session, $"Now {zone.Label}");
			PanelRefresh(ctx.Session, resetPage: true);
			return null;
		})));
		rows.Add(ctx.Act("Duplicate", "", "a copy in the same place", () =>
		{
			var copy = zone.Duplicate();
			editor.Draft.Add(copy);
			SelectZone(copy);
			ZoneDraftChanged(copy);
			PanelBack(ctx.Session, "Duplicated");
			PanelPush(ctx.Session, ZoneEditPage(copy.Key));
		}));
		rows.Add(ctx.Danger("Delete zone", zone.Label, () => PanelContext.Confirm("Delete zone",
			_ => [PanelContext.Info(zone.Label, ZoneSize(zone), zone.Name)],
			"Delete", c =>
			{
				editor.Draft.Remove(zone);
				RemoveEditorOutline(zone.Key);
				if (editor.SelectedKey == zone.Key)
					editor.SelectedKey = null;
				ZoneDraftChanged(null);
				PanelBack(c.Session, $"{zone.Label} deleted");
			})));
		return rows;
	});

	/// <summary>A zone's footprint points - editing any of them makes the zone a polygon (prism)</summary>
	private PanelPage ZonePointsPage(int key) => new("Points", ctx =>
	{
		var editor = EditorOf(ctx);
		var zone = editor?.Draft.FirstOrDefault(z => z.Key == key);
		if (editor == null || zone == null)
			return NotEditing();

		var points = zone.Footprint;
		var rows = new List<HudMenuItem>();
		for (int i = 0; i < points.Count; i++)
		{
			int index = i;
			var p = points[i];
			rows.Add(ctx.Nav($"Point {i + 1}", $"{p.X:0} {p.Y:0} {p.Z:0}", "", () => ZonePointPage(key, index)));
		}
		rows.Add(ctx.Act("Add point here", "", "your position, after the last point", () =>
		{
			var camera = EditorCamera(editor.Editor);
			if (camera == null)
				return;
			zone.ToPrism();
			zone.Points.Add(camera.Value.Position);
			zone.UpdateBounds();
			ZoneDraftChanged(zone);
		}));
		return rows;
	});

	private PanelPage ZonePointPage(int key, int index) => new($"Point {index + 1}", ctx =>
	{
		var editor = EditorOf(ctx);
		var zone = editor?.Draft.FirstOrDefault(z => z.Key == key);
		if (editor == null || zone == null)
			return NotEditing();
		if (index >= zone.Footprint.Count)
			return [PanelContext.Info("This point was removed")];

		float step = editor.Step;
		HudMenuItem Change(string text, string sub, Action<List<VectorT>> change) => ctx.Act(text, "", sub, () =>
		{
			zone.ToPrism();
			if (index >= zone.Points.Count)
				return;
			change(zone.Points);
			zone.UpdateBounds();
			ZoneDraftChanged(zone);
		});
		HudMenuItem Nudge(string text, float x, float y, float z) =>
			Change(text, $"{step:0} units", points => points[index] += new VectorT(x, y, z));

		var p = zone.Footprint[index];
		var rows = new List<HudMenuItem>
		{
			PanelContext.Info($"Point {index + 1} of {zone.Footprint.Count}", $"{p.X:0} {p.Y:0} {p.Z:0}"),
			ctx.Act("Step", $"{step:0} units", "", () => editor.StepIndex = (editor.StepIndex + 1) % ZoneEditorSession.Steps.Length),
			Nudge("+X", step, 0, 0), Nudge("-X", -step, 0, 0),
			Nudge("+Y", 0, step, 0), Nudge("-Y", 0, -step, 0),
			Nudge("Up", 0, 0, step), Nudge("Down", 0, 0, -step),
			ctx.Act("Set to my position", "", "", () =>
			{
				var camera = EditorCamera(editor.Editor);
				if (camera == null)
					return;
				zone.ToPrism();
				zone.Points[index] = camera.Value.Position;
				zone.UpdateBounds();
				ZoneDraftChanged(zone);
			}),
			ctx.Act("Re-shoot", editor.ReshootPoint == index ? "waiting" : "", "the next shot (aim mode) moves this point", () =>
			{
				editor.AimMode = true;
				editor.DrawingShape = false;
				editor.ReshootPoint = index;
				if (editor.Editor.Controller.PawnIsAlive)
					GivePistolItem(editor.Editor.Controller, DefaultPistol(editor.Editor.Controller.Team));
				ctx.Session.Status = $"Shoot where point {index + 1} should be";
			}),
			Change("Insert point after", "halfway to the next point", points =>
			{
				var a = points[index];
				var b = points[(index + 1) % points.Count];
				points.Insert(index + 1, new VectorT((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2));
			}),
		};
		if (zone.Footprint.Count > 3)
		{
			rows.Add(ctx.Act("Delete point", "", "at least 3 stay", () =>
			{
				zone.ToPrism();
				zone.Points.RemoveAt(index);
				zone.UpdateBounds();
				ZoneDraftChanged(zone);
				PanelBack(ctx.Session, $"Point {index + 1} deleted");
			}));
		}
		return rows;
	});

	private PanelPage ZoneFacesPage(int key) => new("Faces", ctx =>
	{
		var editor = EditorOf(ctx);
		var zone = editor?.Draft.FirstOrDefault(z => z.Key == key);
		if (editor == null || zone == null)
			return NotEditing();

		float step = editor.Step;
		HudMenuItem Face(string text, int axis, bool max, float amount) => ctx.Act(text, "", $"{(amount > 0 ? "+" : "-")}{step:0}", () =>
		{
			MoveFace(zone, axis, max, amount);
			ZoneDraftChanged(zone);
		});

		return
		[
			ctx.Act("Step", $"{step:0} units", "", () => editor.StepIndex = (editor.StepIndex + 1) % ZoneEditorSession.Steps.Length),
			Face("East (+X) out", 0, true, step),
			Face("East (+X) in", 0, true, -step),
			Face("West (-X) out", 0, false, step),
			Face("West (-X) in", 0, false, -step),
			Face("North (+Y) out", 1, true, step),
			Face("North (+Y) in", 1, true, -step),
			Face("South (-Y) out", 1, false, step),
			Face("South (-Y) in", 1, false, -step),
			Face("Top out", 2, true, step),
			Face("Top in", 2, true, -step),
			Face("Bottom out", 2, false, step),
			Face("Bottom in", 2, false, -step),
		];
	});

	// ---- Box math ----

	private const float MinZoneSize = 1f;

	private static void Move(ZoneDefinition zone, float x, float y, float z)
	{
		if (zone.Shape == ZoneShape.Trigger)
			zone.ToPrism(); // A moved trigger zone isn't the map trigger any more
		zone.Translate(new VectorT(x, y, z));
		zone.Source = ZoneSource.Editor;
	}

	/// <summary>Grows (negative: shrinks) the box - centered on X / Y, or upward only</summary>
	private static void Grow(ZoneDefinition zone, float x, float y, float z, bool centered)
	{
		var size = zone.Size;
		x = MathF.Max(x, MinZoneSize - size.X);
		y = MathF.Max(y, MinZoneSize - size.Y);
		z = MathF.Max(z, MinZoneSize - size.Z);

		if (centered)
		{
			zone.Mins -= new VectorT(x / 2, y / 2, z / 2);
			zone.Maxs += new VectorT(x / 2, y / 2, z / 2);
		}
		else
		{
			zone.Maxs += new VectorT(x, y, z);
		}
		zone.Source = ZoneSource.Editor;
	}

	/// <summary>Moves one face outward (positive) or inward - never past the opposite face</summary>
	private static void MoveFace(ZoneDefinition zone, int axis, bool max, float amount)
	{
		var mins = zone.Mins;
		var maxs = zone.Maxs;
		if (max)
			maxs[axis] = MathF.Max(maxs[axis] + amount, mins[axis] + MinZoneSize);
		else
			mins[axis] = MathF.Min(mins[axis] - amount, maxs[axis] - MinZoneSize);
		zone.Mins = mins;
		zone.Maxs = maxs;
		zone.Source = ZoneSource.Editor;
	}

	/// <summary>The corner of the box farthest from a point - "set corner" keeps it</summary>
	private static VectorT FarthestCorner(ZoneDefinition zone, VectorT point) => new(
		MathF.Abs(point.X - zone.Mins.X) > MathF.Abs(point.X - zone.Maxs.X) ? zone.Mins.X : zone.Maxs.X,
		MathF.Abs(point.Y - zone.Mins.Y) > MathF.Abs(point.Y - zone.Maxs.Y) ? zone.Mins.Y : zone.Maxs.Y,
		MathF.Abs(point.Z - zone.Mins.Z) > MathF.Abs(point.Z - zone.Maxs.Z) ? zone.Mins.Z : zone.Maxs.Z);
}
