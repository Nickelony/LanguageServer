namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>
/// The search direction used to locate matches relative to the current selection.
/// </summary>
public enum FindingOrder
{
	/// <summary>
	/// Searches upward, toward the start of the document.
	/// </summary>
	Previous,

	/// <summary>
	/// Searches downward, toward the end of the document.
	/// </summary>
	Next
}
