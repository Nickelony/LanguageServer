using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Navigation;

/// <summary>
/// Executes go-to-definition navigation for AvalonEdit <see cref="TextEditor"/> instances using IntelliSense hover and
/// definition providers.
/// </summary>
public static class TextDefinitionNavigation
{
	/// <summary>
	/// Resolves the symbol at the specified offset through the hover provider and navigates to its definition.
	/// </summary>
	/// <param name="textEditor">The editor to navigate.</param>
	/// <param name="definitionProvider">The provider that resolves definition locations.</param>
	/// <param name="hoverProvider">The provider that resolves the hovered symbol.</param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <returns><see langword="true"/> when a definition location was found and its line was selected; otherwise, <see langword="false"/>.</returns>
	public static bool TryGoToDefinition(
		this TextEditor textEditor,
		ITextDefinitionProvider definitionProvider,
		ITextHoverProvider hoverProvider,
		int offset)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(definitionProvider);
		ArgumentNullException.ThrowIfNull(hoverProvider);

		if (offset < 0 || offset > textEditor.Document.TextLength)
			return false;

		TextHoverInfo? hoverInfo = hoverProvider.GetHoverInfo(new TextHoverRequest(textEditor.Document.Text, offset));

		if (hoverInfo is null || string.IsNullOrWhiteSpace(hoverInfo.SymbolName))
			return false;

		return TryGoToObject(textEditor, definitionProvider, hoverInfo.SymbolName, hoverInfo.Identifier as TextDefinitionDiscriminator);
	}

	/// <summary>
	/// Resolves and navigates to the definition of the supplied symbol name.
	/// </summary>
	/// <param name="textEditor">The editor to navigate.</param>
	/// <param name="definitionProvider">The provider that resolves definition locations.</param>
	/// <param name="objectName">The name of the symbol whose definition is resolved.</param>
	/// <param name="identifyingObject">The optional discriminator for resolving the symbol's definition.</param>
	/// <returns><see langword="true"/> when a definition location was found and its line was selected; otherwise, <see langword="false"/>.</returns>
	public static bool TryGoToObject(
		this TextEditor textEditor,
		ITextDefinitionProvider definitionProvider,
		string? objectName,
		TextDefinitionDiscriminator? identifyingObject)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(definitionProvider);

		if (string.IsNullOrWhiteSpace(objectName))
			return false;

		var request = new TextDefinitionRequest(textEditor.Document.Text, objectName, identifyingObject);
		TextDefinitionLocation? location = definitionProvider.GetDefinition(request);

		if (location is null || location.LineNumber < 1 || location.LineNumber > textEditor.Document.LineCount)
			return false;

		DocumentLine line = textEditor.Document.GetLineByNumber(location.LineNumber);
		textEditor.Focus();
		textEditor.ScrollToLine(location.LineNumber);
		textEditor.SelectLine(line);
		return true;
	}
}
