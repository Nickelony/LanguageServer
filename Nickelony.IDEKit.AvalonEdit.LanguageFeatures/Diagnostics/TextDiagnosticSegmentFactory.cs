using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;

/// <summary>
/// Projects IntelliSense diagnostics into the AvalonEdit diagnostic renderer's segment model and creates
/// wired-up renderers, so hosts do not have to hand-map <see cref="TextDiagnostic"/> values.
/// </summary>
/// <remarks>
/// <para>
/// The renderer queries its segment provider during every render pass, so <see cref="Create"/> is the
/// caching path: project once, hand the returned list back from the host's provider, and invalidate the
/// text view when the diagnostics change (for example with
/// <see cref="ICSharpCode.AvalonEdit.Rendering.TextView.Redraw()"/>). <see cref="CreateProvider"/> does not
/// cache; it re-projects the diagnostics on every render pass, which is convenient for small diagnostic
/// sets but allocates per pass for large ones.
/// </para>
/// <para>
/// A diagnostics provider that temporarily has no data returns an empty list; a <see langword="null"/>
/// result is rejected with <see cref="ArgumentNullException"/> by the projection instead of being rendered
/// as empty.
/// </para>
/// </remarks>
public static class TextDiagnosticSegmentFactory
{
	/// <summary>
	/// Projects diagnostics into renderer segments. The segment offsets match the diagnostic offsets; the
	/// renderer clamps them against the drawn document.
	/// </summary>
	/// <param name="diagnostics">The diagnostics to project.</param>
	/// <returns>The projected segments, in the diagnostics' order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<TextDiagnosticSegment> Create(IReadOnlyList<TextDiagnostic> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		var segments = new TextDiagnosticSegment[diagnostics.Count];

		for (int index = 0; index < diagnostics.Count; index++)
		{
			TextDiagnostic diagnostic = diagnostics[index];

			segments[index] = new TextDiagnosticSegment(
				diagnostic.StartOffset,
				diagnostic.EndOffset,
				diagnostic.Severity);
		}

		return segments;
	}

	/// <summary>
	/// Creates a segment provider for the renderer from a diagnostics provider.
	/// </summary>
	/// <param name="diagnosticsProvider">Provides the diagnostics to render.</param>
	/// <returns>
	/// A provider that projects the provided diagnostics on each render pass, without caching the
	/// projection.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="diagnosticsProvider"/> is <see langword="null"/>.</exception>
	public static Func<IReadOnlyList<TextDiagnosticSegment>> CreateProvider(
		Func<IReadOnlyList<TextDiagnostic>> diagnosticsProvider)
	{
		ArgumentNullException.ThrowIfNull(diagnosticsProvider);

		return () => Create(diagnosticsProvider());
	}

	/// <summary>
	/// Creates a diagnostic renderer whose segments are the projection of the provided diagnostics.
	/// Add it to the text view's background renderers and invalidate the view when the diagnostics change.
	/// </summary>
	/// <param name="diagnosticsProvider">Provides the diagnostics to render.</param>
	/// <returns>The created renderer.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="diagnosticsProvider"/> is <see langword="null"/>.</exception>
	public static DiagnosticsRenderer CreateRenderer(
		Func<IReadOnlyList<TextDiagnostic>> diagnosticsProvider)
		=> new(CreateProvider(diagnosticsProvider));
}
