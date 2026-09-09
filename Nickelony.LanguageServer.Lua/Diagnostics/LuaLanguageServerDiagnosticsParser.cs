using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Parses Lua language-server diagnostics payloads into document diagnostics tied to tracked document versions.
/// </summary>
internal static class LuaLanguageServerDiagnosticsParser
{
	/// <summary>
	/// Parses a LuaLS diagnostics notification into document diagnostics for a tracked document.
	/// </summary>
	/// <param name="parameters">The published diagnostics notification payload from the language server.</param>
	/// <param name="filePath">The normalized file path of the tracked document.</param>
	/// <param name="documentContent">The current document content.</param>
	/// <param name="documentVersion">The tracked document version to match against the diagnostics version.</param>
	/// <param name="publishedDiagnostics">When this method returns <see langword="true"/>, contains the parsed diagnostics payload.</param>
	/// <returns>
	/// <see langword="true"/> when the payload can be accepted for the tracked document version; otherwise
	/// <see langword="false"/> when the payload version is known and does not match the tracked version.
	/// </returns>
	/// <remarks>
	/// A payload with an unknown version (<c>0</c>) is accepted; whether it is stored, and whether it advances
	/// the cached version, is decided by <see cref="LuaDocumentStore.TryStoreDiagnostics"/>.
	/// <para>
	/// Accepted diagnostics are ordered by start offset, then by severity rank (errors first), so the
	/// list order is deterministic for equal offsets.
	/// </para>
	/// </remarks>
	internal static bool TryParse(PublishDiagnosticsParams parameters, string filePath,
		string documentContent, int documentVersion, [NotNullWhen(true)] out LuaPublishedDiagnostics? publishedDiagnostics)
	{
		publishedDiagnostics = null;

		int diagnosticsVersion = parameters.Version is > 0 ? parameters.Version.Value : 0;

		if (!LuaDocumentVersionPolicy.IsPayloadCurrent(documentVersion, diagnosticsVersion))
			return false;

		IReadOnlyList<TextDiagnostic> diagnostics = parameters.Diagnostics is { Count: > 0 }
			? BuildDiagnostics(documentContent, parameters.Diagnostics)
			: [];

		publishedDiagnostics = new LuaPublishedDiagnostics(filePath, diagnostics, diagnosticsVersion);
		return true;
	}

	private static IReadOnlyList<TextDiagnostic> BuildDiagnostics(string content, IReadOnlyList<DiagnosticPayload> payloads)
	{
		TextLineMap lineMap = TextLineMap.Build(content);
		var diagnostics = new List<TextDiagnostic>();

		foreach (DiagnosticPayload diagnosticPayload in payloads)
		{
			TextDiagnosticSeverity severity = GetDiagnosticSeverity(diagnosticPayload);

			if (!TryCreateDiagnostic(lineMap, diagnosticPayload, severity, out TextDiagnostic? diagnostic))
				continue;

			diagnostics.Add(diagnostic);
		}

		return [.. diagnostics
			.OrderBy(diagnostic => diagnostic.StartOffset)
			.ThenBy(diagnostic => GetSeverityRank(diagnostic.Severity))];
	}

	/// <summary>
	/// Ranks a diagnostic severity for deterministic ordering. The enum values are stable identifiers
	/// rather than a ranking, so the order is defined explicitly (error first).
	/// </summary>
	private static int GetSeverityRank(TextDiagnosticSeverity severity)
	{
		return severity switch
		{
			TextDiagnosticSeverity.Error => 0,
			TextDiagnosticSeverity.Warning => 1,
			TextDiagnosticSeverity.Information => 2,
			TextDiagnosticSeverity.Hint => 3,
			_ => 4
		};
	}

