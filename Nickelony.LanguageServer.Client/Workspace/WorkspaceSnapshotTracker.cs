using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Tracks the subset of workspace paths mirrored to a language server so watcher recovery can reconcile missed file changes.
/// </summary>
/// <remarks>
/// <para>
/// Capture follows the watch specifications and includes hidden and system files because the file-system watcher
/// observes them as well; reparse points (symbolic links and junctions) are excluded because neither capture nor the
/// watcher follows them consistently. Enumeration problems such as a directory that disappears mid-capture are
/// contained and reported at debug level, so capture does not throw for file-system races.
/// </para>
/// </remarks>
public sealed class WorkspaceSnapshotTracker
{
	/// <summary>
	/// The largest file whose content participates in the fingerprint. Larger files fall back to a zero fingerprint so
	/// a change burst cannot stall the dispatch path on multi-megabyte reads.
	/// </summary>
	private const long MaxFingerprintFileLength = 16 * 1024 * 1024;

	private readonly ILogger _logger;

	private readonly string _workspaceRootDirectoryPath;
	private readonly ReadOnlyCollection<WorkspaceWatchSpecification> _watchSpecifications;
	private readonly object _snapshotSyncRoot = new();
	private Dictionary<string, WorkspaceSnapshotEntry> _trackedSnapshot = new(LanguageServerPaths.LocalPathComparer);

	// Bumped whenever a capture replaces the tracked snapshot; ApplyChanges re-validates against it so an update
	// computed against an older snapshot cannot clobber a newer capture.
	private long _snapshotVersion;

	// Apply-order tickets: a batch that started computing earlier must not overwrite entries committed by a batch
	// that started later, so commits are ordered per path by the ticket assigned when the batch read the snapshot
	// version. Tickets are cleared whenever a capture replaces the snapshot.
	private long _nextApplyTicket;
	private readonly Dictionary<string, long> _lastCommittedApplyTickets = new(LanguageServerPaths.LocalPathComparer);

	/// <summary>
	/// Gets or sets an optional test seam invoked after the version that <see cref="ApplyChanges"/> read was
	/// captured and before the updates are computed, so tests can replace the tracked snapshot deterministically
	/// in between.
	/// </summary>
	/// <remarks>Internal seam used by the package tests; not part of the supported surface.</remarks>
	internal Action? SnapshotVersionReadTestHook { get; set; }

	/// <summary>
	/// Gets or sets an optional test seam invoked after the changed-file updates were computed and before they are
	/// committed, so tests can interleave a competing apply deterministically.
	/// </summary>
	/// <remarks>Internal seam used by the package tests; not part of the supported surface.</remarks>
	internal Action? BeforeApplyCommitTestHook { get; set; }

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceSnapshotTracker"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root directory whose files are captured into the tracked snapshot.</param>
	/// <param name="watchSpecifications">The file patterns that should participate in the tracked snapshot. The list is copied on assignment.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPath"/> is empty or whitespace-only,
	/// <paramref name="watchSpecifications"/> is empty, or one of the specification filters is <see langword="null"/>,
	/// empty, or whitespace-only, is rooted, or contains a directory separator.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="workspaceRootDirectoryPath"/> or <paramref name="watchSpecifications"/> is <see langword="null"/>.</exception>
	public WorkspaceSnapshotTracker(string workspaceRootDirectoryPath, IReadOnlyList<WorkspaceWatchSpecification> watchSpecifications, ILogger? logger = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootDirectoryPath);
		ArgumentNullException.ThrowIfNull(watchSpecifications);

		if (watchSpecifications.Count == 0)
			throw new ArgumentException("At least one watch specification is required.", nameof(watchSpecifications));

		for (int i = 0; i < watchSpecifications.Count; i++)
			WorkspaceWatchSpecification.Validate(watchSpecifications[i], nameof(watchSpecifications));

		_logger = logger ?? NullLogger.Instance;

