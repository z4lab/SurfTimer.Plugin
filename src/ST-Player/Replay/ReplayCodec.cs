using System.IO.Compression;

namespace SurfTimer;

/// <summary>
/// Binary replay format (replays.data), version 1.
///
/// Header (uncompressed, 13 bytes): magic "STRP", u16 format version, u16 tick rate, u32 frame count,
/// u8 flags (bit 0: frames have buttons). Then a Brotli stream with the frames stored column by column:
/// position x/y/z and angle x/y/z as prediction residuals split into byte planes (see WriteFloatColumn),
/// player flags and buttons (if present) XORed with the previous frame's, then the zone situations as a
/// sparse list (u32 count, then u32 frame + u8 situation).
/// Lossless: a decoded replay is bit for bit the recorded one.
/// </summary>
internal static class ReplayCodec
{
	internal const ushort FormatVersion = 1;
	internal const ushort TickRate = 64;

	private static ReadOnlySpan<byte> Magic => "STRP"u8;
	private const int HeaderSize = 13;
	private const byte FlagHasButtons = 1;
	private const int PositionOrder = 2;
	private const int AngleOrder = 1;

	internal sealed record Encoded(byte[] Data, int FrameCount, int RawSize);

	internal static Encoded Encode(IReadOnlyList<ReplayFrame> frames)
	{
		int count = frames.Count;
		bool hasButtons = count > 0 && frames.All(f => f.Buttons != null);

		using var raw = new MemoryStream();
		using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			for (int axis = 0; axis < 3; axis++)
				WriteFloatColumn(writer, frames, f => f.pos[axis], PositionOrder);
			for (int axis = 0; axis < 3; axis++)
				WriteFloatColumn(writer, frames, f => f.ang[axis], AngleOrder);

			uint previousFlags = 0;
			foreach (var frame in frames)
			{
				writer.Write(frame.Flags ^ previousFlags);
				previousFlags = frame.Flags;
			}

			if (hasButtons)
			{
				ulong previousButtons = 0;
				foreach (var frame in frames)
				{
					ulong buttons = frame.Buttons!.Value;
					writer.Write(buttons ^ previousButtons);
					previousButtons = buttons;
				}
			}

			var situations = new List<(int Frame, ReplayFrameSituation Situation)>();
			for (int i = 0; i < count; i++)
			{
				if (frames[i].Situation != ReplayFrameSituation.NONE)
					situations.Add((i, frames[i].Situation));
			}
			writer.Write((uint)situations.Count);
			foreach (var (frame, situation) in situations)
			{
				writer.Write((uint)frame);
				writer.Write((byte)situation);
			}
		}

		int rawSize = (int)raw.Length;
		raw.Position = 0;

		using var output = new MemoryStream();
		Span<byte> header = stackalloc byte[HeaderSize];
		Magic.CopyTo(header);
		BitConverter.TryWriteBytes(header[4..], FormatVersion);
		BitConverter.TryWriteBytes(header[6..], TickRate);
		BitConverter.TryWriteBytes(header[8..], (uint)count);
		header[12] = hasButtons ? FlagHasButtons : (byte)0;
		output.Write(header);

		using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
			raw.CopyTo(brotli);

