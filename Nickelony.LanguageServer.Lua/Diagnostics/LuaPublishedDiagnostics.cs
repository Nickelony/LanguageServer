using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Represents a diagnostics payload published by LuaLS for a document, together with the document version it
/// was produced for (<c>0</c> when the server reported none).
/// </summary>
internal sealed class LuaPublishedDiagnostics
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LuaPublishedDiagnostics"/> class.
	/// </summary>
	/// <param name="filePath">The normalized file path associated with the diagnostics.</param>
	/// <param name="diagnostics">The parsed diagnostics for that file.</param>
	/// <param name="version">The synchronized document version that produced the diagnostics.</param>
	internal LuaPublishedDiagnostics(string filePath, IReadOnlyList<TextDiagnostic> diagnostics, int version)
	{
		FilePath = filePath;
		Diagnostics = diagnostics;
		Version = version;
	}

	/// <summary>
	/// Gets the normalized file path associated with the diagnostics.
	/// </summary>
	internal string FilePath { get; }

	/// <summary>
	/// Gets the parsed diagnostics payload.
	/// </summary>
	internal IReadOnlyList<TextDiagnostic> Diagnostics { get; }

	/// <summary>
	/// Gets the synchronized document version that produced the diagnostics, or <c>0</c> when the server did not report a version.
	/// </summary>
	internal int Version { get; }
}
