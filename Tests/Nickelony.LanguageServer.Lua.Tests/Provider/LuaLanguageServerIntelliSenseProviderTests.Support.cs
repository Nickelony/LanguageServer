using System.Reflection;

namespace Nickelony.LanguageServer.Lua.Tests;

public partial class LuaLanguageServerIntelliSenseProviderTests
{
	private static PublishDiagnosticsParams CreateDiagnostics(string filePath, int? version, int startCharacter, int endCharacter, string message) => new(
		new Uri(filePath).AbsoluteUri,
		version,
		[
			new DiagnosticPayload(
				new ProtocolRangePayload(
					new ProtocolNullablePosition(0, startCharacter),
					new ProtocolNullablePosition(0, endCharacter)),
				2,
				message,
				null,
				null)
		]);

	private static WorkspaceFileWatcher? GetWorkspaceWatcher(LuaLanguageServerIntelliSenseProvider provider)
		=> LuaLanguageServerIntelliSenseProviderTestAccess.GetWorkspaceWatcher(provider);

	private static SemaphoreSlim GetProviderStartLock(LuaLanguageServerIntelliSenseProvider provider)
		=> LuaLanguageServerIntelliSenseProviderTestAccess.GetProviderStartLock(provider);

	private static CancellationTokenSource GetProviderDisposeCancellationTokenSource(LuaLanguageServerIntelliSenseProvider provider)
		=> LuaLanguageServerIntelliSenseProviderTestAccess.GetProviderDisposeCancellationTokenSource(provider);

	private static int GetTrackedDocumentCount(LuaLanguageServerIntelliSenseProvider provider)
		=> LuaLanguageServerIntelliSenseProviderTestAccess.GetTrackedDocumentCount(provider);

	private static int CountSentMethods(FakeLanguageServerClient client, string method)
	{
		int count = 0;
		string[] methods = client.GetSentMethodNames();

		for (int i = 0; i < methods.Length; i++)
		{
			if (string.Equals(methods[i], method, StringComparison.Ordinal))
				count++;
		}

		return count;
	}

	private static T InvokePrivateMethodWithReturn<T>(object instance, string methodName, params object?[] parameters)
	{
		MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException($"Private method '{methodName}' was not found.");

		object? result = method.Invoke(instance, parameters);

		if (result is T typedResult)
			return typedResult;

		throw new InvalidOperationException($"Private method '{methodName}' returned '{result?.GetType().FullName ?? "null"}' instead of '{typeof(T).FullName}'.");
	}

	private static Task DispatchWorkspaceFileChangesAsync(LuaLanguageServerIntelliSenseProvider provider, FileChangeBatch batch, CancellationToken cancellationToken)
		=> LuaLanguageServerIntelliSenseProviderTestAccess.DispatchWorkspaceFileChangesAsync(provider, batch, cancellationToken);
}
