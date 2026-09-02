namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Identifies a deterministic text-edit preparation failure.
/// </summary>
/// <param name="Code">The validation rule that rejected the edit batch.</param>
/// <param name="EditIndex">The source index of the rejected edit.</param>
/// <param name="RelatedEditIndex">The source index of the related edit, or <c>-1</c>.</param>
/// <param name="Message">The diagnostic message.</param>
public sealed record TextEditPreparationDiagnostic(
	TextEditPreparationDiagnosticCode Code,
	int EditIndex,
	int RelatedEditIndex,
	string Message);

/// <summary>
/// Identifies the validation rule that rejected a text edit.
/// </summary>
public enum TextEditPreparationDiagnosticCode
{
	/// <summary>
	/// The range is outside the snapshot.
	/// </summary>
	InvalidRange,

	/// <summary>
	/// Two non-empty edit ranges overlap.
	/// </summary>
	OverlappingRanges,

	/// <summary>
	/// Two insertions target the same source offset.
	/// </summary>
	DuplicateInsertion,

	/// <summary>
	/// An insertion targets the interior of a replacement range.
	/// </summary>
	InsertionReplacementIntersection
}
