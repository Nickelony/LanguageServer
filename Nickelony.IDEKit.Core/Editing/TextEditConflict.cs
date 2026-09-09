namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Describes one conflict between two edits of a batch.
/// </summary>
/// <param name="Kind">The rule that the batch violates.</param>
/// <param name="SourceIndex">The source index of the edit that triggered the conflict.</param>
/// <param name="RelatedSourceIndex">The source index of the edit it conflicts with.</param>
internal readonly record struct TextEditConflict(
	TextEditConflictKind Kind,
	int SourceIndex,
	int RelatedSourceIndex);
