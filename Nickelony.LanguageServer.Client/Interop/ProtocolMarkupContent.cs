namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents extracted markup content together with whether it should be treated as Markdown.
/// </summary>
public readonly record struct ProtocolMarkupContent
{
	private readonly string? _text;

	/// <summary>
	/// Initializes a new instance of the <see cref="ProtocolMarkupContent"/> struct.
	/// </summary>
	/// <param name="text">The extracted text value.</param>
	/// <param name="isMarkdown">Whether the extracted text should be treated as Markdown.</param>
	public ProtocolMarkupContent(string? text, bool isMarkdown)
	{
		_text = text ?? string.Empty;
		IsMarkdown = isMarkdown;
	}

	/// <summary>
	/// Gets the extracted text.
	/// The <see langword="default"/> value carries no text and yields an empty string.
	/// </summary>
	public string Text => _text ?? string.Empty;

	/// <summary>
	/// Gets a value indicating whether the content should be treated as Markdown.
	/// </summary>
	public bool IsMarkdown { get; }
}
