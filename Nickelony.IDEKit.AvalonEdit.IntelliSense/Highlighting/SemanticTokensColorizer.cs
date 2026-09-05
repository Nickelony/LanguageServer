using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Highlighting;

/// <summary>
/// Describes the resolved visual style for a semantic token.
/// </summary>
/// <param name="Foreground">The foreground brush to use, when one is resolved.</param>
/// <param name="IsBold">Whether the token is rendered bold.</param>
/// <param name="TextDecorations">The text decorations applied to the token, when present.</param>
public readonly record struct SemanticTokenStyle(
	Brush? Foreground,
	bool IsBold,
	TextDecorationCollection? TextDecorations)
{
	/// <summary>
	/// Gets a value indicating whether the style contains a non-null formatting component.
	/// </summary>
	public bool HasFormatting => Foreground is not null || IsBold || TextDecorations is not null;
}

/// <summary>
/// Resolves a semantic token to its visual style. Implementations can use the token type and
/// modifiers with their own theme model, so the colorizer stays style-neutral.
/// </summary>
public interface ISemanticTokenStyleResolver
{
	/// <summary>
	/// Resolves the visual style for the given semantic token.
	/// </summary>
	/// <param name="token">The semantic token to resolve.</param>
	/// <returns>The resolved style.</returns>
	SemanticTokenStyle Resolve(TextSemanticToken token);
}

/// <summary>
/// Applies resolved semantic-token styles while AvalonEdit renders the text view. Each token is
/// assigned to the line containing its start offset, then clipped to the current document and that
/// rendered line.
/// </summary>
public sealed class SemanticTokensColorizer : DocumentColorizingTransformer
{
	private static readonly IReadOnlyDictionary<int, IReadOnlyList<StyledSemanticToken>> s_emptyTokensByLine =
		new Dictionary<int, IReadOnlyList<StyledSemanticToken>>();

	private readonly TextView _textView;
	private readonly ISemanticTokenStyleResolver _styleResolver;

	private IReadOnlyList<TextSemanticToken> _rawTokens = [];

	private IReadOnlyDictionary<int, IReadOnlyList<StyledSemanticToken>> _tokensByLine = s_emptyTokensByLine;

	/// <summary>
	/// Initializes a new instance of the <see cref="SemanticTokensColorizer"/> class.
	/// </summary>
	/// <param name="textView">The text view that will be redrawn when semantic styles change.</param>
	/// <param name="styleResolver">The resolver that maps tokens to visual styles.</param>
	public SemanticTokensColorizer(TextView textView, ISemanticTokenStyleResolver styleResolver)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(styleResolver);

