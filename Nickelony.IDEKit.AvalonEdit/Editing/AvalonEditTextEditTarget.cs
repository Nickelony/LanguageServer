using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Applies prepared text-edit batches to the document of an AvalonEdit <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// <para>
/// A batch that contains at least one operation that changes text is applied inside one document update
/// and one undo step; a batch whose operations all change nothing is skipped entirely.
/// The per-change <see cref="TextDocument.Changed"/> event is raised once per operation, while the
/// aggregate events (<see cref="TextDocument.TextChanged"/> and property changes) are raised once when
/// the update ends.
/// </para>
/// <para>
/// The batch is a <see cref="PreparedTextEdits"/> instance, so it is validated when it is
/// constructed and cannot silently corrupt the document through a wrong order or a
/// <see langword="null"/> replacement text. A document-level failure (for example an out-of-range
/// offset) still surfaces while the batch is applied, and earlier operations may already be
/// applied.
/// </para>
/// </remarks>
public sealed class AvalonEditTextEditTarget : IVersionedTextEditTarget
{
	private readonly TextEditor _editor;

	private TextDocument? _trackedDocument;
	private long _version;

	/// <summary>
	/// Initializes a new instance of the <see cref="AvalonEditTextEditTarget"/> class.
	/// </summary>
	/// <param name="editor">The editor whose document this target updates.</param>
	/// <exception cref="ArgumentNullException"><paramref name="editor"/> is <see langword="null"/>.</exception>
	public AvalonEditTextEditTarget(TextEditor editor)
	{
		ArgumentNullException.ThrowIfNull(editor);
		_editor = editor;
	}

	/// <inheritdoc/>
	public string Text => _editor.Text;

	/// <inheritdoc/>
	/// <remarks>
	/// The stamp advances on every change to the editor's current document (made directly, through this
	/// target, through another target, or by an undo) and when a read first observes that the editor has
	/// swapped to a different document, because the subscription to the document's change event is
	/// established on the first read. The
	/// documented workflow (capture the stamp, read the content, prepare a batch, compare the stamp, apply)
	/// therefore detects concurrent edits from every path. The subscription is established on the
	/// editor's thread, like every other access to the target.
	/// </remarks>
	public long Version
	{
		get
		{
			EnsureDocumentSubscription(_editor.Document);
			return Interlocked.Read(ref _version);
		}
	}

	private void EnsureDocumentSubscription(TextDocument document)
	{
		if (ReferenceEquals(_trackedDocument, document))
			return;

		if (_trackedDocument is not null)
		{
			_trackedDocument.Changed -= Document_Changed;

			// A document swap invalidates any batch prepared against the previous document.
			Interlocked.Increment(ref _version);
		}

		_trackedDocument = document;
		document.Changed += Document_Changed;
	}

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
		=> Interlocked.Increment(ref _version);

	/// <inheritdoc/>
	/// <remarks>
	/// Operations that would change nothing (<see cref="TextEditOperation.IsNoOp"/>) are skipped, so an
	/// all-no-op batch changes neither the document nor its undo stack. A document-level failure (for
	/// example an out-of-range offset) still surfaces while the batch is applied, and earlier operations
	/// may already be applied; because <see cref="Version"/> follows the document, the stamp is correct in
	/// that case as well.
	/// </remarks>
	public void Apply(PreparedTextEdits edits)
	{
		ArgumentNullException.ThrowIfNull(edits);

		if (edits.Operations.Count == 0)
			return;

		bool hasApplicableOperation = false;

		foreach (TextEditOperation operation in edits.Operations)
		{
			if (!operation.IsNoOp)
			{
				hasApplicableOperation = true;
				break;
			}
		}

		if (!hasApplicableOperation)
			return;

		// The document is resolved once per call: a Changed handler that swaps the editor's document must
		// not leave this document's update open or apply later operations to the new document. BeginUpdate
		// also groups the changes into one undo step, so no explicit undo group is needed.
		TextDocument document = _editor.Document;

		document.BeginUpdate();

		try
		{
			foreach (TextEditOperation operation in edits.Operations)
			{
				if (operation.IsNoOp)
					continue;

				document.Replace(operation.StartOffset, operation.Length, operation.NewText);
			}
		}
		finally
		{
			document.EndUpdate();
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The version check and the apply run in one call on the editor's thread, which is the only
	/// thread that may touch the target, so a batch prepared against a stale stamp can never be
	/// applied. The stamp advances when the document changes, so a successful apply also advances it.
	/// </remarks>
	public bool TryApply(PreparedTextEdits edits, long expectedVersion)
	{
		ArgumentNullException.ThrowIfNull(edits);

		if (Version != expectedVersion)
			return false;

		Apply(edits);
		return true;
	}
}
