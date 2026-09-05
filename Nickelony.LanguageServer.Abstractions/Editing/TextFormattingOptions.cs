namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Specifies indentation preferences for document formatting.
/// </summary>
public sealed class TextFormattingOptions
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextFormattingOptions"/> class.
	/// </summary>
	/// <param name="tabSize">The indentation width to use.</param>
	/// <param name="insertSpaces"><see langword="true"/> to indent with spaces; otherwise, tabs.</param>
	/// <remarks>A value less than one for <paramref name="tabSize"/> uses the default width of <c>4</c>.</remarks>
	public TextFormattingOptions(int tabSize, bool insertSpaces)
	{
		TabSize = tabSize > 0 ? tabSize : 4;
		InsertSpaces = insertSpaces;
	}

	/// <summary>
	/// Gets the indentation width.
	/// </summary>
	public int TabSize { get; }

	/// <summary>
	/// Gets a value indicating whether indentation uses spaces instead of tabs.
	/// </summary>
	public bool InsertSpaces { get; }
}
