namespace Nickelony.IDEKit.KeyBindings.Tests;

/// <summary>
/// Command identifiers used to exercise key binding dispatch and service
/// behavior with a representative set of commands.
/// </summary>
public enum TestCommand
{
	None,

	NewFile,
	Save,
	SaveAll,
	Build,
	Exit,
	Undo,
	Redo,
	Find,
	GoToDefinition
}
