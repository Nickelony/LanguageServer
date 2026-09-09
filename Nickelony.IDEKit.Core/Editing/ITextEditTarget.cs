namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Applies validated text operations to a host-owned document target.
/// </summary>
/// <remarks>
/// <para>
/// The batch is a <see cref="PreparedTextEdits"/> instance, so its operations are already ordered
/// from highest to lowest source offset and contain no <see langword="null"/> replacement text.
/// Implementations may apply the operations in list order without sorting them.
/// </para>
/// <para>
/// Document-level failures (for example an out-of-range offset) may still surface while the batch is
/// applied, after earlier operations were already applied.
/// </para>
/// <para>
/// The operations carry offsets that are only valid for the content they were prepared against.
/// The workflow is: capture <see cref="ITextEditTargetVersion.Version"/> from the target, read
/// <see cref="Text"/>, prepare a batch from that content, then compare the captured version before
/// calling <see cref="Apply(PreparedTextEdits)"/>; a changed version means the batch is stale and
/// must be prepared again. That workflow is check-then-act: on a target whose content can change
/// between the comparison and the apply, prefer
/// <see cref="IVersionedTextEditTarget.TryApply(PreparedTextEdits, long)"/>, which performs the
/// check and the apply as one step inside the target.
/// </para>
/// </remarks>
public interface ITextEditTarget
{
	/// <summary>
	/// Gets the current target text.
	/// </summary>
	string Text { get; }

	/// <summary>
	/// Applies the validated edits.
	/// </summary>
	/// <param name="edits">The validated edits to apply, ordered from highest to lowest source offset.</param>
	/// <exception cref="ArgumentNullException"><paramref name="edits"/> is <see langword="null"/>.</exception>
	void Apply(PreparedTextEdits edits);
}
