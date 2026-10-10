using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SurfTimer;

public partial class SurfTimer
{
	[ConsoleCommand("css_quake", "Turn all timer sounds on / off (each kind in !options - Sound).")]
	[CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	public void QuakeCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || PlayerOf(player) is not { } target)
			return;

		target.Options.SoundsEnabled = !target.Options.SoundsEnabled;
		PrintOptionChanged(player, target.Options.SoundsEnabled ? "sounds_on" : "sounds_off");
	}
}
