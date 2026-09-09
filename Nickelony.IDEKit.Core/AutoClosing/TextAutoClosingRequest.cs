using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Carries the inputs of an auto-closing action resolution.
/// </summary>
/// <remarks>
/// A request groups the snapshot, the caret, the entered text, the configuration, and the optional
/// selection and provenance inputs, so a resolver call stays one value wide. A <see langword="default"/>
/// instance carries a <see langword="null"/> snapshot, input text, and options, which
/// <see cref="TextAutoClosingResolver.TryResolveAction(in TextAutoClosingRequest, out TextAutoClosingAction)"/>
/// rejects.
/// </remarks>
/// <param name="Snapshot">The snapshot of the text containing the caret.</param>
/// <param name="CaretOffset">
/// The zero-based caret offset. Offsets outside the snapshot are clamped to its bounds.
/// </param>
/// <param name="InputText">The text being entered.</param>
/// <param name="Options">The auto-closing configuration.</param>
/// <param name="IsWrappingSelection">
/// Whether a non-empty selection is being wrapped; a wrapping request resolves only an insert,
/// never a skip.
/// </param>
/// <param name="IsTrackedClosingText">
/// The insertion-tracking callback that reports whether tracked auto-closing inserted the closing
/// text starting at the supplied offset, or <see langword="null"/> when nothing is tracked.
/// </param>
public readonly record struct TextAutoClosingRequest(
	ITextSnapshot Snapshot,
	int CaretOffset,
	string InputText,
	TextAutoClosingOptions Options,
	bool IsWrappingSelection = false,
	Func<int, bool>? IsTrackedClosingText = null);
