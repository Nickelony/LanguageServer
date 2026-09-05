using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>
/// Applies text operations to an AvalonEdit <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// A non-empty operation list is applied in one undo group and one document update.
/// Callers must supply validated operations in highest-offset-first order, as required by <see cref="ITextEditTarget"/>.
/// This class does not sort or validate them.
/// </remarks>
public sealed class AvalonEditTextEditTarget : ITextEditTarget, ITextEditTargetVersion
{
	private readonly TextEditor _editor;

	/// <summary>
	/// Creates a target backed by <paramref name="editor"/>.
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
	/// Starts at <c>0</c> and increments after each successful non-empty <see cref="Apply"/> call.
	/// Changes made directly to the editor do not update this value.
	/// </remarks>
	public long Version { get; private set; }

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

		Version++;
	}
}