	/// <summary>
	/// Creates one shared diagnostic from a protocol payload.
	/// </summary>
	/// <remarks>
	/// Coordinates that exceed the current snapshot are clamped so a stale range cannot drop the diagnostic.
	/// A range that cannot be mapped directly (for example one that becomes reversed after clamping) falls
	/// back to the word or content anchor of its start position; only an unmappable position is skipped.
	/// </remarks>
	/// <param name="lineMap">The line map of the document content.</param>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <param name="severity">The mapped severity.</param>
	/// <param name="diagnostic">Receives the created diagnostic when successful.</param>
	/// <returns><see langword="true"/> when a diagnostic was created; otherwise, <see langword="false"/>.</returns>
	private static bool TryCreateDiagnostic(TextLineMap lineMap, DiagnosticPayload diagnosticPayload,
		TextDiagnosticSeverity severity, [NotNullWhen(true)] out TextDiagnostic? diagnostic)
	{
		diagnostic = null;

		if (diagnosticPayload.Range is not { } rangePayload)
			return false;

		// A range always carries both endpoints (see ProtocolRangePayload). Server coordinates that
		// exceed the current snapshot are clamped so a stale range cannot drop the diagnostic; negative
		// coordinates collapse to the line start.
		int lineIndex = Math.Max(0, Math.Min(rangePayload.Start.Line, lineMap.LineCount - 1));
		int startCharacter = Math.Max(0, rangePayload.Start.Character);
		int endLineIndex = Math.Max(lineIndex, Math.Min(rangePayload.End.Line, lineMap.LineCount - 1));
		int endCharacter = Math.Max(0, rangePayload.End.Character);

		var positionRange = new TextPositionRange(
			new TextPosition(lineIndex, startCharacter),
			new TextPosition(endLineIndex, endCharacter));

		// A position-only diagnostic (a legal zero-length range such as "expected ')' here") keeps
		// its zero length: the exact span is the server's answer and widening it would echo a range
		// the server never published back to it in code-action requests. Only a range that cannot be
		// mapped directly (reversed after clamping) falls back to the word/content anchor.
		if (!lineMap.TryGetOffsets(positionRange, out TextRange resolvedRange)
			&& !TextRangeOffsetResolver.TryResolveOffsets(lineMap, positionRange, IsDiagnosticWordCharacter, out resolvedRange))
		{
			return false;
		}

		diagnostic = new TextDiagnostic(
			severity,
			GetDiagnosticMessage(diagnosticPayload),
			resolvedRange.Offset,
			resolvedRange.EndOffset)
		{
			Source = GetDiagnosticSource(diagnosticPayload),
			Code = GetDiagnosticCode(diagnosticPayload)
		};
		return true;
	}

	private static TextDiagnosticSeverity GetDiagnosticSeverity(DiagnosticPayload diagnosticPayload)
	{
		// The mapping is explicit instead of a cast, so an unknown protocol value - including future
		// protocol members - cannot leak an undefined enum member into severity ordering. The protocol
		// defines no default severity, so an omitted severity falls back to an error (the highest-signal
		// choice) and an unrecognized value to a warning, like other unclassified payloads.
		return diagnosticPayload.Severity switch
		{
			DiagnosticSeverity.Error => TextDiagnosticSeverity.Error,
			DiagnosticSeverity.Warning => TextDiagnosticSeverity.Warning,
			DiagnosticSeverity.Information => TextDiagnosticSeverity.Information,
			DiagnosticSeverity.Hint => TextDiagnosticSeverity.Hint,
			null => TextDiagnosticSeverity.Error,
			_ => TextDiagnosticSeverity.Warning,
		};
	}

	/// <summary>
	/// Gets the displayed diagnostic message: the server message trimmed, or a fixed fallback for a blank one.
	/// </summary>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <returns>The display message.</returns>
	private static string GetDiagnosticMessage(DiagnosticPayload diagnosticPayload)
	{
		string? message = diagnosticPayload.Message?.Trim();

		return string.IsNullOrWhiteSpace(message) ? "Unknown Lua diagnostic." : message;
	}

	/// <summary>
	/// Gets the diagnostic source attribution, or <see langword="null"/> when the server supplied none.
	/// </summary>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <returns>The trimmed source, or <see langword="null"/>.</returns>
	private static string? GetDiagnosticSource(DiagnosticPayload diagnosticPayload)
	{
		string? source = diagnosticPayload.Source?.Trim();

		return string.IsNullOrWhiteSpace(source) ? null : source;
	}

	/// <summary>
	/// Gets the diagnostic code attribution, or <see langword="null"/> when the server supplied none.
	/// A string code is used verbatim and a numeric code keeps its raw JSON text, so the value stays stable
	/// across number formats.
	/// </summary>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <returns>The trimmed code, or <see langword="null"/>.</returns>
	private static string? GetDiagnosticCode(DiagnosticPayload diagnosticPayload)
	{
		string? code = diagnosticPayload.Code switch
		{
			{ ValueKind: JsonValueKind.String } codeElement => codeElement.GetString(),
			{ ValueKind: JsonValueKind.Number } codeElement => codeElement.GetRawText(),
			_ => null
		};

		code = code?.Trim();

		return string.IsNullOrWhiteSpace(code) ? null : code;
	}

	/// <summary>
	/// Determines whether <paramref name="character"/> is part of a word for diagnostic range
	/// expansion. The rule extends the identifier rule with <c>.</c>, <c>:</c>, <c>'</c>, and
	/// <c>"</c>, so member accesses and quoted names resolve as one selectable run.
	/// </summary>
	private static bool IsDiagnosticWordCharacter(char character)
		=> char.IsLetterOrDigit(character) || character is '_' or '.' or ':' or '\'' or '"';
}
