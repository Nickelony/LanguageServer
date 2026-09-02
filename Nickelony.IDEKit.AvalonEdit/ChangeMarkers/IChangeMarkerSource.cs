using ICSharpCode.AvalonEdit.Document;

namespace Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

/// <summary>
/// Provides the set of document lines that a <see cref="ChangeMarkerMargin"/> should mark.
/// </summary>
public interface IChangeMarkerSource
{
	/// <summary>
	/// Gets the lines that should be marked, sorted by line number.
	/// </summary>
	IReadOnlyList<DocumentLine> GetMarkedLines();
}
