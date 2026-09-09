using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// A line-status source test double with fixed line numbers that also supports bookmark toggling
/// and optional change notifications, so the bookmark and change-marker margin tests share one double.
/// </summary>
internal sealed class FixedLineStatusSource : IBookmarkSource, IChangeNotificationSource
{
	private readonly Func<TextDocument> _documentProvider;
	private readonly List<int> _lineNumbers;

	/// <summary>
	/// Initializes a new instance of the <see cref="FixedLineStatusSource"/> class.
	/// </summary>
	/// <param name="documentProvider">Provides the document the fixed line numbers resolve against.</param>
	/// <param name="lineNumbers">The one-based line numbers the source reports.</param>
	/// <param name="notifiesChanges">
	/// Whether <see cref="ToggleBookmark"/> raises <see cref="Changed"/>. Defaults to <see langword="true"/>.
	/// </param>
	public FixedLineStatusSource(
		Func<TextDocument> documentProvider,
		IReadOnlyList<int> lineNumbers,
		bool notifiesChanges = true)
	{
		_documentProvider = documentProvider;
		_lineNumbers = [.. lineNumbers];
		NotifiesChanges = notifiesChanges;
	}

	/// <summary>
	/// Gets a value indicating whether this source raises <see cref="Changed"/> when its bookmarks change.
	/// </summary>
	public bool NotifiesChanges { get; }

	/// <summary>
	/// Gets the number of times <see cref="ToggleBookmark"/> was called.
	/// </summary>
	public int ToggleCalls { get; private set; }

	/// <summary>
	/// Gets the line number the last <see cref="ToggleBookmark"/> call resolved.
	/// </summary>
	public int ToggledLineNumber { get; private set; }

	/// <inheritdoc/>
	public event EventHandler? Changed;

	/// <inheritdoc/>
	public IReadOnlyList<int> GetMarkedLineNumbers()
	{
		TextDocument document = _documentProvider();

		// Out-of-range configuration is dropped instead of being reported by the render pass, mirroring
		// how BookmarkCoordinator.Restore ignores line numbers outside the document.
		return [.. _lineNumbers.Where(lineNumber => lineNumber >= 1 && lineNumber <= document.LineCount)];
	}

	/// <inheritdoc/>
	public void ToggleBookmark(int offset)
	{
		ToggleCalls++;
		ToggledLineNumber = _documentProvider().GetLineByOffset(offset).LineNumber;

		if (NotifiesChanges)
			Changed?.Invoke(this, EventArgs.Empty);
	}
}
