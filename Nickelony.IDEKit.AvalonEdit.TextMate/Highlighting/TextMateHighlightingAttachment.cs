using ICSharpCode.AvalonEdit;
using Microsoft.Extensions.Logging;
using TextMateSharp.Grammars;
using TextMateSharp.Model;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;

/// <summary>
/// Wires a TextMate grammar, model, and colorizing transformer to an editor and owns their disposal.
/// </summary>
/// <remarks>
/// <para>
/// The attachment creates the quartet that TextMate highlighting needs - the incremental
/// <see cref="TextMateDocumentLineList"/>, the <see cref="TMModel"/>, the
/// <see cref="TextMateThemeStyleResolver"/>, and the <see cref="TextMateColorizingTransformer"/> -
/// installs the transformer on the editor's <c>LineTransformers</c>, and starts tokenization with the
/// supplied grammar. It must be created and disposed on the UI thread that owns the editor.
/// </para>
/// <para>
/// Disposing reverses the wiring in the safe order: the transformer is removed from the view and
/// detached from the model first, and the model is disposed afterwards, which also disposes the line
/// list. Dispose the attachment when the editor is unloaded; a disposed attachment leaves the
/// remaining tokenization untouched.
/// </para>
/// </remarks>
public sealed class TextMateHighlightingAttachment : IDisposable
{
	private readonly TextEditor _editor;
	private readonly TextMateColorizingTransformer _transformer;
	private readonly TMModel _model;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateHighlightingAttachment"/> class and starts
	/// tokenizing the editor's document.
	/// </summary>
	/// <param name="editor">The editor whose document is tokenized and whose view is colorized.</param>
	/// <param name="grammar">The grammar used to tokenize the document.</param>
	/// <param name="theme">The token theme that resolves token scopes into visual styles.</param>
	/// <param name="logger">
	/// An optional logger used to report invalid foreground colors, unsupported selectors, and
	/// unrecognized font style traits.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/>, <paramref name="grammar"/>, or <paramref name="theme"/> is
	/// <see langword="null"/>.
	/// </exception>
	public TextMateHighlightingAttachment(TextEditor editor, IGrammar grammar, TextMateTokenTheme theme, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(grammar);
		ArgumentNullException.ThrowIfNull(theme);

		_editor = editor;
		LineList = new TextMateDocumentLineList(editor.Document);
		_model = new TMModel(LineList);

		var styleResolver = new TextMateThemeStyleResolver(theme, logger);
		_transformer = new TextMateColorizingTransformer(editor.TextArea.TextView, _model, styleResolver);

		editor.TextArea.TextView.LineTransformers.Add(_transformer);
		_model.SetGrammar(grammar);
	}

	/// <summary>Gets the incremental line list that feeds the model.</summary>
	public TextMateDocumentLineList LineList { get; }

	/// <summary>Gets the TextMate model that tokenizes the document.</summary>
	public TMModel Model => _model;

	/// <summary>Gets the transformer installed on the editor's view.</summary>
	public TextMateColorizingTransformer Transformer => _transformer;

	/// <summary>
	/// Removes the transformer from the editor's view, detaches it from the model, and disposes the
	/// model (which also disposes the line list). The method is idempotent.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;

		_editor.TextArea.TextView.LineTransformers.Remove(_transformer);
		_transformer.Dispose();
		_model.Dispose();
	}
}
