namespace SurfTimer;

/// <summary>
/// A prism zone's shape: a polygon footprint (any simple polygon, concave allowed) whose points each have
/// their own height, extended straight up by Height. The floor between the points is interpolated over a
/// triangulation of the footprint, so two low and two high points make a ramp / stair zone. Built once
/// per activation; read every tick.
/// </summary>
internal sealed class ZoneGeometry
{
	private readonly float[] _x, _y, _z;
	private readonly int[] _triangles; // Index triples into the points

	internal float Height { get; }
	internal VectorT Mins { get; }
	internal VectorT Maxs { get; }
	internal IReadOnlyList<VectorT> Points { get; }

	private ZoneGeometry(IReadOnlyList<VectorT> points, float height, int[] triangles)
	{
		Points = points.ToList();
		Height = height;
		_x = points.Select(p => p.X).ToArray();
		_y = points.Select(p => p.Y).ToArray();
		_z = points.Select(p => p.Z).ToArray();
		_triangles = triangles;
		Mins = new VectorT(_x.Min(), _y.Min(), _z.Min());
		Maxs = new VectorT(_x.Max(), _y.Max(), _z.Max() + height);
	}

	/// <summary>The geometry of a footprint - null for fewer than 3 points or one that can't be triangulated</summary>
	internal static ZoneGeometry? Build(IReadOnlyList<VectorT> points, float height)
	{
		if (points.Count < 3)
			return null;
		var triangles = Triangulate(points);
		return triangles == null ? null : new ZoneGeometry(points, MathF.Max(1f, height), triangles);
	}

	// ---- Tests ----

	/// <summary>Whether a box (a player's hull) overlaps the prism</summary>
	internal bool Overlaps(in VectorT mins, in VectorT maxs)
	{
		if (mins.X > Maxs.X || maxs.X < Mins.X || mins.Y > Maxs.Y || maxs.Y < Mins.Y || mins.Z > Maxs.Z || maxs.Z < Mins.Z)
			return false;
		if (!FootprintOverlapsRect(mins.X, mins.Y, maxs.X, maxs.Y))
			return false;

		// Vertical: the floor under the hull's center (clamped onto the footprint when it hangs over an edge)
		float cx = (mins.X + maxs.X) / 2, cy = (mins.Y + maxs.Y) / 2;
		if (!InsideFootprint(cx, cy))
			(cx, cy) = ClosestOnBoundary(cx, cy);
		float floor = FloorAt(cx, cy);
		return mins.Z <= floor + Height && maxs.Z >= floor;
	}

	internal bool Contains(in VectorT point)
	{
		if (!InsideFootprint(point.X, point.Y))
			return false;
		float floor = FloorAt(point.X, point.Y);
		return point.Z >= floor && point.Z <= floor + Height;
	}

	/// <summary>The floor height at a footprint point - interpolated in its triangle</summary>
	internal float FloorAt(float x, float y)
	{
		for (int t = 0; t < _triangles.Length; t += 3)
		{
			int a = _triangles[t], b = _triangles[t + 1], c = _triangles[t + 2];
			if (Barycentric(x, y, a, b, c, out float wa, out float wb, out float wc))
				return wa * _z[a] + wb * _z[b] + wc * _z[c];
		}

		// Outside (on the boundary within float error): the nearest point's height
		int nearest = 0;
		float best = float.MaxValue;
		for (int i = 0; i < _x.Length; i++)
		{
			float d = (_x[i] - x) * (_x[i] - x) + (_y[i] - y) * (_y[i] - y);
			if (d < best)
			{
				best = d;
				nearest = i;
			}
		}
		return _z[nearest];
	}

	private bool Barycentric(float x, float y, int a, int b, int c, out float wa, out float wb, out float wc)
	{
		float det = (_y[b] - _y[c]) * (_x[a] - _x[c]) + (_x[c] - _x[b]) * (_y[a] - _y[c]);
		wa = wb = wc = 0;
		if (MathF.Abs(det) < 1e-6f)
			return false;
		wa = ((_y[b] - _y[c]) * (x - _x[c]) + (_x[c] - _x[b]) * (y - _y[c])) / det;
		wb = ((_y[c] - _y[a]) * (x - _x[c]) + (_x[a] - _x[c]) * (y - _y[c])) / det;
		wc = 1 - wa - wb;
		const float eps = -1e-4f;
		return wa >= eps && wb >= eps && wc >= eps;
	}

	/// <summary>Point in polygon (2D, even-odd)</summary>
	internal bool InsideFootprint(float x, float y)
	{
		bool inside = false;
		for (int i = 0, j = _x.Length - 1; i < _x.Length; j = i++)
		{
			if ((_y[i] > y) != (_y[j] > y)
				&& x < (_x[j] - _x[i]) * (y - _y[i]) / (_y[j] - _y[i]) + _x[i])
				inside = !inside;
		}
		return inside;
	}

