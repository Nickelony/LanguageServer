using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Editing;
using System.Globalization;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceEditResultTests
{
	private static WorkspaceEditApplicationResult CreateResult(
		IReadOnlyList<string> changedTargetIds,
		IReadOnlyList<string> unknownTargetIds,
		LocalPathComparisonPolicy? pathComparison = null)
		=> unknownTargetIds.Count == 0
			? WorkspaceEditApplicationResult.Completed(
				preparedOperationCount: 1,
				targetResults: [],
				CreateChangeSet(changedTargetIds),
				pathComparison)
			: WorkspaceEditApplicationResult.PartiallyApplied(
				preparedOperationCount: 1,
				targetResults: [],
				unknownTargetIds,
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.TargetNotChanged, "The target did not change."),
				CreateChangeSet(changedTargetIds),
				pathComparison);

	// The changed target ids are derived from the change set, so a requested id list is expressed
	// as a change set carrying one change per id.
	private static WorkspaceEditChangeSet CreateChangeSet(IReadOnlyList<string> changedTargetIds)
		=> new([.. changedTargetIds.Select(targetId => new WorkspaceDocumentChange
		{
			TargetId = targetId,
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat
		})]);

	[TestMethod]
	public void ChangedTargetIds_DeduplicateWithSuppliedComparerPreservingFirstOccurrence()
	{
		WorkspaceEditApplicationResult result = CreateResult(
			["Doc.lua", "doc.lua", "other.lua"],
			[],
			LocalPathComparisonPolicy.CaseInsensitive);

		CollectionAssert.AreEqual(new[] { "Doc.lua", "other.lua" }, result.ChangedTargetIds.ToArray());
	}

	[TestMethod]
	public void ChangedTargetIds_DefaultComparisonFollowsTheOperatingSystem()
	{
		WorkspaceEditApplicationResult result = CreateResult(
			["Doc.lua", "doc.lua"],
			[]);

		// The default is the current platform's usual file-system semantics: case-insensitive on
		// Windows and macOS, ordinal elsewhere.
		string[] expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? ["Doc.lua"]
			: ["Doc.lua", "doc.lua"];
		CollectionAssert.AreEqual(expected, result.ChangedTargetIds.ToArray());
	}

	[TestMethod]
	public void ChangedTargetIds_CaseSensitivePolicyKeepsCaseVariantsDistinct()
	{
		WorkspaceEditApplicationResult result = CreateResult(
			["Doc.lua", "doc.lua"],
			[],
			LocalPathComparisonPolicy.CaseSensitive);

		CollectionAssert.AreEqual(new[] { "Doc.lua", "doc.lua" }, result.ChangedTargetIds.ToArray());
	}

	[TestMethod]
	[DoNotParallelize]
	public void ChangedTargetIds_DeduplicateOrdinallyNotCultureSensitively()
	{
		CultureInfo originalCulture = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

			WorkspaceEditApplicationResult result = CreateResult(
				["I.lua", "\u0131.lua"],
				[],
				LocalPathComparisonPolicy.CaseInsensitive);

			// Ordinal comparison keeps 'I' and 'ı' distinct, unlike Turkish-culture casing rules.
			CollectionAssert.AreEqual(new[] { "I.lua", "\u0131.lua" }, result.ChangedTargetIds.ToArray());
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
		}
	}

	[TestMethod]
	public void UnknownTargetIds_DeduplicateWithSuppliedComparerPreservingFirstOccurrence()
	{
		WorkspaceEditApplicationResult result = CreateResult(
			[],
			["Doc.lua", "DOC.LUA"],
			LocalPathComparisonPolicy.CaseInsensitive);

		CollectionAssert.AreEqual(new[] { "Doc.lua" }, result.UnknownTargetIds.ToArray());
	}

	[TestMethod]
	public void ValidationFailedResult_CarriesPreparationDiagnostics()
	{
		// The host edit-preparation layer produces ValidationFailed together with diagnostics; the
		// library's WorkspaceEditApplier only produces Completed and PartiallyApplied.
		var diagnostic = new TextEditPreparationDiagnostic(
			sourceIndex: 0,
			relatedSourceIndex: null,
			message: "The edit range is outside the snapshot.");
		var result = WorkspaceEditApplicationResult.ValidationFailed(
			[diagnostic],
			failure: new WorkspaceOperationFailure("PreparationFailed", "Preparation failed."));

		Assert.AreEqual(WorkspaceEditApplicationStatus.ValidationFailed, result.Status);
		Assert.AreEqual(1, result.Diagnostics.Count);
		Assert.AreEqual("The edit range is outside the snapshot.", result.Diagnostics[0].Message);
		Assert.AreEqual("PreparationFailed", result.Failure!.Code);
	}

	[TestMethod]
	public void ValidationFailedFactory_WithoutFailure_ExposesTheEmptyMatrix()
	{
		var diagnostic = new TextEditPreparationDiagnostic(
			sourceIndex: 0,
			relatedSourceIndex: null,
			message: "The edit range is outside the snapshot.");

		WorkspaceEditApplicationResult result = WorkspaceEditApplicationResult.ValidationFailed([diagnostic]);

		Assert.AreEqual(WorkspaceEditApplicationStatus.ValidationFailed, result.Status);
		Assert.AreEqual(0, result.PreparedOperationCount);
		Assert.IsEmpty(result.TargetResults);
		Assert.IsEmpty(result.ChangedTargetIds);
		Assert.IsEmpty(result.UnknownTargetIds);
		Assert.HasCount(1, result.Diagnostics);
		Assert.IsNull(result.Failure);
		Assert.IsFalse(result.ChangeSet.HasChanges);
		Assert.IsEmpty(result.ChangeSet.DocumentChanges);
	}

	[TestMethod]
	public void CompletedFactory_ExposesTheCompletedMatrix()
	{
		var first = new WorkspaceEditTargetResult
		{
			TargetId = "first.lua",
			ExpectedVersion = 4,
			ActualVersion = 5,
			PreparedOperationCount = 2,
			Status = WorkspaceEditTargetStatus.Applied
		};
		var second = new WorkspaceEditTargetResult
		{
			TargetId = "second.lua",
			ExpectedVersion = 4,
			ActualVersion = 6,
			PreparedOperationCount = 1,
			Status = WorkspaceEditTargetStatus.Applied
		};
		WorkspaceEditChangeSet changeSet = CreateChangeSet(["first.lua", "second.lua"]);

		WorkspaceEditApplicationResult result = WorkspaceEditApplicationResult.Completed(
			preparedOperationCount: 3,
			targetResults: [first, second],
			changeSet);

		Assert.AreEqual(WorkspaceEditApplicationStatus.Completed, result.Status);
		Assert.AreEqual(3, result.PreparedOperationCount);
		CollectionAssert.AreEqual(new[] { first, second }, result.TargetResults.ToArray());
		CollectionAssert.AreEqual(new[] { "first.lua", "second.lua" }, result.ChangedTargetIds.ToArray());
		Assert.IsEmpty(result.UnknownTargetIds);
		Assert.IsEmpty(result.Diagnostics);
		Assert.IsNull(result.Failure);
		Assert.AreSame(changeSet, result.ChangeSet);
		Assert.IsTrue(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public void PartiallyAppliedFactory_ExposesThePartialMatrix()
	{
		var failure = new WorkspaceOperationFailure(
			WorkspaceOperationFailureCodes.TargetApplicationFailed,
			"The store failed.");
		var target = new WorkspaceEditTargetResult
		{
			TargetId = "first.lua",
			ExpectedVersion = 4,
			ActualVersion = null,
			PreparedOperationCount = 1,
			Status = WorkspaceEditTargetStatus.Unknown,
			Failure = failure
		};
		WorkspaceEditChangeSet changeSet = CreateChangeSet(["second.lua"]);

		WorkspaceEditApplicationResult result = WorkspaceEditApplicationResult.PartiallyApplied(
			preparedOperationCount: 2,
			targetResults: [target],
			unknownTargetIds: ["first.lua", "FIRST.LUA"],
			failure,
			changeSet,
			LocalPathComparisonPolicy.CaseSensitive);

		Assert.AreEqual(WorkspaceEditApplicationStatus.PartiallyApplied, result.Status);
		Assert.AreEqual(2, result.PreparedOperationCount);
		CollectionAssert.AreEqual(new[] { target }, result.TargetResults.ToArray());
		CollectionAssert.AreEqual(new[] { "second.lua" }, result.ChangedTargetIds.ToArray());

		// A case-sensitive policy keeps case variants distinct.
		CollectionAssert.AreEqual(new[] { "first.lua", "FIRST.LUA" }, result.UnknownTargetIds.ToArray());

		Assert.IsEmpty(result.Diagnostics);
		Assert.AreSame(failure, result.Failure);
		Assert.AreSame(changeSet, result.ChangeSet);
		Assert.IsTrue(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public void CanceledFactory_ExposesTheCanceledMatrix()
	{
		var target = new WorkspaceEditTargetResult
		{
			TargetId = "first.lua",
			ExpectedVersion = 4,
			ActualVersion = null,
			PreparedOperationCount = 1,
			Status = WorkspaceEditTargetStatus.NotApplied
		};

		WorkspaceEditApplicationResult result = WorkspaceEditApplicationResult.Canceled(
			preparedOperationCount: 1,
			targetResults: [target]);

		Assert.AreEqual(WorkspaceEditApplicationStatus.Canceled, result.Status);
		Assert.AreEqual(1, result.PreparedOperationCount);
		CollectionAssert.AreEqual(new[] { target }, result.TargetResults.ToArray());
		Assert.IsEmpty(result.ChangedTargetIds);
		Assert.IsEmpty(result.UnknownTargetIds);
		Assert.IsEmpty(result.Diagnostics);
		Assert.IsNull(result.Failure);
		Assert.IsFalse(result.ChangeSet.HasChanges);
	}

	[TestMethod]
	public void ChangeSet_HasChangesReflectsCapturedDocumentChanges()
	{
		var change = new WorkspaceDocumentChange
		{
			TargetId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat
		};
		var emptyTransaction = new WorkspaceEditChangeSet([]);

		Assert.IsFalse(emptyTransaction.HasChanges);

		WorkspaceEditApplicationResult result = WorkspaceEditApplicationResult.Completed(
			preparedOperationCount: 1,
			targetResults: [],
			new WorkspaceEditChangeSet([change]));

		Assert.IsTrue(result.ChangeSet.HasChanges);
		Assert.AreEqual(1, result.ChangeSet.DocumentChanges.Count);
		Assert.AreEqual("doc.lua", result.ChangeSet.DocumentChanges[0].TargetId);
	}

	[TestMethod]
	public void WorkspaceDocumentChange_IsValueEqualAndSupportsWithUpdates()
	{
		var change = new WorkspaceDocumentChange
		{
			TargetId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat
		};
		var same = new WorkspaceDocumentChange
		{
			TargetId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat
		};
		var updated = change with { AfterContent = "updated" };

		Assert.AreEqual(change, same);
		Assert.AreEqual(change.GetHashCode(), same.GetHashCode());
		Assert.AreEqual("doc.lua", updated.TargetId);
		Assert.AreEqual("before", updated.BeforeContent);
		Assert.AreEqual("updated", updated.AfterContent);
		Assert.AreEqual(TestSnapshots.FileFormat, updated.AfterFileFormat);
	}

	[TestMethod]
	public void WorkspaceEditTargetResult_IsValueEqual()
	{
		var failure = new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.TargetNotChanged, "The target did not change.");
		var result = new WorkspaceEditTargetResult
		{
			TargetId = "doc.lua",
			ExpectedVersion = 4,
			ActualVersion = 4,
			PreparedOperationCount = 2,
			Status = WorkspaceEditTargetStatus.Applied,
			Failure = failure
		};
		var same = new WorkspaceEditTargetResult
		{
			TargetId = "doc.lua",
			ExpectedVersion = 4,
			ActualVersion = 4,
			PreparedOperationCount = 2,
			Status = WorkspaceEditTargetStatus.Applied,
			Failure = failure
		};

		Assert.AreEqual(result, same);
		Assert.AreEqual(result.GetHashCode(), same.GetHashCode());
	}

	[TestMethod]
	public void WorkspaceDocumentChange_WithNullTargetId_Throws()
	{
		var change = new WorkspaceDocumentChange
		{
			TargetId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => change with { TargetId = null! });
	}

	[TestMethod]
	public void WorkspaceDocumentChange_WithNullAfterContent_Throws()
	{
		var change = new WorkspaceDocumentChange
		{
			TargetId = "doc.lua",
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => change with { AfterContent = null! });
	}

	[TestMethod]
	public void WorkspaceEditTargetResult_WithNullTargetId_Throws()
	{
		var result = new WorkspaceEditTargetResult
		{
			TargetId = "doc.lua",
			ExpectedVersion = 4,
			ActualVersion = 4,
			PreparedOperationCount = 2,
			Status = WorkspaceEditTargetStatus.Applied
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => result with { TargetId = null! });
	}

	[TestMethod]
	public void WorkspaceEditTargetResult_WithNegativePreparedOperationCount_Throws()
	{
		var result = new WorkspaceEditTargetResult
		{
			TargetId = "doc.lua",
			ExpectedVersion = 4,
			ActualVersion = 4,
			PreparedOperationCount = 2,
			Status = WorkspaceEditTargetStatus.Applied
		};

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => result with { PreparedOperationCount = -1 });
	}

	[TestMethod]
	public void WorkspaceEditTargetPreparation_WithNullTargetId_Throws()
	{
		WorkspaceEditTargetPreparation preparation = CreatePreparation();

		Assert.ThrowsExactly<ArgumentNullException>(() => preparation with { TargetId = null! });
	}

	[TestMethod]
	public void WorkspaceEditTargetPreparation_WithNullBeforeContent_Throws()
	{
		WorkspaceEditTargetPreparation preparation = CreatePreparation();

		Assert.ThrowsExactly<ArgumentNullException>(() => preparation with { BeforeContent = null! });
	}

	[TestMethod]
	public void WorkspaceEditTargetPreparation_WithNullAfterContent_Throws()
	{
		WorkspaceEditTargetPreparation preparation = CreatePreparation();

		Assert.ThrowsExactly<ArgumentNullException>(() => preparation with { AfterContent = null! });
	}

	[TestMethod]
	public void ApplicationResultFactories_NullCollectionsAndFailureAreRejected()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceEditApplicationResult.Completed(
			preparedOperationCount: 0,
			targetResults: null!,
			changeSet: new WorkspaceEditChangeSet([])));
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceEditApplicationResult.Completed(
			preparedOperationCount: 0,
			targetResults: [],
			changeSet: null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceEditApplicationResult.PartiallyApplied(
			preparedOperationCount: 0,
			targetResults: [],
			unknownTargetIds: [],
			failure: null!,
			changeSet: new WorkspaceEditChangeSet([])));
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceEditApplicationResult.PartiallyApplied(
			preparedOperationCount: 0,
			targetResults: [],
			unknownTargetIds: null!,
			failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.TargetNotChanged, "The target did not change."),
			changeSet: new WorkspaceEditChangeSet([])));
		Assert.ThrowsExactly<ArgumentNullException>(() => WorkspaceEditApplicationResult.ValidationFailed(null!));
	}

	[TestMethod]
	public void ApplicationResultFactories_NegativePreparedOperationCountIsRejected()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorkspaceEditApplicationResult.Completed(
			preparedOperationCount: -1,
			targetResults: [],
			changeSet: new WorkspaceEditChangeSet([])));
	}

	[TestMethod]
	public void ChangeSet_CopiesCallerCollection()
	{
		var changes = new List<WorkspaceDocumentChange>
		{
			new()
			{
				TargetId = "doc.lua",
				BeforeContent = "before",
				AfterContent = "after",
				BeforeFileFormat = TestSnapshots.FileFormat,
				AfterFileFormat = TestSnapshots.FileFormat
			}
		};
		var changeSet = new WorkspaceEditChangeSet(changes);

		changes.Add(new WorkspaceDocumentChange
		{
			TargetId = "other.lua",
			BeforeContent = "old",
			AfterContent = "new",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat
		});

		Assert.AreEqual(1, changeSet.DocumentChanges.Count);
		Assert.AreEqual("doc.lua", changeSet.DocumentChanges[0].TargetId);
	}

	private static WorkspaceEditTargetPreparation CreatePreparation()
	{
		return new WorkspaceEditTargetPreparation
		{
			TargetId = "doc.lua",
			Identity = new(new WorkspaceDocumentKey(Guid.NewGuid()), "doc.lua", 4),
			BeforeContent = "before",
			AfterContent = "after",
			BeforeFileFormat = TestSnapshots.FileFormat,
			AfterFileFormat = TestSnapshots.FileFormat
		};
	}
}
