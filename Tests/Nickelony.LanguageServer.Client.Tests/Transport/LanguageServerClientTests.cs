using StreamJsonRpc;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public partial class LanguageServerClientTests
{
	// Shared defaults used by process-lifetime tests: shorter timeouts keep the real-process tail cheap without
	// weakening the assertions (a delayed JSON-RPC ack still completes well inside these budgets).
	private static readonly LanguageServerClientOptions s_defaultClientOptions = new(static () => new { })
	{
		ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
		DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000)
	};

	private static bool HasAbandonedNotificationLog(TestLoggerScope logScope)
		=> logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("abandoned language server transport task", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated abandoned notification failure.", StringComparison.Ordinal));

	[TestMethod]
	public async Task DisposeAsync_WaitsForDetachedSessionCleanup()
	{
		var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		var queuedCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.TransportHost.QueuedFailedSessionCleanupTask = queuedCleanup.Task;

		Task disposeTask = client.DisposeAsync().AsTask();
		Task completedTask = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromMilliseconds(200))).ConfigureAwait(false);

		Assert.AreNotSame(disposeTask, completedTask);

		queuedCleanup.TrySetResult(true);
		await disposeTask.ConfigureAwait(false);
	}

	private static Process StartDisposableProcess()
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			Arguments = "/c ping 127.0.0.1 -n 10 > nul",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		return Process.Start(startInfo)
			?? throw new InvalidOperationException("Unable to start the disposable test process.");
	}

	private static string CreateJsonRpcResultMessage(int id, string resultJson)
	{
		string payload = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + resultJson + "}";
		int payloadLength = Encoding.UTF8.GetByteCount(payload);
		return "Content-Length: " + payloadLength + "\r\n\r\n" + payload;
	}

	private static string CreateJsonRpcErrorMessage(int id, int code, string message)
	{
		string payload = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":" + code + ",\"message\":" + JsonSerializer.Serialize(message) + "}}";
		int payloadLength = Encoding.UTF8.GetByteCount(payload);
		return "Content-Length: " + payloadLength + "\r\n\r\n" + payload;
	}

	private static LanguageServerTransportSession CreateTransportSession(LanguageServerClient client, long generation, Process? process, Stream serverOutputStream, Stream serverInputStream, bool startListening = false)
	{
		var session = new LanguageServerTransportSession(generation, process, serverOutputStream, serverInputStream);

		session.MessageHandler = client.TransportHost.CreateMessageHandlerWithLogger(serverInputStream, serverOutputStream);
		session.RpcTarget = client.TransportHost.CreateRpcTarget(generation);

		JsonRpc jsonRpc = client.TransportHost.CreateJsonRpc(session);
		session.JsonRpc = jsonRpc;
		session.RpcCompletionTask = jsonRpc.Completion;

		if (startListening)
			jsonRpc.StartListening();

		return session;
	}

	private static LanguageServerClientRpcTarget CreateRpcTarget(LanguageServerClient client, long generation = 0)
		=> client.TransportHost.CreateRpcTarget(generation);

	private static void SetActiveSession(LanguageServerClient client, LanguageServerTransportSession session)
		=> client.CapabilityStore.SetActiveSession(session);

	private static void SetReadyState(LanguageServerClient client, bool isReady)
		=> client.CapabilityStore.SetCapabilityReadinessForGeneration(client.TransportGeneration, isReady);

	private static void CaptureServerCapabilities(LanguageServerClient client, InitializeResponse response)
		=> client.CapabilityStore.CaptureServerCapabilitiesForGeneration(client.TransportGeneration, response);

	private static long GetTransportGeneration(LanguageServerTransportSession session)
		=> session.Generation;

	private static void RecordStandardErrorLine(LanguageServerTransportSession session, string line)
		=> session.RecordStandardErrorLine(line);

	private static InitializeResponse DeserializeInitializeResponse(string json)
	{
		return JsonSerializer.Deserialize<InitializeResponse>(json)
			?? throw new InvalidOperationException("Failed to deserialize the initialize response test payload.");
	}

	private static PublishDiagnosticsParams CreateDiagnosticsParameters(string uri, string message) => new(
		uri,
		Version: null,
		Diagnostics:
		[
			new DiagnosticPayload(
				new ProtocolRangePayload(
					new ProtocolPosition(0, 0),
					new ProtocolPosition(0, 1)),
				Severity: null,
				Message: message,
				Source: null,
				Code: null)
		]);

	private static async Task WaitForStartupCancellationAsync(TaskCompletionSource<bool> sessionActivated, CancellationToken cancellationToken)
	{
		sessionActivated.TrySetResult(true);
		await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
	}

	private static async Task AssertFaultedOrCanceledAsync(Task task)
	{
		try
		{
			await task.ConfigureAwait(false);
			Assert.Fail("Expected the task to fault or be canceled.");
		}
		catch (Exception exception) when (exception is not UnitTestAssertException)
		{
			Assert.IsTrue(task.IsFaulted || task.IsCanceled,
				$"Expected the task to fault or be canceled, but its status was '{task.Status}'.");
		}
	}

	// Deadline-bounded wait: a fixed iteration count can expire under load even though the process still exits
	// a moment later, while the deadline can only fail red when the process never exits at all.
	private static async Task<bool> WaitForProcessExitAsync(int processId)
	{
		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

		while (true)
		{
			try
			{
				using Process process = Process.GetProcessById(processId);

				if (process.HasExited)
					return true;
			}
			catch (ArgumentException)
			{
				return true;
			}

			if (DateTime.UtcNow >= deadline)
				return false;

			await Task.Delay(25).ConfigureAwait(false);
		}
	}

	// Deadline-bounded poll: the request id appears once the transport wrote the outgoing payload, so the wait
	// only fails red when the write never happens.
	private static async Task<int> WaitForRequestIdAsync(RecordingStream stream)
	{
		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

		while (true)
		{
			byte[] writtenPayload = stream.GetWrittenBytes();

			if (TryExtractJsonRpcRequestId(writtenPayload, out int requestId))
				return requestId;

			if (DateTime.UtcNow >= deadline)
				break;

			await Task.Delay(25).ConfigureAwait(false);
		}

		throw new AssertFailedException("Timed out waiting for the JSON-RPC request payload to be written.");
	}

	// Deadline-bounded poll: the text appears once the transport wrote the outgoing payload, so the wait only
	// fails red when the write never happens.
	private static async Task<string> WaitForWrittenTextAsync(RecordingStream stream, string expectedText)
	{
		DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

		while (true)
		{
			string writtenText = Encoding.UTF8.GetString(stream.GetWrittenBytes());

			if (writtenText.Contains(expectedText, StringComparison.Ordinal))
				return writtenText;

			if (DateTime.UtcNow >= deadline)
				break;

			await Task.Delay(25).ConfigureAwait(false);
		}

		throw new AssertFailedException($"Timed out waiting for the written payload to contain '{expectedText}'.");
	}

	private static bool TryExtractJsonRpcRequestId(byte[] writtenPayload, out int requestId)
	{
		requestId = 0;

		if (writtenPayload.Length == 0)
			return false;

		string payloadText = Encoding.UTF8.GetString(writtenPayload);
		int bodySeparatorIndex = payloadText.IndexOf("\r\n\r\n", StringComparison.Ordinal);

		if (bodySeparatorIndex < 0)
			return false;

		string headerText = payloadText[..bodySeparatorIndex];
		const string contentLengthPrefix = "Content-Length:";
		int contentLengthLineIndex = headerText.IndexOf(contentLengthPrefix, StringComparison.OrdinalIgnoreCase);

		if (contentLengthLineIndex < 0)
			throw new AssertFailedException("The JSON-RPC request payload did not contain a Content-Length header.");

		int contentLengthValueStart = contentLengthLineIndex + contentLengthPrefix.Length;
		int contentLengthValueEnd = headerText.IndexOf("\r\n", contentLengthValueStart, StringComparison.Ordinal);
		string contentLengthText = (contentLengthValueEnd >= 0
			? headerText[contentLengthValueStart..contentLengthValueEnd]
			: headerText[contentLengthValueStart..]).Trim();

		if (!int.TryParse(contentLengthText, out int contentLength) || contentLength < 0)
			throw new AssertFailedException("The JSON-RPC request payload contained an invalid Content-Length header.");

		int bodyStartIndex = bodySeparatorIndex + 4;

		if (writtenPayload.Length < bodyStartIndex + contentLength)
			return false;

		string jsonPayload = Encoding.UTF8.GetString(writtenPayload, bodyStartIndex, contentLength);
		using JsonDocument document = JsonDocument.Parse(jsonPayload);

		if (!document.RootElement.TryGetProperty("id", out JsonElement idElement)
			|| !idElement.TryGetInt32(out requestId))
		{
			throw new AssertFailedException("The JSON-RPC request payload did not contain an integer request id.");
		}

		return true;
	}

	private sealed class RecordingStream : Stream
	{
		private readonly object _syncRoot = new();
		private readonly MemoryStream _innerStream = new();

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => _innerStream.Length;

		public override long Position
		{
			get
			{
				lock (_syncRoot)
					return _innerStream.Position;
			}
			set
			{
				lock (_syncRoot)
					_innerStream.Position = value;
			}
		}

		public byte[] GetWrittenBytes()
		{
			lock (_syncRoot)
				return _innerStream.ToArray();
		}

		public string GetWrittenText()
			=> Encoding.UTF8.GetString(GetWrittenBytes());

		public override void Flush()
		{
			lock (_syncRoot)
				_innerStream.Flush();
		}

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
		{
			lock (_syncRoot)
				_innerStream.SetLength(value);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			lock (_syncRoot)
				_innerStream.Write(buffer, offset, count);
		}

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			lock (_syncRoot)
				_innerStream.Write(buffer);
		}

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			lock (_syncRoot)
			{
				_innerStream.Write(buffer.Span);
				return ValueTask.CompletedTask;
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				lock (_syncRoot)
					_innerStream.Flush();
			}

			base.Dispose(disposing);
		}
	}

	private sealed class BlockingWriteStream : Stream
	{
		private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public void Release()
			=> _release.TrySetResult(true);

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override void Write(ReadOnlySpan<byte> buffer)
			=> throw new NotSupportedException();

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
			=> new(WaitForReleaseAsync(cancellationToken));

		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
			=> WaitForReleaseAsync(cancellationToken);

		public override void Flush()
		{ }

		public override Task FlushAsync(CancellationToken cancellationToken)
			=> Task.CompletedTask;

		private async Task WaitForReleaseAsync(CancellationToken cancellationToken)
		{
			if (_release.Task.IsCompleted)
				return;

			if (!cancellationToken.CanBeCanceled)
			{
				await _release.Task.ConfigureAwait(false);
				return;
			}

			await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	private sealed class PendingReadStream : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{ }

			return 0;
		}

		public override void Flush()
		{ }

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();
	}

	private sealed class DeferredJsonRpcResponseStream : Stream
	{
		private readonly TaskCompletionSource<byte[]> _payloadSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private byte[]? _payloadBytes;
		private int _position;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => _payloadBytes?.Length ?? 0;

		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public void SetPayload(string payload)
			=> _payloadSource.TrySetResult(Encoding.UTF8.GetBytes(payload));

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			_payloadBytes ??= await _payloadSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			if (_position >= _payloadBytes.Length)
				return 0;

			int bytesToCopy = Math.Min(buffer.Length, _payloadBytes.Length - _position);
			_payloadBytes.AsMemory(_position, bytesToCopy).CopyTo(buffer);
			_position += bytesToCopy;
			return bytesToCopy;
		}

		public override void Flush()
		{ }

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();
	}

	private sealed class DeferredPersistentJsonRpcResponseStream : Stream
	{
		private readonly TaskCompletionSource<byte[]> _payloadSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<bool> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private byte[]? _payloadBytes;
		private int _position;

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => _payloadBytes?.Length ?? 0;

		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public void SetPayload(string payload)
			=> _payloadSource.TrySetResult(Encoding.UTF8.GetBytes(payload));

		public override int Read(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			_payloadBytes ??= await _payloadSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			if (_position < _payloadBytes.Length)
			{
				int bytesToCopy = Math.Min(buffer.Length, _payloadBytes.Length - _position);
				_payloadBytes.AsMemory(_position, bytesToCopy).CopyTo(buffer);
				_position += bytesToCopy;
				return bytesToCopy;
			}

			await _completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			return 0;
		}

		public override void Flush()
		{ }

		public override long Seek(long offset, SeekOrigin origin)
			=> throw new NotSupportedException();

		public override void SetLength(long value)
			=> throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count)
			=> throw new NotSupportedException();
	}

	private sealed class TestConfigurationRoot
	{
		public TestSectionConfiguration? Section { get; init; }
	}

	private sealed class TestSectionConfiguration
	{
		public TestNestedSectionConfiguration? Runtime { get; init; }
	}

	private sealed class TestNestedSectionConfiguration
	{
		public string? Version { get; init; }
	}
}
