using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using PhysMesh = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;

namespace SurfTimer;

/// <summary>
/// The edges of a map's player clips, from its compiled world physics (maps/&lt;map&gt;/world_physics.vmdl_c): clip
/// brushes aren't entities in CS2, they're baked into the world collision as shapes on the "playerclip" layer -
/// convex hulls (every edge) and triangle meshes (only where the surface bends, and its border). Edges that touching
/// brushes share are kept once. Any thread.
/// </summary>
internal static class ClipGeometry
{
	private const string ClipTag = "playerclip";
	/// <summary>Invisible ladders interact as player clips too - they're ladders, not walls</summary>
	private const string LadderTag = "ladder";
	/// <summary>Edges shorter than this aren't drawn</summary>
	private const float MinEdgeLength = 2f;
	/// <summary>Two triangles meeting this flat (cosine of their angle) are one surface - their shared edge isn't drawn</summary>
	private const float FlatCosine = 0.996f;

	internal static List<(Vector3 A, Vector3 B)> Read(byte[] worldPhysics)
	{
		using var resource = new Resource();
		resource.Read(new MemoryStream(worldPhysics));
		var phys = resource.Blocks.OfType<PhysAggregateData>().FirstOrDefault()
			?? throw new InvalidDataException("no physics data in world_physics");

		var edges = new List<(Vector3, Vector3)>();
		var seen = new HashSet<(Vector3, Vector3)>();
		void Add(Vector3 a, Vector3 b)
		{
			if (Vector3.DistanceSquared(a, b) < MinEdgeLength * MinEdgeLength)
				return;
			// Rounded, so the same edge of two brushes matches
			Vector3 ra = Round(a), rb = Round(b);
			if (seen.Add(Compare(ra, rb) <= 0 ? (ra, rb) : (rb, ra)))
				edges.Add((a, b));
		}

		foreach (var part in phys.Parts)
		{
			foreach (var hull in part.Shape.Hulls)
			{
				if (!IsClip(phys, hull.CollisionAttributeIndex))
					continue;
				var vertices = hull.Shape.GetVertexPositions();
				var halfEdges = hull.Shape.GetEdges();
				for (int i = 0; i < halfEdges.Length; i++)
				{
					int twin = halfEdges[i].Twin;
					if (i < twin)
						Add(vertices[halfEdges[i].Origin], vertices[halfEdges[twin].Origin]);
				}
			}

			foreach (var mesh in part.Shape.Meshes)
			{
				if (IsClip(phys, mesh.CollisionAttributeIndex))
					AddFeatureEdges(mesh.Shape.GetVertices(), mesh.Shape.GetTriangles(), Add);
			}
		}
		return edges;
	}

	/// <summary>A mesh's edges that show its shape: borders (one triangle) and folds (triangles not in one plane)</summary>
	private static void AddFeatureEdges(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<PhysMesh.Triangle> triangles, Action<Vector3, Vector3> add)
	{
		// Per edge (vertex indexes, smaller first): the normal of its first triangle, and whether it is drawn
		var firstNormal = new Dictionary<(int, int), Vector3>();
		var shared = new HashSet<(int, int)>();
		var folds = new HashSet<(int, int)>();
		Span<int> corners = stackalloc int[3];
		foreach (var triangle in triangles)
		{
			var normal = Vector3.Cross(vertices[triangle.Y] - vertices[triangle.X], vertices[triangle.Z] - vertices[triangle.X]);
			if (normal.LengthSquared() < 1e-6f)
				continue; // Degenerate
			normal = Vector3.Normalize(normal);

			corners[0] = triangle.X;
			corners[1] = triangle.Y;
			corners[2] = triangle.Z;
			for (int i = 0; i < 3; i++)
			{
				int a = corners[i], b = corners[(i + 1) % 3];
				var key = a < b ? (a, b) : (b, a);
				if (!firstNormal.TryAdd(key, normal))
				{
					shared.Add(key);
					// Either winding counts as the same plane
					if (MathF.Abs(Vector3.Dot(firstNormal[key], normal)) < FlatCosine)
						folds.Add(key);
				}
			}
		}

		foreach (var key in firstNormal.Keys)
		{
			if (!shared.Contains(key) || folds.Contains(key))
				add(vertices[key.Item1], vertices[key.Item2]);
		}
	}

	private static bool IsClip(PhysAggregateData phys, int index)
	{
		if (index < 0 || index >= phys.CollisionAttributes.Count)
			return false;
		var attributes = phys.CollisionAttributes[index];
		// Older assets carry their tags in m_PhysicsTagStrings
		var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings") ?? [];
		return tags.Contains(ClipTag, StringComparer.OrdinalIgnoreCase) && !tags.Contains(LadderTag, StringComparer.OrdinalIgnoreCase);
	}

	private static Vector3 Round(Vector3 v) => new(MathF.Round(v.X, 1), MathF.Round(v.Y, 1), MathF.Round(v.Z, 1));

	private static int Compare(Vector3 a, Vector3 b) =>
		a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z);
}
