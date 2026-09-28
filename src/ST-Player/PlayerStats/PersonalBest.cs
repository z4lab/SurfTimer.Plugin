namespace SurfTimer;

/// <summary>
/// A stored time (PB or WR) for one course and style, with its splits for map runs.
/// ID is -1 while there's no time.
/// </summary>
public class PersonalBest : MapTimeRunDataEntity
{
	public Dictionary<int, CheckpointEntity>? Checkpoints { get; set; }

	/// <summary>
	/// Loads the splits of this time (map runs).
	/// </summary>
	internal async Task LoadCheckpoints()
	{
		if (this.ID <= 0)
			return;

		var splits = await TimeRepository.GetSplitsAsync(this.ID);
		this.Checkpoints = splits.Count > 0 ? splits : null;
	}

	/// <summary>
	/// Reloads this time (by ID) with its current rank.
	/// </summary>
	internal async Task ReloadAsync()
	{
		if (this.ID <= 0)
			return;

		var row = await TimeRepository.GetTimeAsync(this.ID);
		row?.Fill(this);
	}

	/// <summary>
	/// Clears the values back to "no time".
	/// </summary>
	internal void Clear()
	{
		this.ID = -1;
		this.RunTime = 0;
		this.Rank = 0;
		this.ReplayId = null;
		this.Sync = null;
		this.Checkpoints = null;
	}
}
