using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using TextMateSharp.Model;

namespace Nickelony.IDEKit.AvalonEdit.Extras.TextMate.Highlighting;

/// <summary>
/// Applies styles resolved from TextMate tokens to AvalonEdit document lines as they are rendered.
/// The <see cref="TextMateThemeStyleResolver"/> translates each token's scopes into visual formatting.
/// </summary>
public sealed class TextMateColorizingTransformer : DocumentColorizingTransformer, IDisposable, IModelTokensChangedListener
{
	private readonly TextView _textView;
	private readonly TMModel _model;
	private readonly TextMateThemeStyleResolver _styleResolver;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateColorizingTransformer"/> class.
	/// </summary>
	/// <param name="textView">The text view to redraw when tokens change.</param>
	/// <param name="model">The TextMate model providing per-line tokens.</param>
	/// <param name="styleResolver">The resolver translating token scopes into visual styles.</param>
	public TextMateColorizingTransformer(TextView textView, TMModel model, TextMateThemeStyleResolver styleResolver)
	{
		ArgumentNullException.ThrowIfNull(textView);
		_textView = textView;
		ArgumentNullException.ThrowIfNull(model);
		_model = model;
		ArgumentNullException.ThrowIfNull(styleResolver);
		_styleResolver = styleResolver;

		_model.AddModelTokensChangedListener(this);
	}

	/// <inheritdoc/>
	protected override void ColorizeLine(DocumentLine line)
	{
		if (_isDisposed || line is null)
			return;

		int lineIndex = Math.Max(0, line.LineNumber - 1);
		List<TMToken> tokens = _model.GetLineTokens(lineIndex);

		if (tokens is null || _model.IsLineInvalid(lineIndex))
		{
			_model.ForceTokenization(lineIndex);
			tokens = _model.GetLineTokens(lineIndex);
		}

		if (tokens is null || tokens.Count == 0)
			return;

		int lineLength = line.Length;

		for (int i = 0; i < tokens.Count; i++)
		{
			TMToken token = tokens[i];
			int startIndex = ClampToLine(token.StartIndex, lineLength);
			int endIndex = i + 1 < tokens.Count
				? ClampToLine(tokens[i + 1].StartIndex, lineLength)
				: lineLength;

			if (endIndex <= startIndex)
				continue;

			TextMateHighlightingStyle style = _styleResolver.Resolve(token.Scopes);

			if (!style.HasFormatting)
				continue;

			int startOffset = line.Offset + startIndex;
			int endOffset = line.Offset + endIndex;

			ChangeLinePart(startOffset, endOffset, element => ApplyStyle(element, style));
		}
	}

	/// <summary>
	/// Stops listening for token changes from the TextMate model.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_model.RemoveModelTokensChangedListener(this);
	}

	void IModelTokensChangedListener.ModelTokensChanged(ModelTokensChangedEvent e)
	{
		if (_isDisposed)
			return;

		// Queue the redraw so it does not run while AvalonEdit is building visual lines.
		_textView.Dispatcher.BeginInvoke(new Action(() =>
		{
			if (!_isDisposed)
				_textView.Redraw();
		}));
	}

	private static int ClampToLine(int index, int lineLength)
		=> Math.Max(0, Math.Min(index, lineLength));

	private static void ApplyStyle(VisualLineElement element, TextMateHighlightingStyle style)
	{
		VisualLineElementTextRunProperties properties = element.TextRunProperties;

		if (style.Foreground is not null)
			properties.SetForegroundBrush(style.Foreground);

		if (style.IsBold || style.IsItalic)
			properties.SetTypeface(style.CreateTypeface(properties.Typeface));

		if (style.TextDecorations is not null)
			properties.SetTextDecorations(style.TextDecorations);
	}
}
