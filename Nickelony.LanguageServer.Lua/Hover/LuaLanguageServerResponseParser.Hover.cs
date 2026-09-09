using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense;
using Nickelony.IDEKit.IntelliSense.Hover;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua;

internal static partial class LuaLanguageServerResponseParser
{
	/// <summary>
	/// Parses hover content from a LuaLS hover response.
	/// </summary>
	/// <param name="response">The hover response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="content">The document content the response range refers to.</param>
	/// <returns>The parsed hover info, or <see langword="null"/> when no usable content is present.</returns>
	internal static TextHoverInfo? ParseHoverInfo(HoverResponse? response, string content)
	{
		if (response?.Contents is not { } hoverContents || hoverContents.ValueKind == JsonValueKind.Undefined)
			return null;

		ProtocolMarkupContent hoverContent = MarkupContentReader.ExtractContent(hoverContents);

		// Markdown line endings are normalized like completion documentation; plain text is trimmed.
		// Surrounding markdown whitespace is preserved because it carries indented code blocks and
		// hard line breaks.
		string? hoverText = hoverContent.IsMarkdown
			? MarkupContentReader.NormalizeMarkdownText(hoverContent.Text)
			: hoverContent.Text.Trim();

		return string.IsNullOrWhiteSpace(hoverText)
			? null
			: new TextHoverInfo(
				hoverText,
				hoverContent.IsMarkdown ? TextMarkupKind.Markdown : TextMarkupKind.PlainText)
			{
				Range = TryGetHoverRange(response.Range, content)
			};
	}

	/// <summary>
	/// Converts the optional protocol range of a hover response into the shared zero-based offset
	/// range against the requested document snapshot.
	/// </summary>
	/// <remarks>
	/// Negative coordinates reject the range; positions that exceed the document are clamped by the
	/// line map. A range that still maps to a reversed offset range after clamping is dropped because
	/// <see cref="TextRange"/> cannot represent it.
	/// </remarks>
	/// <param name="range">The protocol range payload, or <see langword="null"/> when the response carried none.</param>
	/// <param name="content">The document content the range refers to.</param>
	/// <returns>The converted range, or <see langword="null"/> when the payload carries no usable range.</returns>
	private static TextRange? TryGetHoverRange(ProtocolRangePayload? range, string content)
		=> ProtocolRangeConversion.TryGetTextPositionRange(range, out TextPositionRange hoverRange)
			&& TextLineMap.Build(content).TryGetOffsets(hoverRange, out TextRange offsetRange)
				? offsetRange
				: null;
}
