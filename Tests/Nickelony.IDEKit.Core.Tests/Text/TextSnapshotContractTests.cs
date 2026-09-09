namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextSnapshotContractTests
{
	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("plain", DisplayName = "SingleLineWithoutTerminator")]
	[DataRow("one\r\ntwo\nthree\rfour", DisplayName = "MixedTerminators")]
	[DataRow("a\n", DisplayName = "TrailingTerminator")]
	[DataRow("\r\n\r\n", DisplayName = "BlankLinesOnly")]
	[DataRow("A\U0001F600B\n\uD83D\n", DisplayName = "Surrogates")]
	public void Lines_ExposeTheDocumentedLineMetadata(string text)
	{
		ITextSnapshot snapshot = new StringTextSnapshot(text);

		Assert.AreEqual(text.Length, snapshot.TextLength);
		Assert.IsTrue(snapshot.LineCount >= 1);

		int nextLineStart = 0;

		for (int index = 0; index < snapshot.LineCount; index++)
		{
			ITextLine line = snapshot.GetLineByNumber(index + 1);
			ITextLine viewLine = snapshot.Lines[index];

			Assert.AreEqual(index + 1, line.LineNumber, $"Line {index + 1} number.");
			Assert.AreEqual(line.Offset + line.Length, line.EndOffset, $"Line {index + 1} end offset.");
			Assert.AreEqual(viewLine.LineNumber, line.LineNumber, $"Line {index + 1} view number.");
			Assert.AreEqual(viewLine.Offset, line.Offset, $"Line {index + 1} view offset.");
			Assert.AreEqual(viewLine.Length, line.Length, $"Line {index + 1} view length.");

			// Line lengths exclude terminators, so each line starts exactly where the previous
			// line's terminator ended and the line text must match the source slice.
			Assert.AreEqual(nextLineStart, line.Offset, $"Line {index + 1} start offset.");
			Assert.AreEqual(text.Substring(line.Offset, line.Length), snapshot.GetText(line.Offset, line.Length), $"Line {index + 1} text.");

			nextLineStart = line.EndOffset;

			if (nextLineStart < text.Length && text[nextLineStart] == '\r' && nextLineStart + 1 < text.Length && text[nextLineStart + 1] == '\n')
				nextLineStart += 2;
			else if (nextLineStart < text.Length && text[nextLineStart] is '\r' or '\n')
				nextLineStart++;
		}

		Assert.AreEqual(text.Length, nextLineStart, "The lines must reconstruct the whole text.");
		Assert.AreEqual(text, snapshot.GetText(0, snapshot.TextLength));
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("plain", DisplayName = "SingleLineWithoutTerminator")]
	[DataRow("one\r\ntwo\nthree\rfour", DisplayName = "MixedTerminators")]
	[DataRow("a\n", DisplayName = "TrailingTerminator")]
	[DataRow("\r\n\r\n", DisplayName = "BlankLinesOnly")]
	[DataRow("A\U0001F600B\n\uD83D\n", DisplayName = "Surrogates")]
	public void GetLineByOffset_MapsEveryOffsetToTheDocumentedLine(string text)
	{
		ITextSnapshot snapshot = new StringTextSnapshot(text);

		// An offset on a line terminator is associated with the preceding line and the end-of-text
		// offset is associated with the final line. Both rules read as "the last line whose text
		// starts at or before the offset", and the lines cover the text without gaps.
		for (int offset = 0; offset <= text.Length; offset++)
		{
			int expectedLineNumber = 1;

			for (int index = 0; index < snapshot.LineCount; index++)
			{
				if (snapshot.Lines[index].Offset <= offset)
					expectedLineNumber = index + 1;
			}

			Assert.AreEqual(expectedLineNumber, snapshot.GetLineByOffset(offset).LineNumber, $"Offset {offset}.");

			if (offset < text.Length)
				Assert.AreEqual(text[offset], snapshot.GetCharAt(offset), $"Offset {offset} character.");
		}
	}
}
