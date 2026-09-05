using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Editing;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Tests <see cref="WorkspaceEditApplierCore"/> results for empty, no-op, successful, and failed
/// applications.
/// </summary>
/// <remarks>
/// The replacement callback records requests or returns controlled mutation results so the tests can
/// verify target statuses, failure details, and the accumulated change set.
/// </remarks>
[TestClass]
[TestCategory("TextEditorBaseModernization")]
public sealed class WorkspaceEditApplierCoreTests
{
	private static readonly TextFileFormat s_fileFormat = new(
		TextEncodingKind.Utf8,
		false,
		TextNewlineStyle.Lf);

	[TestMethod]
	public void Apply_EmptyTargets_CompletesWithoutChanges()
	{
		var applier = new WorkspaceEditApplierCore(_ => throw new InvalidOperationException("No replacement expected."));

		TextWorkspaceEditApplicationResult result = applier.Apply([]);

		Assert.AreEqual(TextWorkspaceEditApplicationStatus.Completed, result.Status);
		Assert.IsFalse(result.HasChanges);
		Assert.AreEqual(0, result.Targets.Count);
		Assert.AreEqual(0, result.PreparedOperationCount);
	}

	[TestMethod]
	public void Apply_NoOpTargets_CompletesWithoutReplacement()
	{
		int replaceCount = 0;
		var applier = new WorkspaceEditApplierCore(_ =>
		{
			replaceCount++;
			throw new InvalidOperationException("No replacement expected.");
		});

		var target = CreateTarget("same.txt", "content", "content");
		TextWorkspaceEditApplicationResult result = applier.Apply([target]);

		Assert.AreEqual(TextWorkspaceEditApplicationStatus.Completed, result.Status);
		Assert.IsFalse(result.HasChanges);
		Assert.AreEqual(0, replaceCount);
		Assert.AreEqual(TextWorkspaceEditTargetStatus.Applied, result.Targets[0].Status);
	}

	[TestMethod]
	public void Apply_AllTargetsApplied_CompletesWithChangeSet()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		long version = 0;
		var applier = new WorkspaceEditApplierCore(request =>
		{
			requests.Add(request);
			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationStatus.Replaced,
				request.ExpectedDocumentKey,
				request.DocumentId,
				request.ExpectedVersion,
				CreateSnapshot(request.Content, ++version, request.ExpectedDocumentKey, request.DocumentId));
		});

		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");
		TextWorkspaceEditApplicationResult result = applier.Apply([first, second]);

		Assert.AreEqual(TextWorkspaceEditApplicationStatus.Completed, result.Status);
		Assert.IsTrue(result.HasChanges);
		Assert.AreEqual(2, result.Targets.Count);
		CollectionAssert.AreEquivalent(
			new[] { "first.txt", "second.txt" },
			result.ChangedTargetIds.ToArray());
		Assert.AreEqual(2, requests.Count);
		Assert.AreEqual("after", requests[0].Content);
		Assert.AreEqual("new", requests[1].Content);
		Assert.AreEqual(2, result.ChangeSet.DocumentChanges.Count);
	}

	[TestMethod]
	public void Apply_RejectedMutation_PartialResultMarksUnknownAndSkipsRemainder()
	{
		var applier = new WorkspaceEditApplierCore(request => new WorkspaceDocumentMutationResult(
			WorkspaceDocumentMutationStatus.StaleDocument,
			request.ExpectedDocumentKey,
			request.DocumentId,
			request.ExpectedVersion,
			null));

		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");
		TextWorkspaceEditApplicationResult result = applier.Apply([first, second]);

		Assert.AreEqual(TextWorkspaceEditApplicationStatus.PartiallyApplied, result.Status);
		Assert.AreEqual(TextWorkspaceEditTargetStatus.Unknown, result.Targets[0].Status);
		Assert.AreEqual(TextWorkspaceEditTargetStatus.NotApplied, result.Targets[1].Status);
		CollectionAssert.Contains(result.UnknownTargetIds.ToArray(), "first.txt");
		Assert.AreEqual(0, result.ChangedTargetIds.Count);
		Assert.AreEqual("TargetNotChanged", result.Failure?.Code);
	}

	[TestMethod]
	public void Apply_ReplaceThrows_PartialResultMarksUnknown()
	{
		var applier = new WorkspaceEditApplierCore(_ => throw new InvalidOperationException("Store unavailable."));

		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");
		TextWorkspaceEditApplicationResult result = applier.Apply([first, second]);

		Assert.AreEqual(TextWorkspaceEditApplicationStatus.PartiallyApplied, result.Status);
		Assert.AreEqual(TextWorkspaceEditTargetStatus.Unknown, result.Targets[0].Status);
		Assert.AreEqual(TextWorkspaceEditTargetStatus.NotApplied, result.Targets[1].Status);
		Assert.AreEqual("TargetApplicationFailed", result.Failure?.Code);
		Assert.AreEqual("Store unavailable.", result.Failure?.Message);
	}

	[TestMethod]
	public void Apply_FailureAfterAppliedTarget_RetainsConfirmedChanges()
	{
		var requests = new List<WorkspaceDocumentReplaceRequest>();
		var applier = new WorkspaceEditApplierCore(request =>
		{
			requests.Add(request);
			if (request.DocumentId == "second.txt")
				return new WorkspaceDocumentMutationResult(
					WorkspaceDocumentMutationStatus.OperationInProgress,
					request.ExpectedDocumentKey,
					request.DocumentId,
					request.ExpectedVersion,
					null);

			return new WorkspaceDocumentMutationResult(
				WorkspaceDocumentMutationStatus.Replaced,
				request.ExpectedDocumentKey,
				request.DocumentId,
				request.ExpectedVersion,
				CreateSnapshot(request.Content, 1, request.ExpectedDocumentKey, request.DocumentId));
		});

		WorkspaceEditTargetPreparation first = CreateTarget("first.txt", "before", "after");
		WorkspaceEditTargetPreparation second = CreateTarget("second.txt", "old", "new");
		TextWorkspaceEditApplicationResult result = applier.Apply([first, second]);

		Assert.AreEqual(TextWorkspaceEditApplicationStatus.PartiallyApplied, result.Status);
		Assert.AreEqual(TextWorkspaceEditTargetStatus.Applied, result.Targets[0].Status);
		Assert.AreEqual(TextWorkspaceEditTargetStatus.Unknown, result.Targets[1].Status);
		CollectionAssert.Contains(result.ChangedTargetIds.ToArray(), "first.txt");
		CollectionAssert.Contains(result.UnknownTargetIds.ToArray(), "second.txt");
		Assert.AreEqual(1, result.ChangeSet.DocumentChanges.Count);
		Assert.AreEqual("before", result.ChangeSet.DocumentChanges[0].BeforeContent);
		Assert.AreEqual("after", result.ChangeSet.DocumentChanges[0].AfterContent);
	}

	private static WorkspaceEditTargetPreparation CreateTarget(
		string targetId,
		string beforeContent,
		string afterContent)
	{
		var documentKey = new WorkspaceDocumentKey(Guid.NewGuid());
		return new WorkspaceEditTargetPreparation(
			targetId,
			documentKey,
			targetId,
			4,
			beforeContent,
			afterContent,
			s_fileFormat);
	}

	private static WorkspaceDocumentSnapshot CreateSnapshot(
		string content,
		long version,
		WorkspaceDocumentKey documentKey,
		string documentId)
		=> new(
			documentKey,
			documentId,
			documentId,
			version,
			version,
			false,
			new StringTextSnapshot(content, documentId),
			s_fileFormat,
			FileStamp.Missing);
}
