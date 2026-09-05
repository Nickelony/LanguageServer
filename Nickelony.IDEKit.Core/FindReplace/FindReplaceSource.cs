namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>
/// A named collection of find-and-replace matches for one document.
/// </summary>
public sealed class FindReplaceSource : List<FindReplaceItem>
{
	/// <summary>
	/// Gets or sets the display name of the document the matches belong to.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Creates an empty find-and-replace source without a name.
	/// </summary>
	public FindReplaceSource()
		=> Name = string.Empty;

	/// <summary>
	/// Creates a find-and-replace source for the named document.
	/// </summary>
	/// <param name="name">The display name of the document.</param>
	public FindReplaceSource(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		Name = name;
	}
}
