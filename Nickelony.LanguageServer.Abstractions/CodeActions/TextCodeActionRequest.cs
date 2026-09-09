using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a request for the code actions available for a document range.
/// </summary>
/// <remarks>
/// The range is stored as supplied otherwise and is not required to be ordered.
/// </remarks>
public sealed record TextCodeActionRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionRequest"/> record.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="range">The zero-based range to get code actions for. Negative line and character values are changed to zero.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	public TextCodeActionRequest(string filePath, string documentText, TextPositionRange range)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		FilePath = filePath;
		DocumentText = documentText;
		Range = new TextPositionRange(Clamp(range.Start), Clamp(range.End));
	}

	/// <summary>
	/// Gets the current document file path.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the current document content.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the zero-based range to get code actions for.
	/// </summary>
	public TextPositionRange Range { get; }

	private static TextPosition Clamp(TextPosition position)
		=> new(Math.Max(0, position.Line), Math.Max(0, position.Character));
}
