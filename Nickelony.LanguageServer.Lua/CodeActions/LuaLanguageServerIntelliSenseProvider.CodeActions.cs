using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextCodeAction>> GetCodeActionsAsync(TextCodeActionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return SendDocumentRequestAsync<CodeActionsResponse?, IReadOnlyList<TextCodeAction>>(
			request.FilePath, request.DocumentText, "textDocument/codeAction",
			supportsRequest: static client => client.SupportsCodeActions,
			buildParameters: textDocument => new CodeActionParams(
				textDocument,
				// Request coordinates are clamped to zero like the shared position-based request path.
				new ProtocolRangePayload(
					new ProtocolPosition(Math.Max(0, request.Range.Start.Line), Math.Max(0, request.Range.Start.Character)),
					new ProtocolPosition(Math.Max(0, request.Range.End.Line), Math.Max(0, request.Range.End.Character))),
				// The protocol context carries the diagnostics currently reported for the requested
				// range; LuaLS derives its quick-fix family from them, so an empty context would
				// suppress it. The build runs inside the parameter factory, after the request gates.
				new CodeActionContextPayload(BuildCodeActionContextDiagnostics(request))),
			parseResponse: response => LuaLanguageServerResponseParser.ParseCodeActions(response, Logger),
			fallbackValue: [],
			cancellationToken);
	}

	private List<DiagnosticPayload> BuildCodeActionContextDiagnostics(TextCodeActionRequest request)
	{
		// A reversed request range cannot describe a selection; the context stays empty instead of
		// guessing which diagnostics were meant.
		if (IsPositionAfter(request.Range.Start, request.Range.End))
			return [];

		(IReadOnlyList<TextDiagnostic> diagnostics, string? sourceContent) = DocumentStore.GetDiagnosticsSnapshot(request.FilePath);

		if (diagnostics.Count == 0)
			return [];

		// The cached diagnostics' offsets refer to the snapshot they were parsed against (the
		// server's synchronized view); converting them with that same snapshot keeps the emitted
		// positions self-consistent even when the caller's document text has changed since (for
		// example between an edit and the next publish).
		TextLineMap lineMap = TextLineMap.Build(sourceContent ?? request.DocumentText);

		// The requested range addresses the caller's text. Only when both texts describe the same
		// document state can the two coordinate spaces be intersected; otherwise the context keeps
		// every cached diagnostic instead of dropping entries through a comparison it cannot make.
		bool canIntersect = string.Equals(sourceContent, request.DocumentText, StringComparison.Ordinal);

		var result = new List<DiagnosticPayload>();

		for (int i = 0; i < diagnostics.Count; i++)
		{
			TextDiagnostic diagnostic = diagnostics[i];

			TextPositionRange diagnosticRange = new(
				lineMap.GetPosition(diagnostic.StartOffset),
				lineMap.GetPosition(diagnostic.EndOffset));

			// A diagnostic that does not intersect the requested range is not part of this request.
			if (canIntersect && !RangesIntersect(diagnosticRange, request.Range))
				continue;

			result.Add(new DiagnosticPayload(
				new ProtocolRangePayload(
					new ProtocolPosition(diagnosticRange.Start.Line, diagnosticRange.Start.Character),
					new ProtocolPosition(diagnosticRange.End.Line, diagnosticRange.End.Character)),
				MapProtocolSeverity(diagnostic.Severity),
				diagnostic.Message,
				diagnostic.Source,
				diagnostic.Code is null ? null : JsonSerializer.SerializeToElement(diagnostic.Code)));
		}

		return result;
	}

	private static bool RangesIntersect(TextPositionRange left, TextPositionRange right)
		=> !IsPositionAfter(left.Start, right.End) && !IsPositionAfter(right.Start, left.End);

	private static bool IsPositionAfter(TextPosition left, TextPosition right)
		=> left.Line > right.Line || (left.Line == right.Line && left.Character > right.Character);

	// The shared severity taxonomy and the protocol enum are mapped explicitly: the numeric values
	// currently agree, but the shared enum documents its values as stable identifiers, not a ranking.
	private static DiagnosticSeverity? MapProtocolSeverity(TextDiagnosticSeverity severity) => severity switch
	{
		TextDiagnosticSeverity.Error => DiagnosticSeverity.Error,
		TextDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
		TextDiagnosticSeverity.Information => DiagnosticSeverity.Information,
		TextDiagnosticSeverity.Hint => DiagnosticSeverity.Hint,
		_ => null
	};
}
