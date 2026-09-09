using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// A validated batch of text edit operations ordered from highest to lowest source offset.
/// </summary>
/// <remarks>
/// <para>
/// The constructor validates the batch once - order, overlaps, and the required order of a
/// replacement and an insertion that share a start offset - so an accepted instance can be applied
/// or mapped without re-validating the sequence. Multiple insertions at one offset are valid; their
/// text order follows the list order (see <see cref="Operations"/>). Create batches through
/// <see cref="TextEditKernel.Prepare(ITextSnapshot, IEnumerable{TextEditInput?})"/>
/// when the edits must also be validated against a snapshot.
/// </para>
/// <para>
/// <see cref="ITextEditTarget"/> implementations apply <see cref="Operations"/> in list order.
/// </para>
/// </remarks>
public sealed class PreparedTextEdits
{
	/// <summary>
	/// Initializes a validated batch of operations.
	/// </summary>
	/// <param name="operations">The operations ordered from highest to lowest source offset.</param>
	/// <exception cref="ArgumentNullException"><paramref name="operations"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="operations"/> contains a <see langword="null"/> entry, is not ordered from
	/// highest to lowest source offset, contains overlapping operations, or orders an insertion
	/// before a replacement that starts at the same source offset.
	/// </exception>
	public PreparedTextEdits(IReadOnlyList<TextEditOperation> operations)
		: this(operations, skipValidation: false)
	{
	}

	/// <summary>
	/// Creates a batch from operations the edit kernel already validated.
	/// </summary>
	/// <remarks>
	/// Kernel-validated only. The caller must have validated the same rules as the public constructor
	/// (ordering, overlaps, and the same-start-offset order of a replacement and an insertion). The
	/// kernel checks the shared conflict rules before constructing the carrier, so this factory skips
	/// the re-detection that would otherwise run twice on every preparation.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="operations"/> is <see langword="null"/>.</exception>
	internal static PreparedTextEdits CreateKernelValidated(IReadOnlyList<TextEditOperation> operations)
		=> new(operations, skipValidation: true);

	private PreparedTextEdits(IReadOnlyList<TextEditOperation> operations, bool skipValidation)
	{
		ArgumentNullException.ThrowIfNull(operations);

		// Copy first, so validation and the stored sequence see the same entries even when the
		// caller's list is mutated concurrently.
		TextEditOperation[] copy = [.. operations];

		if (!skipValidation)
			ValidateOperations(copy);

		Operations = Array.AsReadOnly(copy);
	}

	/// <summary>
	/// Gets the validated operations, ordered from highest to lowest source offset. Operations that
	/// share a start offset are ordered so that applying the list in order gives their texts the
	/// source-index order (the lowest source index lands first), which the preparation kernel
	/// guarantees; a hand-built batch may order them differently, which changes the resulting text
	/// correspondingly.
	/// </summary>
	public IReadOnlyList<TextEditOperation> Operations { get; }

	/// <summary>
	/// Maps an offset from the source snapshot to the text after these operations are applied.
	/// </summary>
	/// <remarks>
	/// <list type="bullet">
	/// <item>An offset inside a replaced range maps to the same relative position within the
	/// replacement text, clamped to its end.</item>
	/// <item>An offset at the end of a replaced range (a range with a non-empty source extent) maps
	/// after the replacement.</item>
	/// <item>An offset equal to an insertion's offset maps before the inserted text, so inserted text
	/// never shifts an offset that coincided with it.</item>
	/// <item>An insertion at the end of the text maps the old end offset to the start of the inserted
	/// text (shifted by any operations below it), so the insertion's own length does not shift it.</item>
	/// <item>An offset after every operation is shifted by the total length delta of the
	/// operations.</item>
	/// </list>
	/// Because the batch is validated at construction, this method does not repeat that validation.
	/// Each call costs one pass over the operations at or below the offset; use
	/// <see cref="MapOffsets(ReadOnlySpan{int})"/> to map several offsets with one shared pass.
	/// </remarks>
	/// <param name="offset">The zero-based source offset to map; values past the end of the source
	/// text are accepted and shifted by the total length delta.</param>
	/// <returns>The corresponding zero-based offset in the edited text.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative.</exception>
	public int MapOffset(int offset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);

		int operationIndex = Operations.Count - 1;
		int cumulativeDelta = 0;

