using Nickelony.LanguageServer.Abstractions.Diagnostics;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Caches the latest non-stale diagnostics payload published for a tracked Lua document.
/// </summary>
internal sealed class LuaDiagnosticsCache
{
	private static readonly IReadOnlyList<TextEditorDiagnostic> s_emptyDiagnostics = Array.AsReadOnly(Array.Empty<TextEditorDiagnostic>());

	/// <summary>
	/// Gets the currently cached diagnostics.
	/// </summary>
	internal IReadOnlyList<TextEditorDiagnostic> Diagnostics { get; private set; } = s_emptyDiagnostics;

	/// <summary>
	/// Gets the synchronized document version associated with the cached diagnostics.
	/// </summary>
	internal int Version { get; private set; }

	/// <summary>
	/// Clears the cached diagnostics state.
	/// </summary>
	internal void Clear()
	{
		Diagnostics = s_emptyDiagnostics;
		Version = 0;
	}

	/// <summary>
	/// Stores a diagnostics payload when its version is not stale relative to the current cache.
	/// </summary>
	/// <param name="publishedDiagnostics">The diagnostics payload to cache.</param>
	/// <returns><see langword="true"/> when the payload was stored; otherwise, <see langword="false"/>.</returns>
	internal bool TryStore(LuaPublishedDiagnostics publishedDiagnostics)
	{
		if (!LuaDocumentVersionHelper.TryAccept(Version, publishedDiagnostics.Version, out int acceptedVersion))
			return false;

		Version = acceptedVersion;

		Diagnostics = CreateReadOnlyDiagnostics(publishedDiagnostics.Diagnostics);
		return true;
	}

	private static IReadOnlyList<TextEditorDiagnostic> CreateReadOnlyDiagnostics(IReadOnlyList<TextEditorDiagnostic>? diagnostics)
	{
		return diagnostics is null || diagnostics.Count == 0
			? s_emptyDiagnostics
			: Array.AsReadOnly([.. diagnostics]);
	}
}
