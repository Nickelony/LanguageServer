using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Provides a minimal completion item for tests that never exercise completion insertion; the item can
/// opt into preselection through <see cref="IPreselectedCompletionData"/> and supply an image for
/// measurement tests.
/// </summary>
/// <param name="text">The item text.</param>
/// <param name="description">The item's synchronous description, or <see langword="null"/> for none.</param>
/// <param name="image">The item's image, or <see langword="null"/> when the item supplies none.</param>
internal sealed class TestCompletionData(string text, object? description = null, ImageSource? image = null) : ICompletionData, IPreselectedCompletionData
{
	/// <inheritdoc/>
	public ImageSource? Image => image;

	/// <inheritdoc/>
	public string Text => text;

	/// <inheritdoc/>
	public object Content => text;

	/// <inheritdoc/>
	public object Description => description!;

	/// <inheritdoc/>
	public double Priority => 0.0;

	/// <inheritdoc/>
	public bool IsPreselected { get; set; }

	/// <inheritdoc/>
	public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
		=> throw new NotSupportedException("Completion insertion is not exercised by these tests.");
}
