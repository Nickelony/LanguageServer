using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Describes the insert and replace ranges supplied by a completion item using zero-based UTF-16
/// document offsets.
/// </summary>
/// <remarks>
/// Ranges use neutral document offsets rather than protocol line and column coordinates so the
/// payload can cross provider and editor boundaries without protocol coupling. Providers that
/// receive line and column coordinates translate them to offsets against the request-time document
/// snapshot before constructing an item.
/// </remarks>
/// <param name="InsertRange">The insert range for the completion edit.</param>
/// <param name="ReplaceRange">
/// The optional replace range; falls back to <paramref name="InsertRange"/> when <see langword="null"/>.
/// </param>
public readonly record struct TextCompletionTextEdit(TextRange InsertRange, TextRange? ReplaceRange = null)
{
	/// <summary>
	/// Gets the effective replacement range, falling back to <see cref="InsertRange"/>.
	/// </summary>
	public TextRange ReplacementRange => ReplaceRange ?? InsertRange;
}
