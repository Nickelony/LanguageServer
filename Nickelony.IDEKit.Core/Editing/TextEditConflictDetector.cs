using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Detects conflicts between text edit operations with one shared rule set, so the preparation
/// kernel (diagnostics) and <see cref="PreparedTextEdits"/> (constructor validation) cannot drift.
/// </summary>
internal static class TextEditConflictDetector
{
	/// <summary>
	/// Returns the conflicts in a batch: pairwise range conflicts in a deterministic order.
	/// Operations that change nothing are ignored.
	/// </summary>
	/// <remarks>
	/// Pairwise range conflicts are ordered by the left (replacement) operation under the candidate
	/// order and then by the right (triggering) operation, so they are not globally sorted by the
	/// triggering edit. The order is total while source indexes are unique per batch, as the kernel
	/// guarantees; a hand-built batch that reuses a source index leaves the relative order of the
	/// operations that carry it unspecified.
	/// </remarks>
	internal static List<TextEditConflict> FindConflicts(IReadOnlyList<TextEditOperation> operations)
	{
		var conflicts = new List<TextEditConflict>();
		var candidates = new List<TextEditOperation>(operations.Count);

		foreach (TextEditOperation operation in operations)
		{
			if (operation.IsNoOp)
				continue;

			candidates.Add(operation);
		}

		candidates.Sort(static (left, right) => TextEditOrderKey.From(left).CompareTo(TextEditOrderKey.From(right)));

		for (int leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
		{
			TextEditOperation left = candidates[leftIndex];

			// Only a replacement can start a conflicting pair. Candidates are sorted by start offset,
			// so an insertion on the left can only meet candidates at its own offset, and the
			// strict-inside test below never matches those (touching a replacement boundary is
			// accepted); skipping insertions keeps a batch of same-offset insertions linear instead
			// of quadratic.
			if (left.Length == 0)
				continue;

			for (int rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
			{
				TextEditOperation right = candidates[rightIndex];

				// Candidates are sorted by start offset, so once the right candidate starts at or
				// after the left candidate's end, no later candidate can conflict with the left one.
				if (right.StartOffset >= left.EndOffset)
					break;

				if (right.Length == 0)
				{
					// An insertion strictly inside the replacement range conflicts; an insertion
					// touching either boundary is accepted.
					if (right.StartOffset > left.StartOffset)
					{
						conflicts.Add(new TextEditConflict(
							TextEditConflictKind.InsertionInsideReplacement,
							right.SourceIndex,
							left.SourceIndex));
					}

					continue;
				}

				conflicts.Add(new TextEditConflict(
					TextEditConflictKind.ReplacementOverlap,
					right.SourceIndex,
					left.SourceIndex));
			}
		}

		return conflicts;
	}
}
