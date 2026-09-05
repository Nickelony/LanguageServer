using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Caches a per-line parser continuation state so line-oriented scans stay fast after document edits.
/// States are computed lazily from an <see cref="ITextSnapshot"/>; applying an edit preserves the
/// states before the first affected line and invalidates the remainder for recomputation.
/// </summary>
/// <typeparam name="TState">The parser continuation state type carried across lines.</typeparam>
public sealed class IncrementalLineStateCache<TState>
{
	private readonly object _syncRoot = new();
	private readonly Func<string, TState, TState> _transition;
	private readonly List<TState> _cachedLineStartStates = [];
	private ITextSnapshot _snapshot;

	/// <summary>
	/// Initializes a new instance of the <see cref="IncrementalLineStateCache{TState}"/> class.
	/// </summary>
	/// <param name="snapshot">The snapshot that provides line text and line mapping for the cached states.</param>
	/// <param name="transition">
	/// Computes the continuation state for a line given its text and the previous line's state.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="transition"/> is null.
	/// </exception>
	public IncrementalLineStateCache(ITextSnapshot snapshot, Func<string, TState, TState> transition)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(transition);

		_snapshot = snapshot;
		_transition = transition;
	}

	/// <summary>
	/// Gets the parser continuation state that applies at the start of the specified one-based line.
	/// Line numbers below 1 return the default state; line numbers past the end of the snapshot are
	/// clamped to its final line.
	/// </summary>
	/// <param name="lineNumber">The one-based document line number.</param>
	/// <returns>The cached or computed line-start parser state, or the default state for an empty snapshot.</returns>
	public TState GetLineStartState(int lineNumber)
	{
		if (lineNumber <= 1)
			return default!;

		lock (_syncRoot)
		{
			if (_snapshot.LineCount == 0)
				return default!;

			int targetLineNumber = Math.Max(1, Math.Min(lineNumber, _snapshot.LineCount));

			EnsureFirstLineStateCached();
			EnsureStatesCachedThrough(targetLineNumber);
			return _cachedLineStartStates[targetLineNumber - 1];
		}
	}

	/// <summary>
	/// Applies a document edit, preserving cached states before the first affected line and
	/// invalidating the remainder so it can be recomputed from the new snapshot.
	/// </summary>
	/// <param name="newSnapshot">The snapshot that reflects the document after the edit.</param>
	/// <param name="change">The edit delta that was applied to the document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="newSnapshot"/> is null.</exception>
	public void ApplyChange(ITextSnapshot newSnapshot, TextIncrementalChange change)
	{
		ArgumentNullException.ThrowIfNull(newSnapshot);

		lock (_syncRoot)
		{
			_snapshot = newSnapshot;

			if (_cachedLineStartStates.Count == 0)
				return;

			int firstAffectedLineNumber = GetSafeLineNumberForOffset(change.Range.Offset);
			int preservedLineCount = Math.Max(0, firstAffectedLineNumber - 1);

			if (_cachedLineStartStates.Count > preservedLineCount)
				_cachedLineStartStates.RemoveRange(preservedLineCount, _cachedLineStartStates.Count - preservedLineCount);

			if (_cachedLineStartStates.Count > _snapshot.LineCount)
				_cachedLineStartStates.RemoveRange(_snapshot.LineCount, _cachedLineStartStates.Count - _snapshot.LineCount);
		}
	}

	private void EnsureFirstLineStateCached()
	{
		if (_snapshot.LineCount > 0 && _cachedLineStartStates.Count == 0)
			_cachedLineStartStates.Add(default!);
	}

	private void EnsureStatesCachedThrough(int lineNumber)
	{
		int targetLineNumber = Math.Min(lineNumber, _snapshot.LineCount);

		if (targetLineNumber <= 0)
			return;

		while (_cachedLineStartStates.Count < targetLineNumber)
		{
			int previousLineNumber = _cachedLineStartStates.Count;
			ITextLine previousLine = _snapshot.GetLineByNumber(previousLineNumber);
			string previousLineText = _snapshot.GetText(previousLine.Offset, previousLine.Length);
			TState nextState = _transition(previousLineText, _cachedLineStartStates[previousLineNumber - 1]);
			_cachedLineStartStates.Add(nextState);
		}
	}

	private int GetSafeLineNumberForOffset(int offset)
	{
		if (_snapshot.LineCount == 0)
			return 1;

		int safeOffset = Math.Max(0, Math.Min(offset, _snapshot.TextLength));
		return _snapshot.GetLineByOffset(safeOffset).LineNumber;
	}
}
