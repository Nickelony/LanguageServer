namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Builds the LuaLS settings payload sent by the provider.
/// </summary>
internal static class LuaLanguageServerSettingsFactory
{
	private const string DisabledSettingValue = "Disable";
	private const string CallSnippetSettingValue = "Replace";

	/// <summary>
	/// Builds the LuaLS settings payload for the current workspace.
	/// </summary>
	/// <param name="options">The provider options that override the default settings values.</param>
	/// <returns>
	/// An anonymous settings object serialized into the <c>workspace/didChangeConfiguration</c> payload
	/// and served for <c>workspace/configuration</c> callbacks.
	/// </returns>
	internal static object Create(LuaLanguageServerOptions options)
	{
		var library = options.AdditionalLibraryDirectories
			.Where(static directory => !string.IsNullOrWhiteSpace(directory))
			.ToList();

		var disabledDiagnostics = options.DisabledDiagnostics
			.Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
			.ToList();

		return new
		{
			Lua = new
			{
				runtime = new
				{
					version = options.RuntimeVersion
				},
				workspace = new
				{
					checkThirdParty = DisabledSettingValue,
					library
				},
				completion = new
				{
					// Call snippets are always enabled: the provider consumes snippet insert texts
					// end to end, and "Replace" offers the call snippet instead of the plain name.
					callSnippet = CallSnippetSettingValue
				},
				semantic = new
				{
					enable = options.EnableSemanticHighlighting,
					annotation = options.EnableSemanticAnnotationHighlighting,
					variable = options.EnableSemanticVariableHighlighting,
					keyword = options.EnableSemanticKeywordHighlighting
				},
				diagnostics = new
				{
					disable = disabledDiagnostics
				}
			}
		};
	}
}