		return MapOffsetCore(offset, Operations, ref operationIndex, ref cumulativeDelta);
	}

	/// <summary>
	/// Maps many offsets with one shared pass over the operations, so mapping several offsets over
	/// one large batch is cheaper than calling <see cref="MapOffset(int)"/> repeatedly.
	/// </summary>
	/// <remarks>
	/// Each result equals the <see cref="MapOffset(int)"/> result for the same element, and the walk
	/// visits the operations once regardless of how many offsets are mapped.
	/// </remarks>
	/// <param name="offsets">The zero-based source offsets to map; values past the end of the source
	/// text are accepted and shifted by the total length delta.</param>
	/// <returns>An array with one mapped offset per input element, in input order.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Any offset is negative.</exception>
	public int[] MapOffsets(ReadOnlySpan<int> offsets)
	{
		if (offsets.Length == 0)
			return [];

		// Validate first, so a negative offset cannot leave the result partially computed.
		for (int index = 0; index < offsets.Length; index++)
			ArgumentOutOfRangeException.ThrowIfNegative(offsets[index]);

		int[] sortedOffsets = offsets.ToArray();
		var sourceIndexes = new int[sortedOffsets.Length];

		for (int index = 0; index < sourceIndexes.Length; index++)
			sourceIndexes[index] = index;

		Array.Sort(sortedOffsets, sourceIndexes);

		var sortedResults = new int[sortedOffsets.Length];
		int operationIndex = Operations.Count - 1;
		int cumulativeDelta = 0;

		for (int index = 0; index < sortedOffsets.Length; index++)
			sortedResults[index] = MapOffsetCore(sortedOffsets[index], Operations, ref operationIndex, ref cumulativeDelta);

		var results = new int[sortedOffsets.Length];

		for (int index = 0; index < sourceIndexes.Length; index++)
			results[sourceIndexes[index]] = sortedResults[index];

		return results;
	}

	/// <summary>
	/// Verifies the input contract (no <see langword="null"/> entry, highest-offset-first order, and
	/// the replacement-before-insertion order at one start offset) and rejects the conflicts found
	/// by the shared conflict detector.
	/// </summary>
	/// <remarks>
	/// Two operations at the same start offset are rejected only when the later one is not an
	/// insertion that follows a replacement, because implementations apply the list in order and the
	/// kernel orders a shared-offset replacement before the insertion; multiple insertions at one
	/// offset are accepted. Overlaps are rejected with the same rule set as
	/// <see cref="TextEditKernel.Prepare(ITextSnapshot, IEnumerable{TextEditInput?})"/>, so a batch
	/// prepared by the kernel never fails construction.
	/// </remarks>
	private static void ValidateOperations(IReadOnlyList<TextEditOperation> operations)
	{
		TextEditOperation? previousOperation = null;

		foreach (TextEditOperation? operation in operations)
		{
			if (operation is null)
				throw new ArgumentException("The operations must not contain a null entry.", nameof(operations));

			// An operation that changes nothing cannot conflict with another operation or with the
			// required order, so it is skipped here and by the shared conflict detector.
			if (operation.IsNoOp)
				continue;

			if (previousOperation is null)
			{
				previousOperation = operation;
				continue;
			}

			if (operation.StartOffset > previousOperation.StartOffset)
				throw new ArgumentException("Operations must be supplied in highest-offset-first order.", nameof(operations));

			if (operation.StartOffset == previousOperation.StartOffset
				&& previousOperation.Length == 0 && operation.Length != 0)
			{
				// Duplicate insertions are valid and matched by no conflict rule; this check only enforces
				// the carrier's ordering contract - an insertion must not precede a replacement that
				// starts at the same offset.
				throw new ArgumentException(
					"An insertion must not precede a replacement that starts at the same source offset.",
					nameof(operations));
			}

			previousOperation = operation;
		}

		foreach (TextEditConflict conflict in TextEditConflictDetector.FindConflicts(operations))
			throw new ArgumentException(TextEditConflictMessages.GetExceptionMessage(conflict.Kind), nameof(operations));
	}

	/// <summary>
	/// Maps an offset over operations that are already validated as ordered and non-null, continuing
	/// the descending walk from <paramref name="operationIndex"/> and carrying
	/// <paramref name="cumulativeDelta"/> so several offsets share one pass.
	/// </summary>
	private static int MapOffsetCore(
		int offset,
		IReadOnlyList<TextEditOperation> operations,
		ref int operationIndex,
		ref int cumulativeDelta)
	{
		while (operationIndex >= 0)
		{
			TextEditOperation operation = operations[operationIndex];

			if (operation.StartOffset > offset)
				break;

			if (offset == operation.StartOffset || offset < operation.EndOffset)
			{
				int relativeOffset = offset - operation.StartOffset;
				int normalizedRelativeOffset = Math.Min(relativeOffset, operation.NewText.Length);
				return operation.StartOffset + cumulativeDelta + normalizedRelativeOffset;
			}

			cumulativeDelta += operation.NewText.Length - operation.Length;
			operationIndex--;
		}

		return offset + cumulativeDelta;
	}
}
