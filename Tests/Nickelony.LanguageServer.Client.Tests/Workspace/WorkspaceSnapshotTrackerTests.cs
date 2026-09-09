namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class WorkspaceSnapshotTrackerTests
{
	[TestMethod]
	public void BuildDeltaBatch_ReportsCreatedChangedAndDeletedPaths()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshot_" + Guid.NewGuid().ToString("N"));
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string changedFilePath = Path.Combine(scriptsDirectoryPath, "changed.txt");
		string deletedFilePath = Path.Combine(scriptsDirectoryPath, "deleted.txt");
		string createdFilePath = Path.Combine(scriptsDirectoryPath, "created.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);
			File.WriteAllText(changedFilePath, "return 1");
			File.WriteAllText(deletedFilePath, "return 2");

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();

			File.WriteAllText(changedFilePath, "return 123456");
			File.Delete(deletedFilePath);
			File.WriteAllText(createdFilePath, "return 3");

			Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			string normalizedChangedFilePath = LanguageServerPaths.NormalizeLocalPath(changedFilePath);
			string normalizedDeletedFilePath = LanguageServerPaths.NormalizeLocalPath(deletedFilePath);
			string normalizedCreatedFilePath = LanguageServerPaths.NormalizeLocalPath(createdFilePath);

			Assert.AreEqual(3, batch.Count);
			Assert.AreEqual(FileChangeKind.Changed, GetChange(batch, normalizedChangedFilePath).Kind);
			Assert.AreEqual(FileChangeKind.Deleted, GetChange(batch, normalizedDeletedFilePath).Kind);
			Assert.AreEqual(FileChangeKind.Created, GetChange(batch, normalizedCreatedFilePath).Kind);
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	public void BuildDeltaBatch_DoesNotReportDeletionWhenTrackedPathStillExistsButCurrentSnapshotMissesIt()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotMissingCurrent_" + Guid.NewGuid().ToString("N"));
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string existingFilePath = Path.Combine(scriptsDirectoryPath, "existing.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);
			File.WriteAllText(existingFilePath, "return 1");

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();
			var currentSnapshot = new Dictionary<string, WorkspaceSnapshotEntry>(StringComparer.OrdinalIgnoreCase);

			FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			Assert.AreEqual(0, batch.Count);
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	public void BuildDeltaBatch_ReportsChangedPathWhenContentChangesWithoutLengthOrTimestampChange()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotFingerprint_" + Guid.NewGuid().ToString("N"));
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string changedFilePath = Path.Combine(scriptsDirectoryPath, "changed.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);
			File.WriteAllText(changedFilePath, "return 1");
			DateTime baselineWriteTime = File.GetLastWriteTimeUtc(changedFilePath);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();

			File.WriteAllText(changedFilePath, "return 2");
			File.SetLastWriteTimeUtc(changedFilePath, baselineWriteTime);

			Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			string normalizedChangedFilePath = LanguageServerPaths.NormalizeLocalPath(changedFilePath);

			Assert.AreEqual(1, batch.Count);
			Assert.AreEqual(FileChangeKind.Changed, GetChange(batch, normalizedChangedFilePath).Kind);
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	public void ApplyChanges_UpdatesTrackedSnapshotForSubsequentRecoveryDiff()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotApply_" + Guid.NewGuid().ToString("N"));
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string createdFilePath = Path.Combine(scriptsDirectoryPath, "created.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			File.WriteAllText(createdFilePath, "return 1");
			string normalizedCreatedFilePath = LanguageServerPaths.NormalizeLocalPath(createdFilePath);

			tracker.ApplyChanges(
			[
				new WorkspaceFileChange(normalizedCreatedFilePath, FileChangeKind.Created)
			]);

			Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();
			File.Delete(createdFilePath);

			Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			Assert.AreEqual(1, batch.Count);
			Assert.AreEqual(FileChangeKind.Deleted, batch.Entries[0].Kind);
			Assert.AreEqual(normalizedCreatedFilePath, batch.Entries[0].Path);
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	public void ApplyChanges_WhenAnOlderBatchCommitsAfterANewerOne_KeepsUnrelatedPathsAndSkipsSupersededOnes()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotTickets_" + Guid.NewGuid().ToString("N"));
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string firstFilePath = Path.Combine(scriptsDirectoryPath, "first.txt");
		string secondFilePath = Path.Combine(scriptsDirectoryPath, "second.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);
			File.WriteAllText(firstFilePath, "return 1");
			File.WriteAllText(secondFilePath, "return 1");

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			string normalizedFirstFilePath = LanguageServerPaths.NormalizeLocalPath(firstFilePath);
			string normalizedSecondFilePath = LanguageServerPaths.NormalizeLocalPath(secondFilePath);

			// Hold the older batch between computing its updates and committing them, run the newer batch
			// completely, then let the older batch commit.
			using var olderBatchReachedCommit = new ManualResetEventSlim(false);
			using var releaseOlderBatch = new ManualResetEventSlim(false);

			tracker.BeforeApplyCommitTestHook = () =>
			{
				olderBatchReachedCommit.Set();
				releaseOlderBatch.Wait();
			};

			File.WriteAllText(firstFilePath, "return 2");
			File.WriteAllText(secondFilePath, "return 2");

			Task olderBatch = Task.Run(() => tracker.ApplyChanges(
			[
				new WorkspaceFileChange(normalizedFirstFilePath, FileChangeKind.Changed),
				new WorkspaceFileChange(normalizedSecondFilePath, FileChangeKind.Changed)
			]));

			Assert.IsTrue(olderBatchReachedCommit.Wait(TimeSpan.FromSeconds(10)));

			// The newer batch commits first: it deletes the first path and leaves the second path untouched.
			tracker.BeforeApplyCommitTestHook = null;
			File.Delete(firstFilePath);

			tracker.ApplyChanges(
			[
				new WorkspaceFileChange(normalizedFirstFilePath, FileChangeKind.Deleted)
			]);

			releaseOlderBatch.Set();
			olderBatch.Wait(TimeSpan.FromSeconds(10));

			Dictionary<string, WorkspaceSnapshotEntry> trackedSnapshot = tracker.CloneTrackedSnapshot();

			// The older observation for the deleted path must not resurrect it from the newer one...
			Assert.IsFalse(trackedSnapshot.ContainsKey(normalizedFirstFilePath));

			// ...while the unrelated path from the same older batch is still applied.
			Assert.IsTrue(trackedSnapshot.ContainsKey(normalizedSecondFilePath));
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	public void CaptureTrackedSnapshot_PreservesCaseDistinctPathsOnCaseSensitiveHosts()
	{
		if (!LanguageServerPaths.UsesCaseSensitiveLocalPaths)
			Assert.Inconclusive("The test requires case-sensitive local-path identity on the current host.");

		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotCase_" + Guid.NewGuid().ToString("N"));
		string firstFilePath = Path.Combine(workspaceRoot, "Case.txt");
		string secondFilePath = Path.Combine(workspaceRoot, "case.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(firstFilePath, "return 1");
			File.WriteAllText(secondFilePath, "return 2");

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			Dictionary<string, WorkspaceSnapshotEntry> snapshot = tracker.CloneTrackedSnapshot();

			Assert.AreEqual(2, snapshot.Count);
			Assert.IsTrue(snapshot.ContainsKey(LanguageServerPaths.NormalizeLocalPath(firstFilePath)));
			Assert.IsTrue(snapshot.ContainsKey(LanguageServerPaths.NormalizeLocalPath(secondFilePath)));
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void CaptureTrackedSnapshot_IncludesHiddenFiles()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotHidden_" + Guid.NewGuid().ToString("N"));
		string hiddenFilePath = Path.Combine(workspaceRoot, "hidden.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(hiddenFilePath, "return 1");
			File.SetAttributes(hiddenFilePath, File.GetAttributes(hiddenFilePath) | FileAttributes.Hidden);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			// The file-system watcher observes hidden files, so capture must include them or recovery could never
			// reconcile a hidden watched file after an outage.
			Assert.IsTrue(tracker.CloneTrackedSnapshot().ContainsKey(LanguageServerPaths.NormalizeLocalPath(hiddenFilePath)));
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	public void CaptureTrackedSnapshot_ExcludesReparsePoints()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotReparse_" + Guid.NewGuid().ToString("N"));
		string targetFilePath = Path.Combine(workspaceRoot, "target.txt");
		string linkFilePath = Path.Combine(workspaceRoot, "link.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(targetFilePath, "return 1");
			File.CreateSymbolicLink(linkFilePath, targetFilePath);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			Dictionary<string, WorkspaceSnapshotEntry> snapshot = tracker.CloneTrackedSnapshot();

			Assert.IsTrue(snapshot.ContainsKey(LanguageServerPaths.NormalizeLocalPath(targetFilePath)));
			Assert.IsFalse(snapshot.ContainsKey(LanguageServerPaths.NormalizeLocalPath(linkFilePath)),
				"Reparse points are excluded because neither capture nor the watcher follows them consistently.");
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or NotSupportedException)
		{
			Assert.Inconclusive("Symbolic link creation is not available on this host: " + exception.Message);
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	public void CaptureTrackedSnapshot_WhenWorkspaceRootIsMissing_ReturnsEmptySnapshot()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotMissingRoot_" + Guid.NewGuid().ToString("N"));

		var tracker = CreateTracker(workspaceRoot);
		tracker.CaptureTrackedSnapshot();

		Assert.AreEqual(0, tracker.CloneTrackedSnapshot().Count);
	}

	[TestMethod]
	public void ApplyChanges_WhenSnapshotWasReplacedWhileComputing_SkipsStaleUpdates()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotVersion_" + Guid.NewGuid().ToString("N"));
		string changedFilePath = Path.Combine(workspaceRoot, "changed.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(changedFilePath, "return 1");

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			string normalizedChangedFilePath = LanguageServerPaths.NormalizeLocalPath(changedFilePath);

			// Replace the tracked snapshot exactly between the version read and the update computation, then change
			// the file again so a stale apply would record a different state than the intermediate capture did.
			tracker.SnapshotVersionReadTestHook = () =>
			{
				tracker.CaptureTrackedSnapshot();
				File.WriteAllText(changedFilePath, "return 22222");
			};

			try
			{
				tracker.ApplyChanges([new WorkspaceFileChange(normalizedChangedFilePath, FileChangeKind.Changed)]);
			}
			finally
			{
				tracker.SnapshotVersionReadTestHook = null;
			}

			// The intermediate capture recorded the pre-modification file ("return 1", 8 bytes); the skipped stale
			// update must not have replaced it with the 12-byte state read afterwards.
			Assert.AreEqual(8, tracker.CloneTrackedSnapshot()[normalizedChangedFilePath].Length);
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	public void Constructor_NullOrBlankArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSnapshotTracker(null!, []));
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSnapshotTracker("C:\\Workspace", null!));
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceSnapshotTracker(" ", []));
	}

	[TestMethod]
	public void ReplaceTrackedSnapshotWithCurrent_ReturnsACloneThatLaterChangesDoNotMutate()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotClone_" + Guid.NewGuid().ToString("N"));
		string firstFilePath = Path.Combine(workspaceRoot, "first.txt");
		string secondFilePath = Path.Combine(workspaceRoot, "second.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(firstFilePath, "return 1");

			var tracker = CreateTracker(workspaceRoot);
			Dictionary<string, WorkspaceSnapshotEntry> capturedSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();

			int capturedCount = capturedSnapshot.Count;

			File.WriteAllText(secondFilePath, "return 2");
			tracker.ApplyChanges([new WorkspaceFileChange(LanguageServerPaths.NormalizeLocalPath(secondFilePath), FileChangeKind.Created)]);

			Assert.AreEqual(capturedCount, capturedSnapshot.Count,
				"The returned snapshot must not be the live tracked dictionary.");
			Assert.AreEqual(capturedCount + 1, tracker.CloneTrackedSnapshot().Count);
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	[TestMethod]
	public async Task ReplaceTrackedSnapshotWithCurrent_ConcurrentWithApplyChanges_StaysConsistent()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotRace_" + Guid.NewGuid().ToString("N"));
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);

			for (int i = 0; i < 32; i++)
				File.WriteAllText(Path.Combine(scriptsDirectoryPath, "file" + i + ".txt"), "return " + i);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			using var startGate = new ManualResetEventSlim(false);

			Task replaceTask = Task.Run(() =>
			{
				startGate.Wait();

				for (int i = 0; i < 120; i++)
					tracker.ReplaceTrackedSnapshotWithCurrent();
			});

			Task applyTask = Task.Run(() =>
			{
				startGate.Wait();

				for (int i = 0; i < 120; i++)
				{
					string path = Path.Combine(scriptsDirectoryPath, "new" + i + ".txt");
					File.WriteAllText(path, "return " + i);

					tracker.ApplyChanges([new WorkspaceFileChange(LanguageServerPaths.NormalizeLocalPath(path), FileChangeKind.Created)]);
				}
			});

			startGate.Set();

			// A clone that enumerates the live tracked dictionary outside the snapshot lock can collide with an
			// in-place apply commit and throw InvalidOperationException; the tracker must stay consistent instead.
			await Task.WhenAll(replaceTask, applyTask).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

			Assert.IsTrue(tracker.CloneTrackedSnapshot().Count >= 32);
		}
		finally
		{
			if (Directory.Exists(workspaceRoot))
				Directory.Delete(workspaceRoot, recursive: true);
		}
	}

	private static WorkspaceSnapshotTracker CreateTracker(string workspaceRootDirectoryPath) => new(
		LanguageServerPaths.NormalizeLocalPath(workspaceRootDirectoryPath),
		[
			new WorkspaceWatchSpecification("*.txt", IncludeSubdirectories: true),
			new WorkspaceWatchSpecification("Config", IncludeSubdirectories: false),
			new WorkspaceWatchSpecification(".settings.*", IncludeSubdirectories: false)
		]);

	private static WorkspaceFileChange GetChange(FileChangeBatch batch, string filePath)
		=> batch.Entries.First(change => string.Equals(change.Path, filePath, StringComparison.OrdinalIgnoreCase));
}
