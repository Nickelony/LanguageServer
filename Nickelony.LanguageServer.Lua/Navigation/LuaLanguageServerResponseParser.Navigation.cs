using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.LanguageServer.Lua;

internal static partial class LuaLanguageServerResponseParser
{
	/// <summary>
	/// Parses a definition location from a LuaLS definition response.
	/// </summary>
	/// <param name="response">The definition response payload, or <see langword="null"/> when unavailable.</param>
	/// <returns>The resolved definition location, or <see langword="null"/> when the response does not contain a usable local file URI or the target carries no usable range.</returns>
	internal static TextDefinitionLocation? ParseDefinitionLocation(DefinitionResponse? response)
	{
		if (response is null)
			return null;

		if (response.FirstTarget is not DefinitionTargetPayload target
			|| !LanguageServerPaths.TryGetLocalPath(target.Uri, out string filePath)
			|| !ProtocolRangeConversion.TryGetTextPositionRange(target.TargetRange, out TextPositionRange targetRange))
		{
			return null;
		}

		TextPositionRange? selectionRange = ProtocolRangeConversion.TryGetTextPositionRange(target.SelectionRange, out TextPositionRange selection)
			? selection
			: null;

		return new(targetRange, filePath, selectionRange);
	}
}