		return new Encoded(output.ToArray(), count, rawSize);
	}

	internal static List<ReplayFrame> Decode(byte[] data)
	{
		if (data.Length < HeaderSize || !data.AsSpan(0, 4).SequenceEqual(Magic))
			throw new InvalidDataException("Not a SurfTimer replay (bad magic)");

		ushort version = BitConverter.ToUInt16(data, 4);
		if (version != FormatVersion)
			throw new InvalidDataException($"Unsupported replay format version {version}");

		int count = (int)BitConverter.ToUInt32(data, 8);
		bool hasButtons = (data[12] & FlagHasButtons) != 0;

		using var input = new MemoryStream(data, HeaderSize, data.Length - HeaderSize);
		using var brotli = new BrotliStream(input, CompressionMode.Decompress);
		using var reader = new BinaryReader(brotli);

		var frames = new List<ReplayFrame>(count);
		for (int i = 0; i < count; i++)
			frames.Add(new ReplayFrame { pos = new float[3], ang = new float[3] });

		for (int axis = 0; axis < 3; axis++)
			ReadFloatColumn(reader, frames, (f, v) => f.pos[axis] = v, PositionOrder);
		for (int axis = 0; axis < 3; axis++)
			ReadFloatColumn(reader, frames, (f, v) => f.ang[axis] = v, AngleOrder);

		uint previousFlags = 0;
		foreach (var frame in frames)
		{
			previousFlags ^= reader.ReadUInt32();
			frame.Flags = previousFlags;
		}

		if (hasButtons)
		{
			ulong previousButtons = 0;
			foreach (var frame in frames)
			{
				previousButtons ^= reader.ReadUInt64();
				frame.Buttons = previousButtons;
			}
		}

		uint situations = reader.ReadUInt32();
		for (uint i = 0; i < situations; i++)
		{
			int frame = (int)reader.ReadUInt32();
			var situation = (ReplayFrameSituation)reader.ReadByte();
			if (frame >= 0 && frame < count)
				frames[frame].Situation = situation;
		}

		ReplayFrame.PrepareSync(frames);
		return frames;
	}

	// Float columns: the raw bits as integers, minus a prediction from the previous frames (order 1 =
	// previous value, order 2 = linear extrapolation), zigzagged so small +/- residuals are small numbers.
	// Floats of one sign order like their bits, so smooth movement leaves tiny residuals.
	private static void WriteFloatColumn(BinaryWriter writer, IReadOnlyList<ReplayFrame> frames, Func<ReplayFrame, float> value, int order)
	{
		var residuals = new uint[frames.Count];
		int previous = 0, beforePrevious = 0;
		for (int i = 0; i < frames.Count; i++)
		{
			int bits = BitConverter.SingleToInt32Bits(value(frames[i]));
			int predicted = Predict(previous, beforePrevious, i, order);
			residuals[i] = ZigZag(unchecked(bits - predicted));
			beforePrevious = previous;
			previous = bits;
		}
		WritePlanes(writer, residuals);
	}

	private static void ReadFloatColumn(BinaryReader reader, List<ReplayFrame> frames, Action<ReplayFrame, float> set, int order)
	{
		var residuals = ReadPlanes(reader, frames.Count);
		int previous = 0, beforePrevious = 0;
		for (int i = 0; i < frames.Count; i++)
		{
			int bits = unchecked(Predict(previous, beforePrevious, i, order) + UnZigZag(residuals[i]));
			set(frames[i], BitConverter.Int32BitsToSingle(bits));
			beforePrevious = previous;
			previous = bits;
		}
	}

	private static int Predict(int previous, int beforePrevious, int index, int order) =>
		order == 2 && index >= 2 ? unchecked(2 * previous - beforePrevious) : index >= 1 ? previous : 0;

	private static uint ZigZag(int value) => (uint)((value << 1) ^ (value >> 31));
	private static int UnZigZag(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

	// Byte planes: every value's lowest byte, then every second byte, ... - the high bytes of small
	// residuals become long zero runs that Brotli compresses to almost nothing
	private static void WritePlanes(BinaryWriter writer, uint[] values)
	{
		var plane = new byte[values.Length];
		for (int shift = 0; shift < 32; shift += 8)
		{
			for (int i = 0; i < values.Length; i++)
				plane[i] = (byte)(values[i] >> shift);
			writer.Write(plane);
		}
	}

	private static uint[] ReadPlanes(BinaryReader reader, int count)
	{
		var values = new uint[count];
		for (int shift = 0; shift < 32; shift += 8)
		{
			byte[] plane = reader.ReadBytes(count);
			if (plane.Length != count)
				throw new EndOfStreamException("Replay data is truncated");
			for (int i = 0; i < count; i++)
				values[i] |= (uint)plane[i] << shift;
		}
		return values;
	}
}
