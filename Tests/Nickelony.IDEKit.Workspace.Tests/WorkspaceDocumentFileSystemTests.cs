using System.Security.Cryptography;
using System.Text;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
[TestCategory("TextEditorBaseModernization")]
public sealed class WorkspaceDocumentFileSystemTests
{
	private static readonly TextFileFormat s_utf8Format = new(
		TextEncodingKind.Utf8,
		false,
		TextNewlineStyle.Lf);

	private static readonly WorkspaceDocumentOpenOptions s_utf8Options = new(
		TextEncodingKind.Utf8,
		s_utf8Format);

	[TestMethod]
	public async Task WorkspaceFileCodec_DecodesByteOrderMarksAndFallbackEncodingAndRecordsNewlines()
	{
		string directory = CreateTempDirectory();
		try
		{
			string classicScriptPath = Path.Combine(directory, "strings.txt");
			File.WriteAllBytes(classicScriptPath, [0x63, 0x61, 0x66, 0xE9, 0x0D, 0x0A]);
			string utf8Path = Path.Combine(directory, "utf8.txt");
			File.WriteAllBytes(utf8Path, WorkspaceFileCodec.Encode(
				"one\r\ntwo",
				new TextFileFormat(TextEncodingKind.Utf8, true, TextNewlineStyle.CrLf)));
			string utf16LittleEndianPath = Path.Combine(directory, "utf16-le.txt");
			File.WriteAllBytes(utf16LittleEndianPath, WorkspaceFileCodec.Encode(
				"little",
				new TextFileFormat(TextEncodingKind.Utf16LittleEndian, true, TextNewlineStyle.None)));
			string utf16BigEndianPath = Path.Combine(directory, "utf16-be.txt");
			File.WriteAllBytes(utf16BigEndianPath, WorkspaceFileCodec.Encode(
				"big",
				new TextFileFormat(TextEncodingKind.Utf16BigEndian, true, TextNewlineStyle.None)));
			var fileSystem = new WorkspaceFileCodec();

			WorkspaceFileReadResult classicScriptBytes = await fileSystem.ReadAsync(
				classicScriptPath,
				CancellationToken.None);
			WorkspaceFileReadResult utf8Bytes = await fileSystem.ReadAsync(
				utf8Path,
				CancellationToken.None);
			WorkspaceFileReadResult utf16LittleEndianBytes = await fileSystem.ReadAsync(
				utf16LittleEndianPath,
				CancellationToken.None);
			WorkspaceFileReadResult utf16BigEndianBytes = await fileSystem.ReadAsync(
				utf16BigEndianPath,
				CancellationToken.None);
			TextFileFormat classicScriptFormat = new();
			TextFileFormat utf8FormatWithBom = new();
			TextFileFormat utf16LittleEndianFormat = new();
			TextFileFormat utf16BigEndianFormat = new();
			string classicScriptContent = WorkspaceFileCodec.Decode(
				classicScriptBytes.RawBytes!.Value.Span,
				TextEncodingKind.Windows1252,
				out classicScriptFormat);
			string utf8Content = WorkspaceFileCodec.Decode(
				utf8Bytes.RawBytes!.Value.Span,
				TextEncodingKind.Windows1252,
				out utf8FormatWithBom);
			string utf16LittleEndianContent = WorkspaceFileCodec.Decode(
				utf16LittleEndianBytes.RawBytes!.Value.Span,
				TextEncodingKind.Windows1252,
				out utf16LittleEndianFormat);
			string utf16BigEndianContent = WorkspaceFileCodec.Decode(
				utf16BigEndianBytes.RawBytes!.Value.Span,
				TextEncodingKind.Windows1252,
				out utf16BigEndianFormat);

			Assert.AreEqual("café\r\n", classicScriptContent);
			Assert.AreEqual(TextEncodingKind.Windows1252, classicScriptFormat.Encoding);
			Assert.IsFalse(classicScriptFormat.HasBom);
			Assert.AreEqual(TextNewlineStyle.CrLf, classicScriptFormat.NewlineStyle);
			Assert.AreEqual("one\r\ntwo", utf8Content);
			Assert.AreEqual(TextEncodingKind.Utf8, utf8FormatWithBom.Encoding);
			Assert.IsTrue(utf8FormatWithBom.HasBom);
			Assert.AreEqual(TextNewlineStyle.CrLf, utf8FormatWithBom.NewlineStyle);
			Assert.AreEqual("little", utf16LittleEndianContent);
			Assert.AreEqual(TextEncodingKind.Utf16LittleEndian, utf16LittleEndianFormat.Encoding);
			Assert.IsTrue(utf16LittleEndianFormat.HasBom);
			Assert.AreEqual("big", utf16BigEndianContent);
			Assert.AreEqual(TextEncodingKind.Utf16BigEndian, utf16BigEndianFormat.Encoding);
			Assert.IsTrue(utf16BigEndianFormat.HasBom);
			Assert.AreEqual(TextNewlineStyle.Cr, WorkspaceFileCodec.DetectNewlineStyle("one\r"));
			Assert.AreEqual(TextNewlineStyle.Mixed, WorkspaceFileCodec.DetectNewlineStyle("one\r\ntwo\nthree\r"));
			Assert.AreEqual(TextNewlineStyle.None, WorkspaceFileCodec.DetectNewlineStyle("one"));
			Assert.IsTrue(classicScriptBytes.OnDiskStamp.Exists);
			Assert.IsFalse(string.IsNullOrEmpty(classicScriptBytes.OnDiskStamp.ContentHash));
		}
		finally
		{
			DeleteTempDirectory(directory);
		}
	}

