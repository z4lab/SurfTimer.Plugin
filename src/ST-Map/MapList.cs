using System.Text;
using CounterStrikeSharp.API;
using Microsoft.Extensions.Logging;

namespace SurfTimer;

/// <summary>
/// The maps this server can load, read from the workshop addons it downloaded
/// (steamapps/workshop/content/730/&lt;id&gt;/*.vpk - with host_workshop_collection that is the collection).
/// Every map addon carries its map as maps/&lt;name&gt;.vpk, so the addon's VPK directory tree gives the exact map
/// name together with its workshop id - nothing is unpacked. The console output of ds_workshop_listmaps can't
/// be read by plugins, hence the files. Without a readable folder the list stays empty and nothing checks
/// against it. Main thread only (the scan itself runs in the background).
/// </summary>
public partial class SurfTimer
{
	private const uint VpkSignature = 0x55AA1234;
	private const int VpkMaxTreeSize = 64 * 1024 * 1024;

	private List<string> _mapList = new();
	private Dictionary<string, ulong> _mapWorkshopIds = new(StringComparer.OrdinalIgnoreCase);
	private bool _mapListRefreshing;
	private bool _mapListWarned;
	private int _unreadableVpks;
	private string? _lastUnreadableVpk;

	/// <summary>Maps of the server's workshop addons, sorted - empty when unknown</summary>
	internal IReadOnlyList<string> MapList => _mapList;
	internal DateTime? MapListUpdatedAt { get; private set; }

	internal bool IsOnMapList(string name) => _mapWorkshopIds.ContainsKey(name);

	/// <summary>The workshop item a listed map comes from - null when it isn't on the list</summary>
	internal ulong? WorkshopIdOf(string name) => _mapWorkshopIds.TryGetValue(name, out ulong id) ? id : null;

