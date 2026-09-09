using System.Diagnostics;
using System.Reflection;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Provides direct access to provider internals for the sparse invariants that have no behavior-level
/// oracle; each accessor documents why its seam is deliberately kept. Prefer behavior-level tests
/// elsewhere.
/// </summary>
internal static class LuaLanguageServerIntelliSenseProviderTestAccess
{
	/// <summary>
	/// Gets the provider's disposal token source.
	/// </summary>
	/// <remarks>
	/// Deliberate direct seam: the disposal-race tests must observe whether the source is
	/// still usable after <c>Dispose</c>, and no behavior-level API exposes that. The source must stay
	/// undisposed so in-flight request paths can keep linking timeout tokens against it.
	/// </remarks>
	public static CancellationTokenSource GetProviderDisposeCancellationTokenSource(LuaLanguageServerIntelliSenseProvider provider)
	{
		FieldInfo field = FindProviderField("_disposeCts")
			?? throw new InvalidOperationException("Private field '_disposeCts' was not found.");

		return (CancellationTokenSource)(field.GetValue(provider)
			?? throw new InvalidOperationException("Provider dispose token source was null."));
	}

	/// <summary>
	/// Finds a provider field on the provider type or any base type: the provider framework owns the
	/// lifecycle fields, so they live on the framework base class of the Lua provider.
	/// </summary>
	private static FieldInfo? FindProviderField(string fieldName)
	{
		for (Type? type = typeof(LuaLanguageServerIntelliSenseProvider); type is not null; type = type.BaseType)
		{
			FieldInfo? field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

			if (field is not null)
				return field;
		}

		return null;
	}

	/// <summary>
	/// Gets the real language-server client the provider constructed for a configured executable path.
	/// </summary>
	/// <remarks>
	/// Deliberate direct seam: the handshake-wiring test observes the client the provider builds without
	/// starting it, and the framework-owned client field has no behavior-level accessor.
	/// </remarks>
	public static LanguageServerClient GetProviderClient(LuaLanguageServerIntelliSenseProvider provider)
	{
		FieldInfo field = FindProviderField("_client")
			?? throw new InvalidOperationException("Private field '_client' was not found.");

		return (LanguageServerClient)(field.GetValue(provider)
			?? throw new InvalidOperationException("Provider client was null."));
	}

	/// <summary>
	/// Invokes the client-capabilities factory the client was wired with.
	/// </summary>
	/// <remarks>
	/// Deliberate direct seam: the factories are private client state, and a dropped registration would
	/// otherwise only surface in the opt-in integration tests.
	/// </remarks>
	public static object? InvokeClientCapabilitiesProvider(LanguageServerClient client, IReadOnlyList<string> workspaceRootDirectoryPaths)
		=> InvokeClientFactory(client, "_clientCapabilitiesProvider", workspaceRootDirectoryPaths);

	/// <summary>
	/// Invokes the initialization-options factory the client was wired with.
	/// </summary>
	public static object? InvokeInitializationOptionsProvider(LanguageServerClient client, IReadOnlyList<string> workspaceRootDirectoryPaths)
		=> InvokeClientFactory(client, "_initializationOptionsProvider", workspaceRootDirectoryPaths);

	/// <summary>
	/// Invokes the settings factory the client was wired with.
	/// </summary>
	public static object InvokeSettingsProvider(LanguageServerClient client)
	{
		object forwarder = GetClientField(client, "_protocolForwarder")
			?? throw new InvalidOperationException("Client protocol forwarder was null.");

		FieldInfo settingsField = forwarder.GetType().GetField("_settingsProvider", BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("Private field '_settingsProvider' was not found on the protocol forwarder.");

		return ((Func<object>)(settingsField.GetValue(forwarder)
			?? throw new InvalidOperationException("Client settings provider was null.")))();
	}

	private static object? InvokeClientFactory(LanguageServerClient client, string fieldName, IReadOnlyList<string> workspaceRootDirectoryPaths)
	{
		object factory = GetClientField(client, fieldName)
			?? throw new InvalidOperationException($"Private field '{fieldName}' was not found on the language-server client.");

		return ((Delegate)factory).DynamicInvoke(workspaceRootDirectoryPaths);
	}

	private static object? GetClientField(LanguageServerClient client, string fieldName)
		=> typeof(LanguageServerClient).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(client);

	/// <summary>
	/// Gets the active server process of the real client, when a live session with a spawned process
	/// exists.
	/// </summary>
	/// <remarks>
	/// Deliberate direct seam: process identity is transport-internal and has no public surface; the
	/// live integration tests kill and observe the spawned server process through it. The helper walks
	/// the client's internal capability-store path and fails loudly when a rename breaks it; the
	/// handshake suite pins the path chain without a live session.
	/// </remarks>
	public static Process? GetActiveServerProcess(LanguageServerClient client)
	{
		object capabilityStore = typeof(LanguageServerClient)
			.GetProperty("CapabilityStore", BindingFlags.Instance | BindingFlags.NonPublic)
			?.GetValue(client)
			?? throw new InvalidOperationException("Internal property 'CapabilityStore' was not found on the language-server client.");

		object? session = capabilityStore.GetType()
			.GetProperty("ActiveSession", BindingFlags.Instance | BindingFlags.NonPublic)
			?.GetValue(capabilityStore);

		if (session is null)
			return null;

		return (Process?)session.GetType()
			.GetProperty("Process", BindingFlags.Instance | BindingFlags.Public)
			?.GetValue(session);
	}
}
