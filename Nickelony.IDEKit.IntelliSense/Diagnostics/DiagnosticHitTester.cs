namespace Nickelony.IDEKit.IntelliSense.Diagnostics;

/// <summary>
/// Selects and formats diagnostics for hover hit-testing without any editor or UI dependency.
/// </summary>
/// <remarks>
/// The kernel is document- and presentation-agnostic: callers supply the diagnostic list and
/// integer offsets, and receive the ordered selection or a formatted message. Severity labels
/// are injectable so hosts can localize or customize the "Error:"/"Warning:" prefixes.
/// </remarks>
public static class DiagnosticHitTester
{
	/// <summary>
	/// Gets the diagnostics whose span contains the supplied offset.
	/// </summary>
	/// <param name="diagnostics">The diagnostics to search.</param>
	/// <param name="offset">The zero-based offset to test.</param>
	/// <returns>The matching diagnostics, ordered by severity then start offset.</returns>
	public static IReadOnlyList<TextEditorDiagnostic> GetDiagnosticsAtOffset(
		IReadOnlyList<TextEditorDiagnostic> diagnostics,
		int offset)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		return diagnostics
			.Where(diagnostic => diagnostic.ContainsOffset(offset))
			.OrderBy(diagnostic => diagnostic.Severity)
			.ThenBy(diagnostic => diagnostic.StartOffset)
			.ToArray();
	}

	/// <summary>
	/// Gets the diagnostics whose span intersects the supplied range.
	/// </summary>
	/// <param name="diagnostics">The diagnostics to search.</param>
	/// <param name="startOffset">The zero-based inclusive start offset of the range.</param>
	/// <param name="endOffset">The zero-based exclusive end offset of the range.</param>
	/// <returns>The matching diagnostics, ordered by severity then start offset.</returns>
	public static IReadOnlyList<TextEditorDiagnostic> GetDiagnosticsForRange(
		IReadOnlyList<TextEditorDiagnostic> diagnostics,
		int startOffset,
		int endOffset)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		return diagnostics
			.Where(diagnostic => diagnostic.Intersects(startOffset, endOffset))
			.OrderBy(diagnostic => diagnostic.Severity)
			.ThenBy(diagnostic => diagnostic.StartOffset)
			.ToArray();
	}

	/// <summary>
	/// Selects the diagnostics shown for a hover at the supplied offset, optionally falling back
	/// to the diagnostics intersecting a containing line range when no diagnostic covers the
	/// exact offset.
	/// </summary>
	/// <param name="diagnostics">The diagnostics to search.</param>
	/// <param name="hoveredOffset">The zero-based hovered offset.</param>
	/// <param name="allowLineFallback">
	/// Whether to fall back to a line-wide selection when the exact offset has no diagnostic.
	/// </param>
	/// <param name="lineStartOffset">
	/// The zero-based inclusive start offset of the containing line, used for the fallback.
	/// </param>
	/// <param name="lineEndOffset">
	/// The zero-based exclusive end offset of the containing line, used for the fallback.
	/// </param>
	/// <returns>The selected diagnostics, ordered by severity then start offset.</returns>
	public static IReadOnlyList<TextEditorDiagnostic> SelectHoverDiagnostics(
		IReadOnlyList<TextEditorDiagnostic> diagnostics,
		int hoveredOffset,
		bool allowLineFallback,
		int lineStartOffset,
		int lineEndOffset)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		IReadOnlyList<TextEditorDiagnostic> hovered = GetDiagnosticsAtOffset(diagnostics, hoveredOffset);

		if (hovered.Count == 0 && allowLineFallback)
			hovered = GetDiagnosticsForRange(diagnostics, lineStartOffset, lineEndOffset);

		return hovered;
	}

	/// <summary>
	/// Formats a diagnostic message, prefixing the supplied severity label when the message does
	/// not already begin with a recognized built-in severity prefix. A <see langword="null"/>
	/// label emits the raw message.
	/// </summary>
	/// <param name="diagnostic">The diagnostic to format.</param>
	/// <param name="severityLabel">The severity label, or <see langword="null"/> to use the raw message.</param>
	/// <returns>The formatted message.</returns>
	public static string FormatMessage(
		TextEditorDiagnostic diagnostic,
		Func<TextEditorDiagnosticSeverity, string>? severityLabel = null)
	{
		ArgumentNullException.ThrowIfNull(diagnostic);

		if (string.IsNullOrWhiteSpace(diagnostic.Message))
			return string.Empty;

		if (severityLabel is null || IsSeverityPrefixed(diagnostic.Message))
			return diagnostic.Message;

		return severityLabel(diagnostic.Severity) + ":\n" + diagnostic.Message;
	}

	/// <summary>
	/// Builds a combined hover message from the supplied diagnostics, deduplicating identical
	/// messages and preserving their input order.
	/// </summary>
	/// <param name="diagnostics">The diagnostics to combine, in the order in which their messages should appear.</param>
	/// <param name="severityLabel">The severity label, or <see langword="null"/> to use raw messages.</param>
	/// <returns>The combined message, or <see langword="null"/> when no non-empty message remains.</returns>
	public static string? BuildCombinedMessage(
		IReadOnlyList<TextEditorDiagnostic> diagnostics,
		Func<TextEditorDiagnosticSeverity, string>? severityLabel = null)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		string message = string.Join(
			Environment.NewLine + Environment.NewLine,
			diagnostics
				.Select(diagnostic => FormatMessage(diagnostic, severityLabel))
				.Where(text => !string.IsNullOrWhiteSpace(text))
				.Distinct(StringComparer.Ordinal));

		return string.IsNullOrWhiteSpace(message) ? null : message;
	}

	private static bool IsSeverityPrefixed(string message)
		=> !string.IsNullOrWhiteSpace(message)
			&& (message.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
				|| message.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)
				|| message.StartsWith("Information:", StringComparison.OrdinalIgnoreCase)
				|| message.StartsWith("Hint:", StringComparison.OrdinalIgnoreCase)
				|| message.StartsWith("Diagnostic:", StringComparison.OrdinalIgnoreCase));
}