	/// <summary>The map a typed name means: exactly that map, or the only one containing the text - null otherwise</summary>
	internal string? ResolveMap(string text)
	{
		var exact = _mapList.FirstOrDefault(m => m.Equals(text, StringComparison.OrdinalIgnoreCase));
		if (exact != null)
			return exact;

		var matches = _mapList.Where(m => m.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
		return matches.Count == 1 ? matches[0] : null;
	}

	/// <summary>Maps close to a typed name: prefix, then contains, then the smallest edit distance</summary>
	internal List<string> SuggestMaps(string text, int count = 3)
	{
		string lower = text.ToLowerInvariant();
		return _mapList
			.Select(m => (Map: m, Lower: m.ToLowerInvariant()))
			.OrderBy(m => m.Lower.StartsWith(lower) || m.Lower.StartsWith("surf_" + lower) ? 0 : m.Lower.Contains(lower) ? 1 : 2)
			.ThenBy(m => EditDistance(lower, m.Lower))
			.Take(count)
			.Select(m => m.Map)
			.ToList();
	}

	/// <summary>The folders workshop items are downloaded to - the timer_settings override first</summary>
	private static List<string> WorkshopContentFolders()
	{
		var folders = new List<string>();
		if (Config.WorkshopContentPath.Length > 0)
			folders.Add(Config.WorkshopContentPath);

		string game = Server.GameDirectory;
		folders.Add(Path.Combine(game, "bin", "linuxsteamrt64", "steamapps", "workshop", "content", "730"));
		folders.Add(Path.Combine(game, "bin", "win64", "steamapps", "workshop", "content", "730"));
		folders.Add(Path.Combine(game, "..", "steamapps", "workshop", "content", "730"));
		return folders;
	}

	/// <summary>Reads the server's map list again (plugin load, map start, admin panel) - in the background</summary>
	internal void RefreshMapList()
	{
		if (_mapListRefreshing)
			return;
		_mapListRefreshing = true;

		var folders = WorkshopContentFolders();
		Task.Run(() =>
		{
			_unreadableVpks = 0;
			_lastUnreadableVpk = null;
			var maps = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
			string? used = null;
			int addons = 0;
			try
			{
				foreach (var folder in folders)
				{
					if (!Directory.Exists(folder))
						continue;

					used = folder;
					foreach (var item in Directory.EnumerateDirectories(folder))
					{
						if (!ulong.TryParse(Path.GetFileName(item), out ulong workshopId))
							continue;
						addons++;
						foreach (string name in MapsInAddon(item))
							maps.TryAdd(name, workshopId);
					}
					break;
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "[MapList] Reading the workshop addons failed");
			}

			Server.NextFrame(() =>
			{
				_mapListRefreshing = false;
				if (used == null)
				{
					if (!_mapListWarned)
					{
						_mapListWarned = true;
						_logger.LogWarning("[MapList] No workshop content folder found (looked in {Folders}) - set workshop_content_path in timer_settings.json; map names aren't checked until then",
							string.Join(", ", folders));
					}
					return;
				}

				_mapWorkshopIds = maps;
				_mapList = maps.Keys.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
				MapListUpdatedAt = DateTime.UtcNow;
				_logger.LogInformation("[MapList] {Maps} maps in {Addons} workshop addons ({Folder})", maps.Count, addons, used);
				if (_unreadableVpks > 0)
					_logger.LogWarning("[MapList] {Count} workshop VPKs couldn't be read (last: {Last})", _unreadableVpks, _lastUnreadableVpk);
			});
		});
	}

	/// <summary>
	/// The map names in one workshop item's folder: the maps/&lt;name&gt;.vpk entries of its VPK directories
	/// (&lt;id&gt;.vpk, or &lt;name&gt;_dir.vpk for split archives - the numbered _000.vpk parts hold no tree).
	/// An entry counts as a map when its own (nested) VPK holds a compiled map (.vmap_c) - skyboxes, baked
	/// resource caches and other packages don't. Entries whose nested VPK can't be read fall back to the name
	/// filter (WithoutSubMaps).
	/// </summary>
	private IEnumerable<string> MapsInAddon(string folder)
	{
		var confirmed = new List<string>();
		var unknown = new List<string>();
		var rejected = new List<string>();
		foreach (var file in Directory.EnumerateFiles(folder, "*.vpk"))
		{
			string stem = Path.GetFileNameWithoutExtension(file);
			int underscore = stem.LastIndexOf('_');
			if (underscore >= 0 && stem.Length - underscore == 4 && stem[(underscore + 1)..].All(char.IsDigit))
				continue; // Archive part (_000), not a directory

			try
			{
				foreach (var (name, isMap) in ReadVpkMaps(file))
				{
					if (isMap == true)
						confirmed.Add(name);
					else if (isMap == null)
						unknown.Add(name);
					else
						rejected.Add(name);
				}
			}
			catch (Exception ex)
			{
				// Counted and reported once per scan (RefreshMapList), not per file
				Interlocked.Increment(ref _unreadableVpks);
				_lastUnreadableVpk = $"{file}: {ex.Message}";
			}
		}

		// No .vmap_c anywhere in this addon: the check doesn't apply to how it's packed - guess by name instead
		if (confirmed.Count == 0)
			unknown.AddRange(rejected);

		// Compiled sub-maps (3D skyboxes are maps of their own, loaded by the main map) pass the .vmap_c check -
		// the name rules drop them from both groups
		return WithoutSubMaps(confirmed.Concat(unknown).ToList());
	}

	// Skyboxes and other sub-maps an addon ships next to its map (maps/surf_x_skybox.vpk, ...) - only used when
	// a nested VPK can't be read
	private static readonly string[] SubMapSuffixes = ["_skybox", "_3dskybox", "_sky", "_3dsky", "_background", "_bg", "_bakeresourcecache"];

	/// <summary>
	/// The name-based guess: no skybox / background sub-maps, and no "&lt;map&gt;_&lt;part&gt;" entry next to its own
	/// &lt;map&gt; (sub-maps are named after the map they belong to).
	/// </summary>
	private static List<string> WithoutSubMaps(List<string> names)
	{
		var maps = names.Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(n => !n.Contains("skybox", StringComparison.OrdinalIgnoreCase)
				&& !SubMapSuffixes.Any(suffix => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		return maps.Where(n => !maps.Any(other => other.Length < n.Length
			&& n.StartsWith(other + "_", StringComparison.OrdinalIgnoreCase))).ToList();
	}

	private readonly record struct VpkEntry(string Extension, string Directory, string File, ushort ArchiveIndex,
		uint Offset, uint Length, ushort Preload);

	/// <summary>
	/// A VPK (v1 / v2) directory tree read from the stream's current position. The tree is three nested lists of
	/// null-terminated strings - extension, path, file name - each file followed by its entry data (CRC 4,
	/// preload size 2, archive index 2, offset 4, length 4, terminator 2) and its preload bytes; an empty string
	/// ends a list. dataStart is where the "in this file" data (archive index 0x7FFF) begins. Null: not a VPK.
	/// </summary>
	private static List<VpkEntry>? ReadVpkTree(Stream stream, out long dataStart)
	{
		dataStart = 0;
		var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
		long start = stream.Position;

		if (reader.ReadUInt32() != VpkSignature)
			return null;
		uint version = reader.ReadUInt32();
		uint treeSize = reader.ReadUInt32();
		if (version == 2)
			stream.Seek(16, SeekOrigin.Current); // File data, archive MD5, other MD5 and signature section sizes
		else if (version != 1)
			return null;
		if (treeSize == 0 || treeSize > VpkMaxTreeSize)
			return null;

		long treeStart = stream.Position;
		long treeEnd = treeStart + treeSize;
		dataStart = treeEnd;
		var entries = new List<VpkEntry>();
		while (stream.Position < treeEnd)
		{
			string extension = ReadVpkString(reader);
			if (extension.Length == 0)
				break;
			while (true)
			{
				string directory = ReadVpkString(reader);
				if (directory.Length == 0)
					break;
				while (true)
				{
					string file = ReadVpkString(reader);
					if (file.Length == 0)
						break;

					reader.ReadUInt32(); // CRC
					ushort preload = reader.ReadUInt16();
					ushort archive = reader.ReadUInt16();
					uint offset = reader.ReadUInt32();
					uint length = reader.ReadUInt32();
					reader.ReadUInt16(); // Terminator
					stream.Seek(preload, SeekOrigin.Current);
					entries.Add(new VpkEntry(extension, directory.Replace('\\', '/').Trim('/'), file, archive, offset, length, preload));
				}
			}
		}
		return entries;
	}

	private const ushort VpkSameFile = 0x7FFF;

	/// <summary>
	/// The maps/&lt;name&gt;.vpk entries of an addon VPK, each with whether it is a playable map: true when its
	/// nested VPK holds a .vmap_c, false when it was read and holds none, null when it couldn't be read.
	/// </summary>
	internal static List<(string Name, bool? IsMap)> ReadVpkMaps(string path)
	{
		var result = new List<(string, bool?)>();
		List<VpkEntry>? entries;
		long dataStart;
		using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			entries = ReadVpkTree(stream, out dataStart);
		if (entries == null)
			return result;

		foreach (var entry in entries)
		{
			if (!entry.Extension.Equals("vpk", StringComparison.OrdinalIgnoreCase)
				|| !entry.Directory.Equals("maps", StringComparison.OrdinalIgnoreCase)
				|| !IsValidMapName(entry.File))
				continue;
			result.Add((entry.File, HoldsCompiledMap(path, dataStart, entry)));
		}
		return result;
	}

	/// <summary>Whether a nested map VPK holds a compiled map (.vmap_c) - null when it can't be read</summary>
	private static bool? HoldsCompiledMap(string outerPath, long outerDataStart, VpkEntry entry)
	{
		if (entry.Preload > 0 || entry.Length == 0)
			return null; // Starts in the tree's preload data - not worth reassembling for a check

		string archivePath;
		long position;
		if (entry.ArchiveIndex == VpkSameFile)
		{
			archivePath = outerPath;
			position = outerDataStart + entry.Offset;
		}
		else
		{
			if (!outerPath.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase))
				return null;
			archivePath = outerPath[..^"_dir.vpk".Length] + $"_{entry.ArchiveIndex:000}.vpk";
			position = entry.Offset;
		}

		try
		{
			using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (position + entry.Length > stream.Length)
				return null;
			stream.Seek(position, SeekOrigin.Begin);
			var nested = ReadVpkTree(stream, out _);
			return nested?.Any(e => e.Extension.Equals("vmap_c", StringComparison.OrdinalIgnoreCase));
		}
		catch
		{
			return null;
		}
	}

	private static string ReadVpkString(BinaryReader reader)
	{
		var bytes = new List<byte>(32);
		byte b;
		while ((b = reader.ReadByte()) != 0)
			bytes.Add(b);
		return Encoding.UTF8.GetString(bytes.ToArray());
	}

	private static int EditDistance(string a, string b)
	{
		var previous = new int[b.Length + 1];
		var current = new int[b.Length + 1];
		for (int j = 0; j <= b.Length; j++)
			previous[j] = j;
		for (int i = 1; i <= a.Length; i++)
		{
			current[0] = i;
			for (int j = 1; j <= b.Length; j++)
			{
				int cost = a[i - 1] == b[j - 1] ? 0 : 1;
				current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
			}
			(previous, current) = (current, previous);
		}
		return previous[b.Length];
	}
}
