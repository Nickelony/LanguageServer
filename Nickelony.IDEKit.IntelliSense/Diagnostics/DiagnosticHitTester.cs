namespace Nickelony.IDEKit.IntelliSense.Diagnostics;

/// <summary>
/// Selects diagnostics at a zero-based offset or within a range, independent of any editor or UI.
/// </summary>
/// <remarks>
/// <para>
/// This helper is document-agnostic: callers supply the diagnostic list and zero-based UTF-16
/// offsets, and receive the selection ordered by start offset, then by end offset; diagnostics with
/// identical spans keep no defined relative order. Selections apply no severity policy, so callers
/// that want severity-first results copy and sort the returned list. Message formatting and severity
/// labels are host presentation and live in editor host packages. A selection with no matches is a
/// shared empty result that callers must not mutate, and elements of the supplied list must not be
/// <see langword="null"/>.
/// </para>
/// </remarks>
public static class DiagnosticHitTester
{
	private static readonly Comparison<TextDiagnostic> s_documentOrder = static (left, right) =>
	{
		int byStartOffset = left.StartOffset.CompareTo(right.StartOffset);

		return byStartOffset != 0
			? byStartOffset
			: left.EndOffset.CompareTo(right.EndOffset);
	};

	/// <summary>
	/// Gets the diagnostics whose span contains the supplied offset.
	/// </summary>
	/// <param name="diagnostics">The diagnostics to search.</param>
	/// <param name="offset">The zero-based offset to test.</param>
	/// <returns>
	/// The matching diagnostics, ordered by start offset then end offset; a selection with no match
	/// is a shared empty result.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="diagnostics"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="diagnostics"/> contains a <see langword="null"/> element.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative.</exception>
	public static IReadOnlyList<TextDiagnostic> GetDiagnosticsAtOffset(
		IReadOnlyList<TextDiagnostic> diagnostics,
		int offset)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);
		ValidateElements(diagnostics, nameof(diagnostics));
		ArgumentOutOfRangeException.ThrowIfNegative(offset);

		List<TextDiagnostic>? matches = null;

		for (int i = 0; i < diagnostics.Count; i++)
		{
			TextDiagnostic diagnostic = diagnostics[i];

			if (diagnostic.ContainsOffset(offset))
				(matches ??= new List<TextDiagnostic>()).Add(diagnostic);
		}

		return Order(matches);
	}

	/// <summary>
	/// Gets the diagnostics whose span intersects the supplied range.
	/// </summary>
	/// <remarks>
	/// An empty or reversed range selects nothing. Offsets must not be negative: the selection entry
	/// point validates its inputs, while <see cref="TextDiagnostic.Intersects"/> tolerates a
	/// negative start for callers that compare ranges outside this helper.
	/// </remarks>
	/// <param name="diagnostics">The diagnostics to search.</param>
	/// <param name="startOffset">The zero-based inclusive start offset of the range.</param>
	/// <param name="endOffset">The zero-based exclusive end offset of the range.</param>
	/// <returns>
	/// The matching diagnostics, ordered by start offset then end offset; a selection with no match
	/// is a shared empty result.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="diagnostics"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="diagnostics"/> contains a <see langword="null"/> element.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> or <paramref name="endOffset"/> is negative.
	/// </exception>
	public static IReadOnlyList<TextDiagnostic> GetDiagnosticsForRange(
		IReadOnlyList<TextDiagnostic> diagnostics,
		int startOffset,
		int endOffset)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);
		ValidateElements(diagnostics, nameof(diagnostics));
		ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
		ArgumentOutOfRangeException.ThrowIfNegative(endOffset);

		List<TextDiagnostic>? matches = null;

		for (int i = 0; i < diagnostics.Count; i++)
		{
			TextDiagnostic diagnostic = diagnostics[i];

			if (diagnostic.Intersects(startOffset, endOffset))
				(matches ??= new List<TextDiagnostic>()).Add(diagnostic);
		}

		return Order(matches);
	}

	/// <summary>
	/// Returns the collected matches in document order, or the shared empty result when nothing matched.
	/// </summary>
	/// <param name="matches">The collected matches, or <see langword="null"/> when nothing matched.</param>
	/// <returns>The ordered list, or the shared empty result when nothing matched.</returns>
	private static IReadOnlyList<TextDiagnostic> Order(List<TextDiagnostic>? matches)
	{
		if (matches is null)
			return Array.Empty<TextDiagnostic>();

		matches.Sort(s_documentOrder);
		return matches;
	}

	/// <summary>
	/// Validates that the diagnostics list contains no <see langword="null"/> element, so a violated
	/// precondition fails at the entry point instead of as a null dereference mid-scan.
	/// </summary>
	/// <param name="diagnostics">The diagnostics to validate.</param>
	/// <param name="paramName">The parameter name reported by the exception.</param>
	/// <exception cref="ArgumentException">The list contains a <see langword="null"/> element.</exception>
	private static void ValidateElements(IReadOnlyList<TextDiagnostic> diagnostics, string paramName)
	{
		for (int i = 0; i < diagnostics.Count; i++)
		{
			if (diagnostics[i] is null)
			{
				throw new ArgumentException(
					$"The diagnostics collection contains a null element at index {i}.",
					paramName);
			}
		}
	}
}
