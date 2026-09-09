using static Nickelony.IDEKit.Core.Tests.TextEditInputs;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditTargetContractTests
{
	[TestMethod]
	public void Apply_PreparedBatchInListOrder_ProducesTheEditedText()
	{
		var target = new NaiveTextEditTarget("abcdef");
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[
				CreateEdit(1, 1, "B"),
				CreateEdit(4, 0, "Z")
			]);

		Assert.IsTrue(result.IsValid);

		// The target contract is that a prepared batch applies in list order without sorting, so a
		// target that does exactly that must produce the edited text from the descending operations.
		target.Apply(result.Edits);

		Assert.AreEqual("aBcdZef", target.Text);
	}

	// A minimal ITextEditTarget implementation: it applies the validated operations in list order,
	// exactly as the interface documents, with no sorting or offset bookkeeping of its own.
	private sealed class NaiveTextEditTarget : ITextEditTarget
	{
		public NaiveTextEditTarget(string text) => Text = text;

		public string Text { get; private set; }

		public void Apply(PreparedTextEdits edits)
		{
			foreach (TextEditOperation operation in edits.Operations)
			{
				Text = string.Concat(
					Text.AsSpan(0, operation.StartOffset),
					operation.NewText,
					Text.AsSpan(operation.EndOffset));
			}
		}
	}

	[TestMethod]
	public void VersionWorkflow_CapturePrepareCompare_ConfirmsAnUnchangedTarget()
	{
		var target = new VersionedTextEditTarget("abcdef");

		// The documented workflow: capture the stamp, read the content, prepare against it, then
		// compare the captured version before publishing.
		long capturedVersion = target.Version;
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot(target.Text, "target"),
			[CreateEdit(1, 1, "B")]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(capturedVersion, target.Version);

		target.Apply(result.Edits);

		Assert.AreEqual("aBcdef", target.Text);
		Assert.AreNotEqual(capturedVersion, target.Version);
	}

	[TestMethod]
	public void VersionWorkflow_ChangedTargetBetweenCaptureAndPublish_IsDetectable()
	{
		var target = new VersionedTextEditTarget("abcdef");
		long capturedVersion = target.Version;

		// An edit after the capture advances the stamp, so a batch prepared before it must be
		// rejected by the version comparison instead of applied to changed content.
		target.Apply(new PreparedTextEdits([new TextEditOperation(1, 2, "X", 0)]));

		Assert.AreNotEqual(capturedVersion, target.Version);
		Assert.AreEqual("aXcdef", target.Text);
	}

	// A target that exposes the opaque change stamp: every applied batch advances it.
	private sealed class VersionedTextEditTarget : ITextEditTarget, ITextEditTargetVersion
	{
		public VersionedTextEditTarget(string text) => Text = text;

		public string Text { get; private set; }

		public long Version { get; private set; }

		public void Apply(PreparedTextEdits edits)
		{
			foreach (TextEditOperation operation in edits.Operations)
			{
				Text = string.Concat(
					Text.AsSpan(0, operation.StartOffset),
					operation.NewText,
					Text.AsSpan(operation.EndOffset));
			}

			Version++;
		}
	}
}
