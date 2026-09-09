using System.Text.Json;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Resolves dotted workspace/configuration section paths against a serialized settings payload.
/// </summary>
internal static class JsonConfigurationSectionReader
{
	/// <summary>
	/// Extracts a nested configuration section from a serialized settings payload.
	/// </summary>
	/// <param name="settingsElement">The serialized root settings element.</param>
	/// <param name="section">The dotted configuration section path.</param>
	/// <returns>The extracted section object, or <see langword="null"/> when the section is missing.</returns>
	/// <remarks>
	/// A blank section returns a clone of the whole settings payload. Property lookup prefers an exact name match and
	/// otherwise falls back to a case-insensitive match per path segment; when several properties differ only in
	/// casing, the first one in document order wins.
	/// </remarks>
	internal static object? GetSection(JsonElement settingsElement, string? section)
	{
		if (string.IsNullOrWhiteSpace(section))
			return settingsElement.Clone();

		JsonElement currentSection = settingsElement;
		string[] parts = section.Split('.');

		for (int i = 0; i < parts.Length; i++)
		{
			if (currentSection.ValueKind is not JsonValueKind.Object
				|| !JsonElementReadHelpers.TryGetProperty(currentSection, parts[i], out JsonElement nextSection))
			{
				return null;
			}

			currentSection = nextSection;
		}

		return currentSection.Clone();
	}
}
