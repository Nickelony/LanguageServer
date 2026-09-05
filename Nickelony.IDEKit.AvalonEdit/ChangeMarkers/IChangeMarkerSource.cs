using ICSharpCode.AvalonEdit.Document;

namespace Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

/// <summary>
/// Provides document lines for a <see cref="ChangeMarkerMargin"/> to mark.
/// </summary>
public interface IChangeMarkerSource
{
	/// <summary>
	/// Gets the document lines to mark, sorted by line number (ascending).
	/// </summary>
	IReadOnlyList<DocumentLine> GetMarkedLines();
}
