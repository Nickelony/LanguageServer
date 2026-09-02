namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

/// <summary>
/// Describes the severity of a diagnostic segment rendered by <see cref="DiagnosticsRenderer"/>.
/// </summary>
public enum TextDiagnosticSeverity
{
	/// <summary>The segment represents an error.</summary>
	Error = 0,

	/// <summary>The segment represents a warning.</summary>
	Warning = 1,

	/// <summary>The segment represents informational content.</summary>
	Information = 2,

	/// <summary>The segment represents a hint.</summary>
	Hint = 3
}