		_workspaceRootDirectoryPath = workspaceRootDirectoryPath;
		_watchSpecifications = Array.AsReadOnly([.. watchSpecifications]);
	}

	/// <summary>
	/// Replaces the tracked snapshot with a fresh capture of the current workspace state.
	/// </summary>
	public void CaptureTrackedSnapshot()
	{
		Dictionary<string, WorkspaceSnapshotEntry> snapshot = CaptureSnapshot();

		lock (_snapshotSyncRoot)
		{
			_trackedSnapshot = snapshot;
			_snapshotVersion++;

			// The version bump rejects every apply that read an older version, so the per-path tickets of the
			// replaced snapshot are no longer needed.
			_lastCommittedApplyTickets.Clear();
		}
	}

	/// <summary>
	/// Creates a stable clone of the currently tracked workspace snapshot.
	/// </summary>
	/// <returns>The cloned snapshot.</returns>
	public Dictionary<string, WorkspaceSnapshotEntry> CloneTrackedSnapshot()
	{
		lock (_snapshotSyncRoot)
			return CloneSnapshot(_trackedSnapshot);
	}

	/// <summary>
	/// Captures the current workspace state, replaces the tracked snapshot, and returns a stable clone of the fresh snapshot.
	/// </summary>
	/// <returns>The newly captured snapshot as a caller-owned clone that later tracked changes cannot mutate.</returns>
	public Dictionary<string, WorkspaceSnapshotEntry> ReplaceTrackedSnapshotWithCurrent()
	{
		Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot = CaptureSnapshot();

		lock (_snapshotSyncRoot)
		{
			_trackedSnapshot = currentSnapshot;
			_snapshotVersion++;

			// The version bump rejects every apply that read an older version, so the per-path tickets of the
			// replaced snapshot are no longer needed.
			_lastCommittedApplyTickets.Clear();

			// The tracked snapshot is mutated in place by later ApplyChanges calls; the clone must run under the
			// snapshot lock, otherwise a concurrent apply can mutate the dictionary while the clone enumerates it.
			return CloneSnapshot(currentSnapshot);
		}
	}

	/// <summary>
	/// Applies a set of already forwarded file changes to the tracked snapshot.
	/// </summary>
	/// <param name="changes">The normalized forwarded file changes. Entries without a usable path are ignored.</param>
	/// <remarks>
	/// Concurrent calls are ordered per path by the ticket captured when each call read the snapshot version, so a
	/// batch that started computing earlier cannot overwrite entries that a later-started batch already committed for
	/// the same path; observations for unrelated paths are still applied. The file reads and content hashing for one
	/// call run outside the snapshot lock.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
	public void ApplyChanges(IReadOnlyList<WorkspaceFileChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);

		long startingSnapshotVersion;
		long applyTicket;

		lock (_snapshotSyncRoot)
		{
			startingSnapshotVersion = _snapshotVersion;
			applyTicket = ++_nextApplyTicket;
		}

		SnapshotVersionReadTestHook?.Invoke();

		// File reads and content hashing happen outside the lock; only the snapshot mutation is serialized.
		var updates = new List<(string Path, WorkspaceSnapshotEntry? Entry)>(changes.Count);

		for (int i = 0; i < changes.Count; i++)
		{
			WorkspaceFileChange change = changes[i];

			if (string.IsNullOrWhiteSpace(change.Path))
			{
				_logger.LogDebug("Ignored a {Kind} workspace change without a usable path.", change.Kind);
				continue;
			}

			if (change.Kind == FileChangeKind.Deleted)
			{
				updates.Add((change.Path, null));
				continue;
			}

			if (TryCreateSnapshotEntry(change.Path, out WorkspaceSnapshotEntry entry, _logger))
			{
				updates.Add((change.Path, entry));
			}
			else if (TryDeterminePathMissing(change.Path, out bool isMissing, _logger) && isMissing)
			{
				updates.Add((change.Path, null));
			}
		}

		BeforeApplyCommitTestHook?.Invoke();

		lock (_snapshotSyncRoot)
		{
			// A capture that replaced the snapshot while these updates were computed already reflects the current
			// file-system state, so applying the older lock-free updates would clobber it.
			if (_snapshotVersion != startingSnapshotVersion)
			{
				_logger.LogDebug("Skipped {Count} tracked workspace change(s) because the tracked snapshot was replaced while their updates were being computed.", updates.Count);
				return;
			}

			// Commits are ordered per path: a concurrent apply that started later may already have committed newer
			// file-system observations for some paths, and only those paths are skipped here; observations for
			// unrelated paths from this (older) batch are still applied.
			int skippedUpdateCount = 0;

			foreach ((string path, WorkspaceSnapshotEntry? entry) in updates)
			{
				if (_lastCommittedApplyTickets.TryGetValue(path, out long committedApplyTicket) && committedApplyTicket > applyTicket)
				{
					skippedUpdateCount++;
					continue;
				}

				if (entry is WorkspaceSnapshotEntry capturedEntry)
					_trackedSnapshot[path] = capturedEntry;
				else
					_trackedSnapshot.Remove(path);

				_lastCommittedApplyTickets[path] = applyTicket;
			}

			if (skippedUpdateCount > 0)
			{
				_logger.LogDebug("Skipped {Count} tracked workspace change(s) because a newer apply already committed those paths.", skippedUpdateCount);
			}
		}
	}

	/// <summary>
	/// Builds a normalized delta batch between two captured workspace snapshots.
	/// </summary>
	/// <param name="previousSnapshot">The baseline snapshot captured before watcher disruption.</param>
	/// <param name="currentSnapshot">The replacement snapshot captured after watcher recovery.</param>
	/// <returns>The sorted workspace change batch needed to reconcile the snapshots.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="previousSnapshot"/> or <paramref name="currentSnapshot"/> is <see langword="null"/>.</exception>
	public static FileChangeBatch BuildDeltaBatch(
		IReadOnlyDictionary<string, WorkspaceSnapshotEntry> previousSnapshot,
		IReadOnlyDictionary<string, WorkspaceSnapshotEntry> currentSnapshot)
	{
		ArgumentNullException.ThrowIfNull(previousSnapshot);
		ArgumentNullException.ThrowIfNull(currentSnapshot);

		var changes = new List<WorkspaceFileChange>();

		foreach ((string path, WorkspaceSnapshotEntry previousEntry) in previousSnapshot)
		{
			if (!currentSnapshot.TryGetValue(path, out WorkspaceSnapshotEntry currentEntry))
			{
				if (TryDeterminePathMissing(path, out bool isMissing) && isMissing)
					changes.Add(new WorkspaceFileChange(path, FileChangeKind.Deleted));

				continue;
			}

			if (!currentEntry.Equals(previousEntry))
				changes.Add(new WorkspaceFileChange(path, FileChangeKind.Changed));
		}

		foreach ((string path, WorkspaceSnapshotEntry _) in currentSnapshot)
		{
			if (!previousSnapshot.ContainsKey(path))
				changes.Add(new WorkspaceFileChange(path, FileChangeKind.Created));
		}

		changes.Sort(static (left, right) => LanguageServerPaths.LocalPathComparer.Compare(left.Path, right.Path));
		return new(changes);
	}

	private Dictionary<string, WorkspaceSnapshotEntry> CaptureSnapshot()
	{
		var snapshot = new Dictionary<string, WorkspaceSnapshotEntry>(LanguageServerPaths.LocalPathComparer);

		if (!Directory.Exists(_workspaceRootDirectoryPath))
			return snapshot;

		for (int i = 0; i < _watchSpecifications.Count; i++)
			CaptureSnapshotForSpecification(snapshot, _watchSpecifications[i]);

		return snapshot;
	}

	private void CaptureSnapshotForSpecification(
		Dictionary<string, WorkspaceSnapshotEntry> snapshot,
		WorkspaceWatchSpecification watchSpecification)
	{
		try
		{
			if (watchSpecification.Filter.IndexOfAny(['*', '?']) < 0)
			{
				TryAddSnapshotPath(snapshot, Path.Combine(_workspaceRootDirectoryPath, watchSpecification.Filter));
				return;
			}

			var enumerationOptions = new EnumerationOptions
			{
				IgnoreInaccessible = true,
				RecurseSubdirectories = watchSpecification.IncludeSubdirectories,
				ReturnSpecialDirectories = false,
				// Hidden and system files are captured because the file-system watcher observes them; reparse points
				// are skipped so capture cannot escape the workspace through symbolic links or junctions.
				AttributesToSkip = FileAttributes.ReparsePoint,
				// Capture must apply the same simple wildcard grammar the watch specification matcher uses, so the
				// captured scope and the forwarded-event scope cannot disagree on exotic patterns.
				MatchType = MatchType.Simple
			};

			foreach (string filePath in Directory.EnumerateFiles(_workspaceRootDirectoryPath, watchSpecification.Filter, enumerationOptions))
				TryAddSnapshotPath(snapshot, filePath);
		}
		catch (Exception exception)
		{
			// Enumeration races (a directory removed mid-capture) and access failures are contained here so provider
			// start and recovery never fail because of file-system churn; the next capture picks the state up.
			_logger.LogDebug(exception, "Failed to enumerate workspace files for filter '{Filter}' under '{Workspace}'.", watchSpecification.Filter, _workspaceRootDirectoryPath);
		}
	}

	private void TryAddSnapshotPath(Dictionary<string, WorkspaceSnapshotEntry> snapshot, string path)
	{
		if (!LanguageServerPaths.TryNormalizeLocalPath(path, out string normalizedPath))
			return;

		if (TryCreateSnapshotEntry(normalizedPath, out WorkspaceSnapshotEntry entry, _logger))
			snapshot[normalizedPath] = entry;
	}

	private static Dictionary<string, WorkspaceSnapshotEntry> CloneSnapshot(Dictionary<string, WorkspaceSnapshotEntry> snapshot)
		=> new(snapshot, LanguageServerPaths.LocalPathComparer);

	private static bool TryCreateSnapshotEntry(string normalizedPath, out WorkspaceSnapshotEntry entry, ILogger? logger = null)
	{
		entry = default;

		try
		{
			if (File.Exists(normalizedPath))
			{
				var fileInfo = new FileInfo(normalizedPath);

				entry = new WorkspaceSnapshotEntry(
					IsDirectory: false,
					fileInfo.LastWriteTimeUtc.Ticks,
					fileInfo.Length,
					ComputeFileContentFingerprint(normalizedPath));

				return true;
			}

			if (Directory.Exists(normalizedPath))
			{
				var directoryInfo = new DirectoryInfo(normalizedPath);
				entry = new WorkspaceSnapshotEntry(IsDirectory: true, directoryInfo.LastWriteTimeUtc.Ticks, 0, 0);
				return true;
			}
		}
		catch (Exception exception)
		{
			logger?.LogDebug(exception, "Failed to capture a workspace snapshot entry for '{Path}'.", normalizedPath);
		}

		return false;
	}

	/// <summary>
	/// Computes a compact content fingerprint for one file. The fingerprint disambiguates entries whose timestamp
	/// and length are equal, for example when a file is rewritten twice within one file-system timestamp tick.
	/// </summary>
	/// <remarks>
	/// Files larger than <see cref="MaxFingerprintFileLength"/> fall back to a zero fingerprint so a change burst cannot
	/// stall the dispatch path on multi-megabyte reads; such entries are compared by timestamp and length alone, which
	/// can miss a rewrite of the same length within one file-system timestamp tick.
	/// </remarks>
	/// <param name="normalizedPath">The normalized file path.</param>
	/// <returns>The first eight bytes of the file's SHA-256 hash, or <c>0</c> for oversized files.</returns>
	private static ulong ComputeFileContentFingerprint(string normalizedPath)
	{
		using FileStream stream = File.OpenRead(normalizedPath);

		if (stream.Length > MaxFingerprintFileLength)
			return 0;

		byte[] hash = SHA256.HashData(stream);
		return BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, sizeof(ulong)));
	}

	/// <summary>
	/// Determines whether a normalized path is missing from the file system.
	/// </summary>
	/// <param name="normalizedPath">The normalized file path.</param>
	/// <param name="isMissing">Receives whether the path does not exist.</param>
	/// <param name="logger">The logger used for unexpected access failures.</param>
	/// <returns><see langword="true"/> when the missing state was resolved; otherwise, <see langword="false"/>.</returns>
	private static bool TryDeterminePathMissing(string normalizedPath, out bool isMissing, ILogger? logger = null)
	{
		isMissing = false;

		try
		{
			_ = File.GetAttributes(normalizedPath);
			return true;
		}
		catch (DirectoryNotFoundException)
		{
			isMissing = true;
			return true;
		}
		catch (FileNotFoundException)
		{
			isMissing = true;
			return true;
		}
		catch (Exception exception)
		{
			logger?.LogDebug(exception, "Failed to determine whether the workspace path '{Path}' is missing.", normalizedPath);
			return false;
		}
	}
}

/// <summary>
/// Represents the tracked file-system state for a single watched workspace path.
/// </summary>
/// <param name="IsDirectory">Whether the path represents a directory.</param>
/// <param name="LastWriteUtcTicks">The last-write timestamp used for change detection.</param>
/// <param name="Length">The file length used for change detection.</param>
/// <param name="ContentFingerprint">A stable file-content fingerprint used when length and timestamps alone are ambiguous.</param>
public readonly record struct WorkspaceSnapshotEntry(bool IsDirectory, long LastWriteUtcTicks, long Length, ulong ContentFingerprint);
