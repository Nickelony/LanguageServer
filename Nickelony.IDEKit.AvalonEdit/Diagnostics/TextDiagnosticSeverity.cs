namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

/// <summary>
/// Specifies the severity and underline style of a diagnostic segment.
/// </summary>
public enum TextDiagnosticSeverity
{
	/// <summary>Uses the error underline style.</summary>
	Error = 0,

	/// <summary>Uses the warning underline style.</summary>
	Warning = 1,

	/// <summary>Uses the informational underline style.</summary>
	Information = 2,

	/// <summary>Uses the dashed hint underline style.</summary>
	Hint = 3
}
