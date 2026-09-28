using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace SurfTimer;

internal static class ConVarHelper
{
	public static void RemoveCheatFlagFromConVar(string cv_name)
	{
		ConVar? cv = ConVar.Find(cv_name);
		if (cv == null || (cv.Flags & CounterStrikeSharp.API.ConVarFlags.FCVAR_CHEAT) == 0)
			return;

		cv.Flags &= ~CounterStrikeSharp.API.ConVarFlags.FCVAR_CHEAT;
	}

	/// <summary>
	/// ConVars are typed - reads one as the text you'd type in the console.
	/// </summary>
	internal static string Read(ConVar convar) => convar.Type switch
	{
		ConVarType.Bool => convar.GetPrimitiveValue<bool>() ? "1" : "0",
		ConVarType.Int16 or ConVarType.Int32 or ConVarType.UInt16 or ConVarType.UInt32 => convar.GetPrimitiveValue<int>().ToString(),
		ConVarType.Int64 or ConVarType.UInt64 => convar.GetPrimitiveValue<long>().ToString(),
		ConVarType.Float32 => convar.GetPrimitiveValue<float>().ToString(CultureInfo.InvariantCulture),
		ConVarType.Float64 => convar.GetPrimitiveValue<double>().ToString(CultureInfo.InvariantCulture),
		_ => convar.StringValue,
	};
}