		_textView = textView;
		_styleResolver = styleResolver;
	}

	/// <summary>
	/// Replaces the semantic tokens currently applied to the text view.
	/// </summary>
	/// <param name="tokens">The semantic tokens to render; an empty collection clears the current tokens.</param>
	public void SetTokens(IReadOnlyList<TextSemanticToken> tokens)
	{
		ArgumentNullException.ThrowIfNull(tokens);

		if (tokens.Count == 0)
		{
			ClearTokens();
			return;
		}

		_rawTokens = tokens;
		_tokensByLine = BuildStyledMap(tokens);
		_textView.Redraw();
	}

	/// <summary>
	/// Rebuilds the styled semantic token cache from the most recent token set. Call this after the
	/// style resolver's configuration changes so the cached styles reflect the new configuration. Tokens whose start
	/// offset is outside the current document are ignored, and tokens extending past their start line are clipped to
	/// that line.
	/// </summary>
	public void Rebuild()
	{
		_tokensByLine = BuildStyledMap(_rawTokens);
		_textView.Redraw();
	}

	/// <summary>
	/// Removes all semantic token styling from the text view.
	/// </summary>
	public void ClearTokens()
	{
		if (_tokensByLine.Count == 0 && _rawTokens.Count == 0)
			return;

		_rawTokens = [];
		_tokensByLine = s_emptyTokensByLine;
		_textView.Redraw();
	}

	/// <inheritdoc/>
	protected override void ColorizeLine(DocumentLine line)
	{
		if (!_tokensByLine.TryGetValue(line.LineNumber - 1, out IReadOnlyList<StyledSemanticToken>? tokens))
			return;

		int lineLength = line.Length;

		for (int i = 0; i < tokens.Count; i++)
		{
			StyledSemanticToken styled = tokens[i];
			int startIndex = Math.Max(0, Math.Min(styled.Character, lineLength));
			int endIndex = Math.Max(startIndex, Math.Min(styled.Character + styled.Length, lineLength));

			if (endIndex <= startIndex)
				continue;

			SemanticTokenStyle style = styled.Style;
			ChangeLinePart(line.Offset + startIndex, line.Offset + endIndex, element => ApplyStyle(element, style));
		}
	}

	private IReadOnlyDictionary<int, IReadOnlyList<StyledSemanticToken>> BuildStyledMap(
		IReadOnlyList<TextSemanticToken> tokens)
	{
		if (tokens.Count == 0)
			return s_emptyTokensByLine;

		TextDocument? document = _textView.Document;

		if (document is null)
			return s_emptyTokensByLine;

		var grouped = new Dictionary<int, List<StyledSemanticToken>>();

		for (int i = 0; i < tokens.Count; i++)
		{
			TextSemanticToken token = tokens[i];
			SemanticTokenStyle style = _styleResolver.Resolve(token);

			if (!style.HasFormatting)
				continue;

			(int line, int character, int length) = ToLineColumn(token, document);

			if (line < 0 || length <= 0)
				continue;

			if (!grouped.TryGetValue(line, out List<StyledSemanticToken>? lineTokens))
			{
				lineTokens = [];
				grouped[line] = lineTokens;
			}

			lineTokens.Add(new StyledSemanticToken(character, length, style));
		}

		var frozen = new Dictionary<int, IReadOnlyList<StyledSemanticToken>>(grouped.Count);

		foreach (KeyValuePair<int, List<StyledSemanticToken>> pair in grouped)
		{
			pair.Value.Sort(
				static (left, right) =>
				{
					int characterComparison = left.Character.CompareTo(right.Character);
					return characterComparison != 0 ? characterComparison : left.Length.CompareTo(right.Length);
				});

			frozen[pair.Key] = pair.Value;
		}

		return frozen;
	}

	private static (int Line, int Character, int Length) ToLineColumn(TextSemanticToken token, TextDocument document)
	{
		int startOffset = Math.Max(0, Math.Min(token.Range.Offset, document.TextLength));
		int endOffset = Math.Max(startOffset, Math.Min(token.Range.EndOffset, document.TextLength));

		if (startOffset >= document.TextLength)
			return (-1, 0, 0);

		DocumentLine line = document.GetLineByOffset(startOffset);
		int character = startOffset - line.Offset;
		int lineEnd = Math.Min(line.EndOffset, document.TextLength);
		int length = Math.Max(0, Math.Min(endOffset, lineEnd) - startOffset);

		return (line.LineNumber - 1, character, length);
	}

	private static void ApplyStyle(VisualLineElement element, SemanticTokenStyle style)
	{
		VisualLineElementTextRunProperties properties = element.TextRunProperties;

		if (style.Foreground is not null)
			properties.SetForegroundBrush(style.Foreground);

		if (style.IsBold)
		{
			Typeface typeface = properties.Typeface;
			properties.SetTypeface(new Typeface(typeface.FontFamily, typeface.Style, FontWeights.Bold, typeface.Stretch));
		}

		if (style.TextDecorations is not null)
			properties.SetTextDecorations(style.TextDecorations);
	}

	private readonly struct StyledSemanticToken(int character, int length, SemanticTokenStyle style)
	{
		public int Character { get; } = character;
		public int Length { get; } = length;
		public SemanticTokenStyle Style { get; } = style;
	}
}
