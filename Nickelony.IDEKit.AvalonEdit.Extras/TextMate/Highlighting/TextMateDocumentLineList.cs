using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using TextMateSharp.Grammars;
using TextMateSharp.Model;

namespace Nickelony.IDEKit.AvalonEdit.Extras.TextMate.Highlighting;

/// <summary>
/// Provides TextMateSharp with a live, zero-based view of an AvalonEdit <see cref="TextDocument"/>'s lines.
/// Each line includes its terminator when present, and the view is updated when the document changes.
/// </summary>
public sealed class TextMateDocumentLineList : AbstractLineList
{
	private readonly TextDocument _document;
	private readonly object _syncRoot = new();
	private readonly List<string> _lineTexts = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateDocumentLineList"/> class.
	/// </summary>
	/// <param name="document">The document whose lines are tracked.</param>
	/// <remarks>Dispose the instance to stop tracking document changes when it is no longer being tokenized.</remarks>
	public TextMateDocumentLineList(TextDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		_document = document;

		InitializeSnapshot();

		_document.Changed += Document_Changed;
	}

	/// <inheritdoc/>
	public override void UpdateLine(int lineIndex)
		=> InvalidateLineRange(lineIndex, Math.Max(lineIndex, GetNumberOfLines() - 1));

	/// <inheritdoc/>
	public override int GetNumberOfLines()
	{
		lock (_syncRoot)
			return _lineTexts.Count;
	}

	/// <inheritdoc/>
	public override LineText GetLineTextIncludingTerminators(int lineIndex)
	{
		lock (_syncRoot)
		{
			if (lineIndex < 0 || lineIndex >= _lineTexts.Count)
				return new LineText(string.Empty);

			return new LineText(_lineTexts[lineIndex]);
		}
	}

	/// <inheritdoc/>
	public override int GetLineLength(int lineIndex)
	{
		lock (_syncRoot)
		{
			if (lineIndex < 0 || lineIndex >= _lineTexts.Count)
				return 0;

			return _lineTexts[lineIndex].Length;
		}
	}

	/// <inheritdoc/>
	public override void Dispose()
		=> _document.Changed -= Document_Changed;

	private void Document_Changed(object? sender, DocumentChangeEventArgs e)
	{
		(int startLineIndex, int removedLineCount, int insertedLineCount) = GetChangeInfo(_document, e);
		int lineDelta = insertedLineCount - removedLineCount;

		lock (_syncRoot)
		{
			ReplaceSnapshotLines(startLineIndex, removedLineCount, insertedLineCount);

			if (lineDelta > 0)
			{
				for (int i = 0; i < lineDelta; i++)
					AddLine(startLineIndex + 1 + i);
			}
			else if (lineDelta < 0)
			{
				for (int i = 0; i < -lineDelta; i++)
				{
					int removeIndex = Math.Min(startLineIndex + 1, GetNumberOfLines() - 1);

					if (removeIndex >= 0)
						RemoveLine(removeIndex);
				}
			}

			UpdateLine(Math.Min(startLineIndex, Math.Max(0, GetNumberOfLines() - 1)));
		}
	}

	/// <summary>
	/// Computes the line range counts needed to update a TextMate line list after a document change.
	/// </summary>
	/// <param name="document">The document the change applies to.</param>
	/// <param name="change">The document change to analyze.</param>
	/// <returns>
	/// A tuple containing the zero-based start line index, the number of lines removed, and the number of lines inserted.
	/// </returns>
	public static (int StartLineIndex, int RemovedLineCount, int InsertedLineCount) GetChangeInfo(TextDocument document, DocumentChangeEventArgs change)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(change);

		return (
			GetStartLineIndex(document, change.Offset),
			GetAffectedLineCount(change.RemovedText?.Text),
			GetAffectedLineCount(change.InsertedText?.Text));
	}

	private void InitializeSnapshot()
	{
		lock (_syncRoot)
		{
			_lineTexts.Clear();

			for (int i = 0; i < _document.LineCount; i++)
			{
				_lineTexts.Add(ReadDocumentLineText(i));
				AddLine(i);
			}
		}
	}

	private void ReplaceSnapshotLines(int startLineIndex, int removedLineCount, int insertedLineCount)
	{
		int safeStartLineIndex = Math.Max(0, Math.Min(startLineIndex, _lineTexts.Count));
		int removableLineCount = Math.Max(0, Math.Min(removedLineCount, _lineTexts.Count - safeStartLineIndex));

		if (removableLineCount > 0)
			_lineTexts.RemoveRange(safeStartLineIndex, removableLineCount);

		_lineTexts.InsertRange(safeStartLineIndex, ReadDocumentLines(safeStartLineIndex, insertedLineCount));
	}

	private List<string> ReadDocumentLines(int startLineIndex, int lineCount)
	{
		var lines = new List<string>();

		if (_document.LineCount == 0)
			return lines;

		int safeStartLineIndex = Math.Max(0, Math.Min(startLineIndex, _document.LineCount - 1));
		int safeLineCount = Math.Max(1, Math.Min(lineCount, _document.LineCount - safeStartLineIndex));

		for (int i = 0; i < safeLineCount; i++)
			lines.Add(ReadDocumentLineText(safeStartLineIndex + i));

		return lines;
	}

	private string ReadDocumentLineText(int lineIndex)
	{
		DocumentLine line = _document.GetLineByNumber(Math.Max(1, lineIndex + 1));
		return _document.GetText(line.Offset, line.TotalLength);
	}

	private static int GetStartLineIndex(TextDocument document, int offset)
	{
		if (document.LineCount == 0)
			return 0;

		int safeOffset = document.ClampOffset(offset);
		DocumentLine line = document.GetLineByOffset(safeOffset);
		return Math.Max(0, line.LineNumber - 1);
	}

	private static int CountLineBreaks(string? text)
	{
		if (string.IsNullOrEmpty(text))
			return 0;

		int count = 0;

		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '\r')
			{
				count++;

				if (i + 1 < text.Length && text[i + 1] == '\n')
					i++;

				continue;
			}

			if (text[i] == '\n')
				count++;
		}

		return count;
	}

	private static int GetAffectedLineCount(string? text)
		=> CountLineBreaks(text) + 1;
}
