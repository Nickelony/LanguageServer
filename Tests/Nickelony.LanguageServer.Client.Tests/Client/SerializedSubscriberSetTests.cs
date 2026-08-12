namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SerializedSubscriberSetTests
{
	[TestMethod]
	public async Task DiagnosticsQueue_ConcurrentOlderReplacementCannotOverwriteLatestPayload()
	{
		const string documentKey = "file:///C:/Workspace/test.lua";

		var firstInvocationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstInvocationToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var olderPayloadRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowOlderReplacement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondPayloadObserved = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		int invocationCount = 0;

		var subscribers = new SerializedDiagnosticsSubscriberSet<Action<PublishDiagnosticsParams>>(
			(handler, parameters) => handler(parameters),
			_ => { },
			parameters =>
			{
				if (parameters.Diagnostics?[0].Message is "Older warning.")
				{
					olderPayloadRead.TrySetResult(true);
					allowOlderReplacement.Task.GetAwaiter().GetResult();
				}
			});

		subscribers.Add(parameters =>
		{
			int currentInvocation = Interlocked.Increment(ref invocationCount);

			if (currentInvocation == 1)
			{
				firstInvocationEntered.TrySetResult(true);
				allowFirstInvocationToFinish.Task.GetAwaiter().GetResult();

				return;
			}

			secondPayloadObserved.TrySetResult(parameters.Diagnostics?[0].Message);
		});

		subscribers.Dispatch(documentKey, CreateDiagnostics("First warning."));
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		// Leave an intermediate payload pending while the first callback is blocked.
		subscribers.Dispatch(documentKey, CreateDiagnostics("Intermediate warning."));

		Task olderDispatch = Task.Run(() => subscribers.Dispatch(documentKey, CreateDiagnostics("Older warning.")));
		await olderPayloadRead.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		// This payload is newer than the paused older enqueue and must remain the one delivered.
		subscribers.Dispatch(documentKey, CreateDiagnostics("Latest warning."));
		allowOlderReplacement.TrySetResult(true);
		await olderDispatch.ConfigureAwait(false);

		allowFirstInvocationToFinish.TrySetResult(true);

		Assert.AreEqual("Latest warning.",
			await secondPayloadObserved.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));

		Assert.AreEqual(2, Volatile.Read(ref invocationCount));
	}

	private static PublishDiagnosticsParams CreateDiagnostics(string message) => new(
		"file:///C:/Workspace/test.lua",
		null,
		[new DiagnosticPayload(null, null, message, "test", null)]);
}
