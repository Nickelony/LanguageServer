using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing.Tests;

[TestClass]
public sealed class TextEditKernelTests
{
	[TestMethod]
	public void Prepare_NonOverlappingEdits_ReturnsDescendingOperations()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[
				CreateEdit(1, 1, "B"),
				CreateEdit(4, 1, "E")
			]);

		Assert.IsTrue(result.IsValid);
		CollectionAssert.AreEqual(new[] { 4, 1 }, result.Operations.Select(operation => operation.StartOffset).ToArray());
		Assert.AreEqual("B", result.Operations[1].NewText);
		Assert.AreEqual(1, result.Operations[1].Length);
	}

	[TestMethod]
	public void Prepare_AdjacentInsertions_AreValid()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[
				CreateEdit(1, 0, "x"),
				CreateEdit(2, 0, "y")
			]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(2, result.Operations.Count);
		Assert.AreEqual(2, result.Operations[0].StartOffset);
		Assert.AreEqual(1, result.Operations[1].StartOffset);
	}

	[TestMethod]
	public void Prepare_SameOffsetInsertions_AreRejected()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[CreateEdit(1, 0, "x"), CreateEdit(1, 0, "y")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(TextEditPreparationDiagnosticCode.DuplicateInsertion, result.Diagnostics[0].Code);
		Assert.AreEqual(0, result.Diagnostics[0].EditIndex);
		Assert.AreEqual(1, result.Diagnostics[0].RelatedEditIndex);
		Assert.AreEqual(0, result.Operations.Count);
	}

	[TestMethod]
	public void Prepare_InsertionInsideReplacement_IsRejected()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 3, "x"), CreateEdit(2, 0, "y")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(TextEditPreparationDiagnosticCode.InsertionReplacementIntersection, result.Diagnostics[0].Code);
		Assert.AreEqual(1, result.Diagnostics[0].EditIndex);
		Assert.AreEqual(0, result.Diagnostics[0].RelatedEditIndex);
	}

	[TestMethod]
	public void Prepare_OverlappingReplacements_AreRejected()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 3, "x"), CreateEdit(3, 2, "y")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(TextEditPreparationDiagnosticCode.OverlappingRanges, result.Diagnostics[0].Code);
		Assert.AreEqual(0, result.Diagnostics[0].EditIndex);
		Assert.AreEqual(1, result.Diagnostics[0].RelatedEditIndex);
	}

	[TestMethod]
	public void Prepare_InvalidRange_ReturnsDiagnosticWithoutOperations()
	{
		var snapshot = new StringTextSnapshot("text");
		TextEditPreparationResult result = TextEditKernel.Prepare(
			snapshot,
			[CreateEdit(4, 1, "invalid")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(TextEditPreparationDiagnosticCode.InvalidRange, result.Diagnostics[0].Code);
		Assert.AreEqual(0, result.Diagnostics[0].EditIndex);
		Assert.AreEqual(0, result.Operations.Count);
		Assert.AreEqual("text", snapshot.GetText(0, snapshot.TextLength));
	}

	[TestMethod]
	public void MapOffset_AccountsForReplacementAndInsertionDeltas()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 2, "X"), CreateEdit(5, 0, "YZ")]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(0, TextEditKernel.MapOffset(0, result.Operations));
		Assert.AreEqual(1, TextEditKernel.MapOffset(1, result.Operations));
		Assert.AreEqual(2, TextEditKernel.MapOffset(3, result.Operations));
		Assert.AreEqual(7, TextEditKernel.MapOffset(6, result.Operations));
	}

	[TestMethod]
	public void Prepare_UsesUtf16OffsetsForSupplementaryCharacters()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("A\U0001F600B"),
			[CreateEdit(3, 1, "C")]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(3, result.Operations[0].StartOffset);
		Assert.AreEqual(4, result.Operations[0].EndOffset);
	}

	private static TextEditInput CreateEdit(int offset, int length, string newText)
		=> new(new TextRange(offset, length), newText);
}
