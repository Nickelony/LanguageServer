using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Applies prepared operations to an AvalonEdit <see cref="ICSharpCode.AvalonEdit.TextEditor"/> using one undo group
/// and one document update.
/// </summary>
/// <remarks>
/// Operations are applied in the order supplied. Callers must provide the validated, highest-offset-first
/// operations required by <see cref="ITextEditTarget"/>; this adapter does not sort or validate them.
/// </remarks>
public sealed class AvalonEditTextEditTarget : ITextEditTarget, ITextEditTargetVersion
{
	private readonly ICSharpCode.AvalonEdit.TextEditor _editor;
	private long _version;

	/// <summary>
	/// Initializes a new instance of the <see cref="AvalonEditTextEditTarget"/> class.
	/// </summary>
	/// <param name="editor">The AvalonEdit editor to mutate.</param>
	public AvalonEditTextEditTarget(ICSharpCode.AvalonEdit.TextEditor editor)
	{
		ArgumentNullException.ThrowIfNull(editor);
		_editor = editor;
	}

	/// <inheritdoc/>
	public string Text => _editor.Text;

	/// <inheritdoc/>
	public long Version => _version;

	/// <inheritdoc/>
	public void Apply(IReadOnlyList<TextEditOperation> operations)
	{
		ArgumentNullException.ThrowIfNull(operations);

		if (operations.Count == 0)
			return;

		_editor.Document.UndoStack.StartUndoGroup();
		_editor.Document.BeginUpdate();

		try
		{
			foreach (TextEditOperation operation in operations)
				_editor.Document.Replace(operation.StartOffset, operation.Length, operation.NewText);
		}
		finally
		{
			_editor.Document.EndUpdate();
			_editor.Document.UndoStack.EndUndoGroup();
		}

		_version++;
	}
}
