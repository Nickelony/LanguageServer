using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Encodes and decodes workspace text files and provides the default file-system implementation used
/// by the document store.
/// </summary>
public sealed class WorkspaceFileCodec : IWorkspaceFileSystem
{
	private static readonly byte[] s_utf8Bom = [0xEF, 0xBB, 0xBF];
	private static readonly byte[] s_utf16LittleEndianBom = [0xFF, 0xFE];
	private static readonly byte[] s_utf16BigEndianBom = [0xFE, 0xFF];
	private static readonly byte[] s_utf32LittleEndianBom = [0xFF, 0xFE, 0x00, 0x00];
	private static readonly byte[] s_utf32BigEndianBom = [0x00, 0x00, 0xFE, 0xFF];

	static WorkspaceFileCodec()
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
	}

	/// <summary>Decodes bytes, detects their text format, and records the newline style.</summary>
	/// <remarks>
	/// A recognized UTF-8 or UTF-16 byte-order mark takes precedence over <paramref name="noBomEncoding"/>.
	/// Bytes without a recognized mark use <paramref name="noBomEncoding"/>. UTF-32 and invalid byte
	/// sequences are rejected with a <see cref="DecoderFallbackException"/>.
	/// </remarks>
	public static string Decode(ReadOnlySpan<byte> bytes, TextEncodingKind noBomEncoding, out TextFileFormat fileFormat)
	{
		(TextEncodingKind encoding, bool hasBom, int preambleLength) = DetectEncoding(bytes, noBomEncoding);
		Encoding decoder = GetEncoding(encoding);
		ReadOnlySpan<byte> content = bytes[preambleLength..];

		if (encoding == TextEncodingKind.Windows1252)
			RejectUndefinedWindows1252Bytes(content);

		string text = decoder.GetString(content);
		fileFormat = new TextFileFormat(encoding, hasBom, DetectNewlineStyle(text));
		return text;
	}

	/// <summary>Encodes text using the specified encoding and byte-order-mark setting.</summary>
	/// <remarks>
	/// The content is encoded as supplied; <see cref="TextFileFormat.NewlineStyle"/> is metadata and
	/// does not normalize newline characters. Windows-1252 can be written only without a byte-order mark.
	/// </remarks>
	public static byte[] Encode(string content, TextFileFormat fileFormat)
	{
		ArgumentNullException.ThrowIfNull(content);

		if (fileFormat.HasBom && fileFormat.Encoding == TextEncodingKind.Windows1252)
			throw new ArgumentException("Windows-1252 does not support a BOM.", nameof(fileFormat));

		Encoding encoder = GetEncoding(fileFormat.Encoding);
		byte[] encoded = encoder.GetBytes(content);

		if (!fileFormat.HasBom)
			return encoded;

		byte[] preamble = GetPreamble(fileFormat.Encoding);
		byte[] result = new byte[preamble.Length + encoded.Length];
		Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
		Buffer.BlockCopy(encoded, 0, result, preamble.Length, encoded.Length);
		return result;
	}

	/// <summary>Detects the newline style used by text content without changing the content.</summary>
	public static TextNewlineStyle DetectNewlineStyle(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		bool hasCrLf = false;
		bool hasLf = false;
		bool hasCr = false;

		for (int index = 0; index < content.Length; index++)
		{
			if (content[index] == '\r')
			{
				if (index + 1 < content.Length && content[index + 1] == '\n')
				{
					hasCrLf = true;
					index++;
				}
				else
				{
					hasCr = true;
				}
			}
			else if (content[index] == '\n')
			{
				hasLf = true;
			}
		}

		int styles = (hasCrLf ? 1 : 0) + (hasLf ? 1 : 0) + (hasCr ? 1 : 0);
		if (styles == 0)
			return TextNewlineStyle.None;
		if (styles > 1)
			return TextNewlineStyle.Mixed;
		if (hasCrLf)
			return TextNewlineStyle.CrLf;
		if (hasLf)
			return TextNewlineStyle.Lf;
		return TextNewlineStyle.Cr;
	}

	/// <summary>Reads raw bytes from a file and captures their content stamp.</summary>
	/// <remarks>
	/// For an existing file, <see cref="WorkspaceFileReadResult.RawBytes"/> contains the bytes and
	/// <see cref="WorkspaceFileReadResult.Content"/> is empty; decoding is deferred to the document
	/// store. A missing file returns <see cref="FileStamp.Missing"/>.
	/// </remarks>
	public async Task<WorkspaceFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);

		cancellationToken.ThrowIfCancellationRequested();

		if (!File.Exists(path))
			return new WorkspaceFileReadResult(string.Empty, default, FileStamp.Missing);

		byte[] bytes = await ReadBytesAsync(path, cancellationToken).ConfigureAwait(false);
		FileStamp stamp = await CaptureStampFromBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
		return new WorkspaceFileReadResult(string.Empty, default, stamp, bytes);
	}

	/// <summary>Captures a file's current byte length, last-write time, and content hash.</summary>
	public async Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(path);

		cancellationToken.ThrowIfCancellationRequested();

		if (!File.Exists(path))
			return FileStamp.Missing;

		byte[] bytes = await ReadBytesAsync(path, cancellationToken).ConfigureAwait(false);
		return await CaptureStampFromBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Writes bytes to and flushes a uniquely named temporary file in the specified directory.</summary>
	/// <remarks>The temporary file is not removed on success; the caller must delete it.</remarks>
	public async Task<WorkspaceTemporaryFile> WriteTemporaryAsync(
		string directory,
		ReadOnlyMemory<byte> content,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(directory);

		string path = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
		try
		{
			await using FileStream stream = new(
				path,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None,
				64 * 1024,
				FileOptions.Asynchronous | FileOptions.SequentialScan);
			await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
			await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
			await stream.DisposeAsync().ConfigureAwait(false);

			return new WorkspaceTemporaryFile(path, content.Length, Convert.ToHexString(SHA256.HashData(content.Span)));
		}
		catch
		{
			File.Delete(path);
			throw;
		}
	}

	/// <summary>Replaces a destination file after validating its expected stamp.</summary>
	/// <remarks>
	/// Existing files are replaced; missing destinations are created. If the replacement throws after
	/// the operation begins, the result can be <see cref="WorkspaceFileReplacementStatus.ReplacementStateUnknown"/>
	/// because the final on-disk state cannot be established reliably.
	/// </remarks>
	public async Task<WorkspaceFileReplacementResult> ReplaceAsync(
		WorkspaceTemporaryFile temporaryFile,
		string destinationPath,
		FileStamp expectedStamp,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(temporaryFile);
		ArgumentNullException.ThrowIfNull(destinationPath);

		FileStamp actualStamp = await CaptureStampAsync(destinationPath, cancellationToken).ConfigureAwait(false);
		if (actualStamp != expectedStamp)
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.ExternalFileConflict,
				actualStamp);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (expectedStamp.Exists)
				File.Replace(temporaryFile.Path, destinationPath, null);
			else
				File.Move(temporaryFile.Path, destinationPath);

			DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(destinationPath);
			FileStamp replacementStamp = new(
				true,
				temporaryFile.Length,
				lastWriteTimeUtc,
				temporaryFile.ContentHash);
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.Replaced,
				replacementStamp);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (IOException exception)
		{
			FileStamp? observedOnDiskStamp = await TryCaptureStampAsync(destinationPath).ConfigureAwait(false);
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.ReplacementStateUnknown,
				observedOnDiskStamp,
				Failure: new WorkspaceOperationFailure("ReplacementStateUnknown", exception.Message, exception));
		}
		catch (PlatformNotSupportedException exception)
		{
			FileStamp? observedOnDiskStamp = await TryCaptureStampAsync(destinationPath).ConfigureAwait(false);
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.ReplacementStateUnknown,
				observedOnDiskStamp,
				Failure: new WorkspaceOperationFailure("ReplacementStateUnknown", exception.Message, exception));
		}
		catch (UnauthorizedAccessException exception)
		{
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementStatus.Failed,
				Failure: new WorkspaceOperationFailure("WriteFailed", exception.Message, exception));
		}
	}

	/// <summary>Moves a file after validating its expected source stamp.</summary>
	/// <remarks>Windows case-only renames use a temporary intermediate path.</remarks>
	public async Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(sourcePath);
		ArgumentNullException.ThrowIfNull(destinationPath);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			FileStamp actualSourceStamp = await CaptureStampAsync(sourcePath, cancellationToken).ConfigureAwait(false);
			if (actualSourceStamp != expectedSourceStamp)
				return new WorkspaceFileMoveResult(
					WorkspaceFileMoveStatus.ExternalFileConflict,
					actualSourceStamp);

			bool isCaseOnlyWindowsRename = OperatingSystem.IsWindows()
				&& string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(sourcePath, destinationPath, StringComparison.Ordinal);
			if (!isCaseOnlyWindowsRename && (File.Exists(destinationPath) || Directory.Exists(destinationPath)))
				return new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.DestinationExists);

			if (isCaseOnlyWindowsRename)
			{
				string intermediatePath = sourcePath + "." + Guid.NewGuid().ToString("N") + ".rename";
				File.Move(sourcePath, intermediatePath);
				try
				{
					File.Move(intermediatePath, destinationPath);
				}
				catch
				{
					if (File.Exists(intermediatePath))
						File.Move(intermediatePath, sourcePath);

					throw;
				}
			}
			else
			{
				File.Move(sourcePath, destinationPath);
			}

			return new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved);
		}
		catch (OperationCanceledException)
		{
			return new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Cancelled);
		}
		catch (IOException exception) when (File.Exists(destinationPath) || Directory.Exists(destinationPath))
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.DestinationExists,
				Failure: new WorkspaceOperationFailure("DestinationExists", exception.Message, exception));
		}
		catch (Exception exception)
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.MoveFailed,
				Failure: new WorkspaceOperationFailure("MoveFailed", exception.Message, exception));
		}
	}

	/// <summary>Deletes a file after validating its expected stamp.</summary>
	/// <remarks>
	/// When <paramref name="useRecycleBin"/> is <see langword="true"/>, the Windows shell recycle-bin
	/// operation is requested; otherwise the file is permanently deleted.
	/// </remarks>
	public async Task<WorkspaceFileDeleteResult> DeleteAsync(
		string path,
		FileStamp expectedStamp,
		CancellationToken cancellationToken,
		bool useRecycleBin = false)
	{
		ArgumentNullException.ThrowIfNull(path);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			FileStamp actualStamp = await CaptureStampAsync(path, cancellationToken).ConfigureAwait(false);
			if (actualStamp != expectedStamp)
				return new WorkspaceFileDeleteResult(
					WorkspaceFileDeleteStatus.ExternalFileConflict,
					actualStamp);

			if (File.Exists(path))
			{
				if (useRecycleBin)
					FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
				else
					File.Delete(path);
			}

			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted);
		}
		catch (OperationCanceledException)
		{
			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Cancelled);
		}
		catch (Exception exception)
		{
			return new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteStatus.DeleteFailed,
				Failure: new WorkspaceOperationFailure("DeleteFailed", exception.Message, exception));
		}
	}

	/// <summary>Moves a directory, including its contents, without a source-stamp check.</summary>
	public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
		string sourcePath,
		string destinationPath,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(sourcePath);
		ArgumentNullException.ThrowIfNull(destinationPath);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Directory.Exists(sourcePath))
				return Task.FromResult(new WorkspaceFileMoveResult(
					WorkspaceFileMoveStatus.MoveFailed,
					Failure: new WorkspaceOperationFailure("MoveFailed", "The source directory does not exist.")));

			bool isCaseOnlyWindowsRename = OperatingSystem.IsWindows()
				&& string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(sourcePath, destinationPath, StringComparison.Ordinal);
			if (!isCaseOnlyWindowsRename && (Directory.Exists(destinationPath) || File.Exists(destinationPath)))
				return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.DestinationExists));

			if (isCaseOnlyWindowsRename)
			{
				string intermediatePath = sourcePath + "." + Guid.NewGuid().ToString("N") + ".rename";
				Directory.Move(sourcePath, intermediatePath);
				try
				{
					Directory.Move(intermediatePath, destinationPath);
				}
				catch
				{
					if (Directory.Exists(intermediatePath))
						Directory.Move(intermediatePath, sourcePath);

					throw;
				}
			}
			else
			{
				Directory.Move(sourcePath, destinationPath);
			}

			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Moved));
		}
		catch (OperationCanceledException)
		{
			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveStatus.Cancelled));
		}
		catch (IOException exception) when (Directory.Exists(destinationPath) || File.Exists(destinationPath))
		{
			return Task.FromResult(new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.DestinationExists,
				Failure: new WorkspaceOperationFailure("DestinationExists", exception.Message, exception)));
		}
		catch (Exception exception)
		{
			return Task.FromResult(new WorkspaceFileMoveResult(
				WorkspaceFileMoveStatus.MoveFailed,
				Failure: new WorkspaceOperationFailure("MoveFailed", exception.Message, exception)));
		}
	}

	/// <summary>Deletes a directory recursively.</summary>
	/// <remarks>
	/// When <paramref name="useRecycleBin"/> is <see langword="true"/>, the Windows shell recycle-bin
	/// operation is requested; otherwise the directory is permanently deleted.
	/// </remarks>
	public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
		string path,
		CancellationToken cancellationToken,
		bool useRecycleBin = false)
	{
		ArgumentNullException.ThrowIfNull(path);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (Directory.Exists(path))
			{
				if (useRecycleBin)
					FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
				else
					Directory.Delete(path, recursive: true);
			}

			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Deleted));
		}
		catch (OperationCanceledException)
		{
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteStatus.Cancelled));
		}
		catch (Exception exception)
		{
			return Task.FromResult(new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteStatus.DeleteFailed,
				Failure: new WorkspaceOperationFailure("DeleteFailed", exception.Message, exception)));
		}
	}

	/// <summary>Deletes a temporary file created by <see cref="WriteTemporaryAsync"/>.</summary>
	public Task DeleteTemporaryAsync(WorkspaceTemporaryFile temporaryFile)
	{
		ArgumentNullException.ThrowIfNull(temporaryFile);

		File.Delete(temporaryFile.Path);
		return Task.CompletedTask;
	}

	private static async Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = new(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			64 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		using MemoryStream buffer = new();
		await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
		return buffer.ToArray();
	}

	private static async Task<FileStamp> CaptureStampFromBytesAsync(
		string path,
		ReadOnlyMemory<byte> bytes,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
		return await Task.FromResult(new FileStamp(
			true,
			bytes.Length,
			lastWriteTimeUtc,
			Convert.ToHexString(SHA256.HashData(bytes.Span)))).ConfigureAwait(false);
	}

	private static async Task<FileStamp?> TryCaptureStampAsync(string path)
	{
		try
		{
			if (!File.Exists(path))
				return FileStamp.Missing;

			byte[] bytes = await ReadBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
			return new FileStamp(
				true,
				bytes.Length,
				File.GetLastWriteTimeUtc(path),
				Convert.ToHexString(SHA256.HashData(bytes)));
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static (TextEncodingKind Encoding, bool HasBom, int PreambleLength) DetectEncoding(
		ReadOnlySpan<byte> bytes,
		TextEncodingKind noBomEncoding)
	{
		if (bytes.StartsWith(s_utf32LittleEndianBom) || bytes.StartsWith(s_utf32BigEndianBom))
			throw new DecoderFallbackException("UTF-32 input is not supported.");
		if (bytes.StartsWith(s_utf8Bom))
			return (TextEncodingKind.Utf8, true, s_utf8Bom.Length);
		if (bytes.StartsWith(s_utf16LittleEndianBom))
			return (TextEncodingKind.Utf16LittleEndian, true, s_utf16LittleEndianBom.Length);
		if (bytes.StartsWith(s_utf16BigEndianBom))
			return (TextEncodingKind.Utf16BigEndian, true, s_utf16BigEndianBom.Length);
		return (noBomEncoding, false, 0);
	}

	private static Encoding GetEncoding(TextEncodingKind encoding)
	{
		return encoding switch
		{
			TextEncodingKind.Utf8 => new UTF8Encoding(false, true),
			TextEncodingKind.Utf16LittleEndian => new UnicodeEncoding(false, false, true),
			TextEncodingKind.Utf16BigEndian => new UnicodeEncoding(true, false, true),
			TextEncodingKind.Windows1252 => Encoding.GetEncoding(
				1252,
				EncoderFallback.ExceptionFallback,
				DecoderFallback.ExceptionFallback),
			_ => throw new ArgumentOutOfRangeException(nameof(encoding))
		};
	}

	private static byte[] GetPreamble(TextEncodingKind encoding)
	{
		return encoding switch
		{
			TextEncodingKind.Utf8 => s_utf8Bom,
			TextEncodingKind.Utf16LittleEndian => s_utf16LittleEndianBom,
			TextEncodingKind.Utf16BigEndian => s_utf16BigEndianBom,
			TextEncodingKind.Windows1252 => [],
			_ => throw new ArgumentOutOfRangeException(nameof(encoding))
		};
	}

	private static void RejectUndefinedWindows1252Bytes(ReadOnlySpan<byte> bytes)
	{
		foreach (byte value in bytes)
		{
			if (value is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D)
				throw new DecoderFallbackException($"Undefined Windows-1252 byte 0x{value:X2}.");
		}
	}
}
