using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextDocumentSnapshotTests
{
	[TestMethod]
	public void Snapshot_DoesNotChangeWhenDocumentTextIsReplaced()
	{
		var document = new TextDocument("hello");
		var snapshot = new TextDocumentSnapshot(document);

		document.Text = "world!";

		Assert.AreEqual("hello", snapshot.GetText(0, snapshot.TextLength));
		Assert.AreEqual(5, snapshot.TextLength);
		Assert.AreEqual(1, snapshot.LineCount);
	}

	[TestMethod]
	public void Snapshot_DoesNotChangeWhenDocumentIsEdited()
	{
		var document = new TextDocument("line1\nline2");
		var snapshot = new TextDocumentSnapshot(document);

		document.Insert(6, " inserted");

		Assert.AreEqual("line1\nline2", snapshot.GetText(0, snapshot.TextLength));
		Assert.AreEqual(11, snapshot.TextLength);
		Assert.AreEqual(2, snapshot.LineCount);
	}

	[TestMethod]
	public void Snapshot_Lines_RemainStableAfterDocumentEdit()
	{
		var document = new TextDocument("first\nsecond");
		var snapshot = new TextDocumentSnapshot(document);

		ITextLine line = snapshot.GetLineByNumber(2);
		document.Insert(0, "X");

		Assert.AreEqual(6, line.Offset);
		Assert.AreEqual(6, line.Length);
		Assert.AreEqual(2, line.LineNumber);
		Assert.AreEqual("second", snapshot.GetText(line.Offset, line.Length));
	}

	[TestMethod]
	public void Snapshot_CapturesFileNameAtConstruction()
	{
		var document = new TextDocument("text")
		{
			FileName = @"C:\path\original.txt"
		};

		var snapshot = new TextDocumentSnapshot(document);

		document.FileName = @"C:\path\changed.txt";

		Assert.AreEqual(@"C:\path\original.txt", snapshot.FileName);
	}
}