	[TestMethod]
	public async Task WorkspaceFileCodec_MovesAndDeletesOnlyWhenExpectedStampMatches()
	{
		string directory = CreateTempDirectory();
		try
		{
			string sourcePath = Path.Combine(directory, "source.lua");
			string destinationPath = Path.Combine(directory, "destination.lua");
			File.WriteAllText(sourcePath, "content");
			var fileSystem = new WorkspaceFileCodec();
			FileStamp sourceStamp = await fileSystem.CaptureStampAsync(sourcePath, CancellationToken.None);

			WorkspaceFileMoveResult moved = await fileSystem.MoveAsync(
				sourcePath,
				destinationPath,
				sourceStamp,
				CancellationToken.None);

			Assert.AreEqual(WorkspaceFileMoveStatus.Moved, moved.Status);
			Assert.IsFalse(File.Exists(sourcePath));
			Assert.AreEqual("content", File.ReadAllText(destinationPath));
			File.WriteAllText(destinationPath, "changed");

			WorkspaceFileDeleteResult staleDelete = await fileSystem.DeleteAsync(
				destinationPath,
				sourceStamp,
				CancellationToken.None);
			Assert.AreEqual(WorkspaceFileDeleteStatus.ExternalFileConflict, staleDelete.Status);

			FileStamp destinationStamp = await fileSystem.CaptureStampAsync(destinationPath, CancellationToken.None);
			WorkspaceFileDeleteResult deleted = await fileSystem.DeleteAsync(
				destinationPath,
				destinationStamp,
				CancellationToken.None);

			Assert.AreEqual(WorkspaceFileDeleteStatus.Deleted, deleted.Status);
			Assert.IsFalse(File.Exists(destinationPath));
		}
		finally
		{
			DeleteTempDirectory(directory);
		}
	}

	[TestMethod]
	public async Task Store_RenameAndSaveAsPreserveDocumentKeyAndRetargetPath()
	{
		string directory = CreateTempDirectory();
		try
		{
			string sourcePath = Path.Combine(directory, "source.lua");
			string renamedPath = Path.Combine(directory, "renamed.lua");
			string savedAsPath = Path.Combine(directory, "saved-as.lua");
			File.WriteAllText(sourcePath, "content");
			await using var store = new WorkspaceDocumentStore(new WorkspaceFileCodec());
			WorkspaceDocumentSnapshot initial = (await store.OpenAsync(sourcePath, s_utf8Options)).Snapshot!;

			WorkspaceDocumentRenameResult renamed = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
				initial.DocumentKey,
				initial.DocumentId,
				initial.Version,
				initial.OnDiskStamp,
				renamedPath));

			Assert.AreEqual(WorkspaceDocumentRenameStatus.Renamed, renamed.Status);
			Assert.IsFalse(store.TryGetSnapshot(sourcePath, out _));
			Assert.IsTrue(store.TryGetSnapshot(renamedPath, out WorkspaceDocumentSnapshot? renamedSnapshot));
			Assert.AreEqual(initial.DocumentKey, renamedSnapshot!.DocumentKey);

