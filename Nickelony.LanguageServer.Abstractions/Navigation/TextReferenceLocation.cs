using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Identifies a symbol reference in a source file.
/// </summary>
/// <remarks>
/// <see cref="Range"/> uses zero-based line and character positions in LSP units: lines count line
/// breaks, characters count UTF-16 code units within the line, and a tab counts as a single code
/// unit. Range values are stored as supplied.
/// </remarks>
public sealed record TextReferenceLocation
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextReferenceLocation"/> record.
	/// </summary>
	/// <param name="filePath">The file containing the reference.</param>
	/// <param name="range">The zero-based range of the reference.</param>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	public TextReferenceLocation(string filePath, TextPositionRange range)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		FilePath = filePath;
		Range = range;
	}

	/// <summary>
	/// Gets the file containing the reference.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the zero-based range of the reference.
	/// </summary>
	public TextPositionRange Range { get; }
}