	private bool FootprintOverlapsRect(float minX, float minY, float maxX, float maxY)
	{
		int n = _x.Length;
		for (int i = 0; i < n; i++)
		{
			if (_x[i] >= minX && _x[i] <= maxX && _y[i] >= minY && _y[i] <= maxY)
				return true; // A polygon point inside the rectangle
		}

		if (InsideFootprint(minX, minY) || InsideFootprint(maxX, minY) || InsideFootprint(maxX, maxY) || InsideFootprint(minX, maxY))
			return true; // A rectangle corner inside the polygon

		for (int i = 0, j = n - 1; i < n; j = i++)
		{
			// Any polygon edge crossing a rectangle edge
			if (SegmentsCross(_x[j], _y[j], _x[i], _y[i], minX, minY, maxX, minY)
				|| SegmentsCross(_x[j], _y[j], _x[i], _y[i], maxX, minY, maxX, maxY)
				|| SegmentsCross(_x[j], _y[j], _x[i], _y[i], maxX, maxY, minX, maxY)
				|| SegmentsCross(_x[j], _y[j], _x[i], _y[i], minX, maxY, minX, minY))
				return true;
		}
		return false;
	}

	private (float X, float Y) ClosestOnBoundary(float x, float y)
	{
		float bestX = _x[0], bestY = _y[0], best = float.MaxValue;
		for (int i = 0, j = _x.Length - 1; i < _x.Length; j = i++)
		{
			float ex = _x[i] - _x[j], ey = _y[i] - _y[j];
			float len = ex * ex + ey * ey;
			float t = len > 0 ? Math.Clamp(((x - _x[j]) * ex + (y - _y[j]) * ey) / len, 0, 1) : 0;
			float px = _x[j] + t * ex, py = _y[j] + t * ey;
			float d = (px - x) * (px - x) + (py - y) * (py - y);
			if (d < best)
			{
				best = d;
				bestX = px;
				bestY = py;
			}
		}
		return (bestX, bestY);
	}

	// ---- Building ----

	private static float Cross(float ax, float ay, float bx, float by, float cx, float cy) =>
		(bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

	private static bool SegmentsCross(float ax, float ay, float bx, float by, float cx, float cy, float dx, float dy)
	{
		float d1 = Cross(cx, cy, dx, dy, ax, ay);
		float d2 = Cross(cx, cy, dx, dy, bx, by);
		float d3 = Cross(ax, ay, bx, by, cx, cy);
		float d4 = Cross(ax, ay, bx, by, dx, dy);
		return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
	}

	/// <summary>A footprint whose edges don't cross each other (2D) and that has an area</summary>
	internal static bool IsSimple(IReadOnlyList<VectorT> points)
	{
		int n = points.Count;
		if (n < 3 || MathF.Abs(SignedArea(points)) < 1f)
			return false;

		for (int i = 0; i < n; i++)
		{
			var a = points[i];
			var b = points[(i + 1) % n];
			for (int j = i + 1; j < n; j++)
			{
				// Neighbouring edges share a point - not a crossing
				if (j == i || (j + 1) % n == i || (i + 1) % n == j)
					continue;
				var c = points[j];
				var d = points[(j + 1) % n];
				if (SegmentsCross(a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y))
					return false;
			}
		}
		return true;
	}

	private static float SignedArea(IReadOnlyList<VectorT> points)
	{
		float area = 0;
		for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
			area += points[j].X * points[i].Y - points[i].X * points[j].Y;
		return area / 2;
	}

	/// <summary>Ear clipping (2D) - triangle index triples, or null when the polygon can't be clipped</summary>
	private static int[]? Triangulate(IReadOnlyList<VectorT> points)
	{
		int n = points.Count;
		var indices = Enumerable.Range(0, n).ToList();
		if (SignedArea(points) < 0)
			indices.Reverse(); // Counter-clockwise for the convexity test below

		var triangles = new List<int>();
		int guard = 0;
		while (indices.Count > 3 && guard++ < n * n)
		{
			bool clipped = false;
			for (int k = 0; k < indices.Count; k++)
			{
				int prev = indices[(k + indices.Count - 1) % indices.Count];
				int cur = indices[k];
				int next = indices[(k + 1) % indices.Count];
				var a = points[prev];
				var b = points[cur];
				var c = points[next];
				if (Cross(a.X, a.Y, b.X, b.Y, c.X, c.Y) <= 0)
					continue; // Reflex corner - not an ear

				bool containsOther = false;
				foreach (int other in indices)
				{
					if (other == prev || other == cur || other == next)
						continue;
					var p = points[other];
					if (Cross(a.X, a.Y, b.X, b.Y, p.X, p.Y) >= 0 && Cross(b.X, b.Y, c.X, c.Y, p.X, p.Y) >= 0
						&& Cross(c.X, c.Y, a.X, a.Y, p.X, p.Y) >= 0)
					{
						containsOther = true;
						break;
					}
				}
				if (containsOther)
					continue;

				triangles.AddRange([prev, cur, next]);
				indices.RemoveAt(k);
				clipped = true;
				break;
			}
			if (!clipped)
				return null;
		}

		if (indices.Count == 3)
			triangles.AddRange(indices);
		return triangles.Count >= 3 ? triangles.ToArray() : null;
	}
}