			WorkspaceDocumentSaveAsResult savedAs = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
				renamedSnapshot.DocumentKey,
				renamedSnapshot.DocumentId,
				renamedSnapshot.Version,
				renamedSnapshot.OnDiskStamp,
				savedAsPath));

			Assert.AreEqual(WorkspaceDocumentSaveAsStatus.SavedAs, savedAs.Status);
			Assert.IsFalse(store.TryGetSnapshot(renamedPath, out _));
			Assert.IsTrue(store.TryGetSnapshot(savedAsPath, out WorkspaceDocumentSnapshot? savedAsSnapshot));
			Assert.AreEqual(initial.DocumentKey, savedAsSnapshot!.DocumentKey);

			WorkspaceDocumentDeleteResult deleted = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
				savedAsSnapshot.DocumentKey,
				savedAsSnapshot.DocumentId,
				savedAsSnapshot.Version,
				savedAsSnapshot.OnDiskStamp));

			Assert.AreEqual(WorkspaceDocumentDeleteStatus.Deleted, deleted.Status);
			Assert.IsFalse(store.TryGetSnapshot(savedAsPath, out _));
			Assert.IsFalse(File.Exists(savedAsPath));
		}
		finally
		{
			DeleteTempDirectory(directory);
		}
	}

	[TestMethod]
	public async Task Store_CaseOnlyRenamePreservesDocumentKeyAndUpdatesDisplayPath()
	{
		string directory = CreateTempDirectory();
		try
		{
			string sourcePath = Path.Combine(directory, "Script.lua");
			string destinationPath = Path.Combine(directory, "script.lua");
			File.WriteAllText(sourcePath, "content");
			await using var store = new WorkspaceDocumentStore(new WorkspaceFileCodec());
			WorkspaceDocumentSnapshot initial = (await store.OpenAsync(sourcePath, s_utf8Options)).Snapshot!;

			WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
				initial.DocumentKey,
				initial.DocumentId,
				initial.Version,
				initial.OnDiskStamp,
				destinationPath));

			Assert.AreEqual(WorkspaceDocumentRenameStatus.Renamed, result.Status);
			Assert.AreEqual(initial.DocumentKey, result.Snapshot!.DocumentKey);
			Assert.AreEqual(destinationPath, result.Snapshot.DisplayPath);
			string[] files = Directory.GetFiles(directory);
			Assert.AreEqual(1, files.Length);
			Assert.AreEqual("script.lua", Path.GetFileName(files[0]));
		}
		finally
		{
			DeleteTempDirectory(directory);
		}
	}

	[TestMethod]
	public async Task InvalidSelectedEncoding_ReturnsInvalidEncodingFailure()
	{
		string directory = CreateTempDirectory();
		try
		{
			string path = Path.Combine(directory, "invalid.lua");
			File.WriteAllBytes(path, [0xC3, 0x28]);
			await using var store = new WorkspaceDocumentStore(new WorkspaceFileCodec());

			WorkspaceDocumentOpenResult result = await store.OpenAsync(path, s_utf8Options);

			Assert.AreEqual(WorkspaceDocumentOpenStatus.LoadFailed, result.Status);
			Assert.IsNotNull(result.Failure);
			Assert.AreEqual("InvalidEncoding", result.Failure.Code);
			Assert.IsNull(result.Snapshot);
		}
		finally
		{
			DeleteTempDirectory(directory);
		}
	}

	[TestMethod]
	public async Task UndefinedWindows1252Byte_ReturnsInvalidEncodingFailure()
	{
		string directory = CreateTempDirectory();
		try
		{
			string path = Path.Combine(directory, "invalid.txt");
			File.WriteAllBytes(path, [0x81]);
			WorkspaceDocumentOpenOptions options = new(
				TextEncodingKind.Windows1252,
				new TextFileFormat(TextEncodingKind.Windows1252, false, TextNewlineStyle.None));
			await using var store = new WorkspaceDocumentStore(new WorkspaceFileCodec());

			WorkspaceDocumentOpenResult result = await store.OpenAsync(path, options);

			Assert.AreEqual(WorkspaceDocumentOpenStatus.LoadFailed, result.Status);
			Assert.AreEqual("InvalidEncoding", result.Failure!.Code);
			Assert.IsNull(result.Snapshot);
		}
		finally
		{
			DeleteTempDirectory(directory);
		}
	}

	[TestMethod]
	public async Task MissingFile_UsesNewFileFormatAndCommitCreatesEmptyFile()
	{
		string directory = CreateTempDirectory();
		try
		{
			string path = Path.Combine(directory, "new.lua");
			TextFileFormat newFileFormat = new(TextEncodingKind.Utf16LittleEndian, true, TextNewlineStyle.CrLf);
			WorkspaceDocumentOpenOptions options = new(TextEncodingKind.Utf8, newFileFormat);
			await using var store = new WorkspaceDocumentStore(new WorkspaceFileCodec());

			WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, options)).Snapshot!;
			WorkspaceDocumentCommitResult committed = await store.CommitAsync(CreateCommitRequest(opened));

			Assert.IsFalse(opened.ExistsOnDisk);
			Assert.AreEqual(string.Empty, opened.Content);
			Assert.AreEqual(newFileFormat, opened.FileFormat);
			Assert.IsFalse(opened.IsDirty);
			Assert.IsNull(opened.OnDiskStamp.Length);
			Assert.IsNull(opened.OnDiskStamp.LastWriteTimeUtc);
			Assert.IsNull(opened.OnDiskStamp.ContentHash);
			Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, committed.Status);
			Assert.IsTrue(File.Exists(path));
			Assert.AreEqual(2, new FileInfo(path).Length);
			CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFE }, File.ReadAllBytes(path));
			Assert.IsFalse(committed.Snapshot!.IsDirty);
			Assert.AreEqual(newFileFormat, committed.Snapshot.FileFormat);
		}
		finally
		{
			DeleteTempDirectory(directory);
		}
	}

	[TestMethod]
	public async Task Windows1252Document_PreservesEncodingForLaterNonAsciiEdit()
	{
		string directory = CreateTempDirectory();
		try
		{
			string path = Path.Combine(directory, "strings.txt");
			File.WriteAllBytes(path, [0x6E, 0x61, 0x6D, 0x65]);
			TextFileFormat classicScriptFormat = new(
				TextEncodingKind.Windows1252,
				false,
				TextNewlineStyle.None);
			WorkspaceDocumentOpenOptions options = new(TextEncodingKind.Windows1252, classicScriptFormat);
			await using var store = new WorkspaceDocumentStore(new WorkspaceFileCodec());
			WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, options)).Snapshot!;
			WorkspaceDocumentMutationResult replaced = store.TryReplace(new WorkspaceDocumentReplaceRequest(
				opened.DocumentKey,
				opened.DocumentId,
				opened.Version,
				"café",
				opened.FileFormat));

			WorkspaceDocumentCommitResult committed = await store.CommitAsync(CreateCommitRequest(replaced.Snapshot!));

			Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, committed.Status);
			CollectionAssert.AreEqual(new byte[] { 0x63, 0x61, 0x66, 0xE9 }, File.ReadAllBytes(path));
			Assert.IsFalse(committed.Snapshot!.IsDirty);
		}
		finally
		{
			DeleteTempDirectory(directory);
		}
	}

	[TestMethod]
	public async Task CleanExistingCommit_IsNoOpWithoutWriting()
	{
		var fileSystem = new ControllableFileSystem("disk", s_utf8Format);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync("script.lua", s_utf8Options)).Snapshot!;

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(snapshot));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, result.Status);
		Assert.AreEqual(1, fileSystem.CaptureCount);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.AreEqual(snapshot.Version, result.Snapshot!.PersistedVersion);
	}

	[TestMethod]
	public async Task StaleCommit_IsRejectedWithoutFilesystemWork()
	{
		var fileSystem = new ControllableFileSystem("disk", s_utf8Format);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_utf8Options)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"changed",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(initial));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.StaleDocument, result.Status);
		Assert.AreEqual(changed.Snapshot!.Content, result.Snapshot!.Content);
		Assert.AreEqual(0, fileSystem.CaptureCount);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
	}

	[TestMethod]
	public async Task ObservedExternalConflict_LeavesBaselineUnchanged()
	{
		var fileSystem = new ControllableFileSystem("disk", s_utf8Format);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync("script.lua", s_utf8Options)).Snapshot!;
		fileSystem.CurrentStamp = new FileStamp(true, 8, DateTime.UnixEpoch.AddDays(1), "external");
		WorkspaceDocumentMutationResult changed = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			snapshot.DocumentKey,
			snapshot.DocumentId,
			snapshot.Version,
			"changed",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changedSnapshot));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.ExternalFileConflict, result.Status);
		Assert.AreEqual(fileSystem.CurrentStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(0, fileSystem.WriteTemporaryCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
		Assert.AreEqual(changedSnapshot.PersistedVersion, result.Snapshot.PersistedVersion);
	}

	[TestMethod]
	public async Task CancellationBeforeReplacement_CleansTemporaryFileAndPreservesBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_utf8Format)
		{
			PendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_utf8Options)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"changed",
			initial.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);
		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(CreateCommitRequest(changedSnapshot), cancellation.Token);
		await fileSystem.ReplacementEntered.Task;

		cancellation.Cancel();
		WorkspaceDocumentCommitResult result = await commit;

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Cancelled, result.Status);
		Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
		Assert.IsFalse(fileSystem.ReplacementBegan);
		Assert.AreEqual(changedSnapshot.PersistedVersion, result.Snapshot!.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ReplacementFailure_CleansTemporaryFileAndDoesNotAdvanceBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_utf8Format)
		{
			ReplacementResult = new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.Failed,
				Failure: new WorkspaceOperationFailure("WriteFailed", "write failed"))
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_utf8Options)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"changed",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changed.Snapshot!));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.WriteFailed, result.Status);
		Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
		Assert.AreEqual(initial.PersistedVersion, result.Snapshot.PersistedVersion);
		Assert.IsNotNull(result.Failure);
	}

	[TestMethod]
	public async Task TemporaryWriteFailure_ReturnsWriteFailedAndDoesNotAdvanceBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_utf8Format)
		{
			WriteException = new IOException("flush failed")
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_utf8Options)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"changed",
			initial.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changedSnapshot));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.WriteFailed, result.Status);
		Assert.AreEqual(0, fileSystem.DeleteTemporaryCount);
		Assert.AreEqual(changedSnapshot.PersistedVersion, result.Snapshot!.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.IsNotNull(result.Failure);
	}

	[TestMethod]
	public async Task ReplacementStateUnknown_ReturnsObservedStampAndDoesNotAdvanceBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_utf8Format)
		{
			ReplacementResult = new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.ReplacementStateUnknown,
				new FileStamp(true, 9, DateTime.UnixEpoch.AddDays(2), "unknown"),
				new WorkspaceOperationFailure("ReplacementStateUnknown", "unknown"))
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_utf8Options)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"changed",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changed.Snapshot!));

		Assert.AreEqual(WorkspaceDocumentCommitStatus.ReplacementStateUnknown, result.Status);
		Assert.AreEqual(fileSystem.ReplacementResult.ObservedOnDiskStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(result.ObservedOnDiskStamp, result.Snapshot!.OnDiskStamp);
		Assert.AreEqual(initial.PersistedVersion, result.Snapshot.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
	}

	[TestMethod]
	public async Task EditDuringCommit_InstallsCapturedBaselineAndLeavesLaterEditDirty()
	{
		var fileSystem = new ControllableFileSystem("disk", s_utf8Format)
		{
			PendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_utf8Options)).Snapshot!;
		WorkspaceDocumentMutationResult captured = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"captured",
			initial.FileFormat));
		WorkspaceDocumentSnapshot capturedSnapshot = RequireSnapshot(captured);
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(CreateCommitRequest(capturedSnapshot));
		await fileSystem.ReplacementEntered.Task;
		WorkspaceDocumentMutationResult later = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			capturedSnapshot.Version,
			"later",
			initial.FileFormat));
		fileSystem.PendingReplacement!.SetResult(fileSystem.ReplacementResult);
		WorkspaceDocumentSnapshot laterSnapshot = RequireSnapshot(later);

		WorkspaceDocumentCommitResult result = await commit;

		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, result.Status);
		Assert.AreEqual("later", result.Snapshot!.Content);
		Assert.AreEqual(capturedSnapshot.Version, result.Snapshot.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.AreEqual("captured", fileSystem.WrittenContent);
		Assert.AreEqual(laterSnapshot.Version, result.Snapshot.Version);
	}

	[TestMethod]
	public async Task DiscardAndSecondCommit_ReturnOperationInProgressWithoutSecondIo()
	{
		var fileSystem = new ControllableFileSystem("disk", s_utf8Format)
		{
			PendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync("script.lua", s_utf8Options)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.TryReplace(new WorkspaceDocumentReplaceRequest(
			initial.DocumentKey,
			initial.DocumentId,
			initial.Version,
			"changed",
			initial.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);
		WorkspaceDocumentCommitRequest request = CreateCommitRequest(changedSnapshot);
		Task<WorkspaceDocumentCommitResult> firstCommit = store.CommitAsync(request);
		await fileSystem.ReplacementEntered.Task;

		WorkspaceDocumentMutationResult discard = store.Discard(new WorkspaceDocumentDiscardRequest(
			changedSnapshot.DocumentKey,
			changedSnapshot.DocumentId,
			changedSnapshot.Version));
		WorkspaceDocumentCommitResult secondCommit = await store.CommitAsync(request);
		fileSystem.PendingReplacement!.SetResult(fileSystem.ReplacementResult);
		WorkspaceDocumentCommitResult completed = await firstCommit;

		Assert.AreEqual(WorkspaceDocumentMutationStatus.OperationInProgress, discard.Status);
		Assert.AreEqual(WorkspaceDocumentCommitStatus.OperationInProgress, secondCommit.Status);
		Assert.AreEqual(1, fileSystem.CaptureCount);
		Assert.AreEqual(1, fileSystem.WriteTemporaryCount);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
		Assert.AreEqual(WorkspaceDocumentCommitStatus.Committed, completed.Status);
	}

	private static WorkspaceDocumentCommitRequest CreateCommitRequest(WorkspaceDocumentSnapshot snapshot)
	{
		return new WorkspaceDocumentCommitRequest(
			snapshot.DocumentKey,
			snapshot.DocumentId,
			snapshot.Version,
			snapshot.OnDiskStamp);
	}

	private static WorkspaceDocumentSnapshot RequireSnapshot(WorkspaceDocumentMutationResult result)
	{
		return result.Snapshot ?? throw new AssertFailedException("Expected a mutation snapshot.");
	}

	private static string CreateTempDirectory()
	{
		string path = Path.Combine(Path.GetTempPath(), $"tomb-editor-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	private static void DeleteTempDirectory(string path)
	{
		if (Directory.Exists(path))
			Directory.Delete(path, true);
	}

	private sealed class ControllableFileSystem : IWorkspaceFileSystem
	{
		private readonly string _content;
		private readonly TextFileFormat _fileFormat;

		public ControllableFileSystem(string content, TextFileFormat fileFormat)
		{
			_content = content;
			_fileFormat = fileFormat;
			CurrentStamp = new FileStamp(true, content.Length, DateTime.UnixEpoch, "disk");
			ReplacementResult = new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.Replaced,
				CurrentStamp);
		}

		public FileStamp CurrentStamp { get; set; }

		public WorkspaceFileReplacementResult ReplacementResult { get; set; }

		public TaskCompletionSource<WorkspaceFileReplacementResult>? PendingReplacement { get; set; }

		public TaskCompletionSource<object?> ReplacementEntered { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public bool ReplacementBegan { get; private set; }

		public int CaptureCount { get; private set; }

		public int WriteTemporaryCount { get; private set; }

		public int ReplaceCount { get; private set; }

		public int DeleteTemporaryCount { get; private set; }

		public string? WrittenContent { get; private set; }

		public Exception? WriteException { get; set; }

		public Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken)
		{
			return Task.FromResult(new WorkspaceFileReadResult(_content, _fileFormat, CurrentStamp));
		}

		public Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken)
		{
			CaptureCount++;
			return Task.FromResult(CurrentStamp);
		}

		public Task<WorkspaceTemporaryFile> WriteTemporaryAsync(
			string directory,
			ReadOnlyMemory<byte> content,
			CancellationToken cancellationToken)
		{
			WriteTemporaryCount++;
			if (WriteException is not null)
				throw WriteException;

			WrittenContent = Encoding.UTF8.GetString(content.Span);
			return Task.FromResult(new WorkspaceTemporaryFile(
				"fake.tmp",
				content.Length,
				Convert.ToHexString(SHA256.HashData(content.Span))));
		}

		public async Task<WorkspaceFileReplacementResult> ReplaceAsync(
			WorkspaceTemporaryFile temporaryFile,
			string destinationPath,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			ReplaceCount++;
			ReplacementEntered.TrySetResult(null);

			if (PendingReplacement is not null)
				await PendingReplacement.Task.WaitAsync(cancellationToken);

			ReplacementBegan = true;
			return ReplacementResult;
		}

		public Task DeleteTemporaryAsync(WorkspaceTemporaryFile temporaryFile)
		{
			DeleteTemporaryCount++;
			return Task.CompletedTask;
		}

		public Task<WorkspaceFileMoveResult> MoveAsync(
			string sourcePath,
			string destinationPath,
			FileStamp expectedSourceStamp,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
			string sourcePath,
			string destinationPath,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<WorkspaceFileDeleteResult> DeleteAsync(
			string path,
			FileStamp expectedStamp,
			CancellationToken cancellationToken,
			bool useRecycleBin = false)
			=> throw new NotSupportedException();

		public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
			string path,
			CancellationToken cancellationToken,
			bool useRecycleBin = false)
			=> throw new NotSupportedException();
	}
}
