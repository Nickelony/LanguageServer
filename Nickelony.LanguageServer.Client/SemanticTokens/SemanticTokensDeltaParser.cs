namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Normalizes semantic token responses that may carry either a full token stream or incremental edits.
/// </summary>
/// <remarks>
/// The client parses delta payloads but never applies them: the editor-facing provider requests full tokens, so
/// incremental application stays a host-side concern. Parsed edits are validated (present, non-negative, ordered,
/// and non-overlapping); a payload that violates the contract degrades to an empty result. Use the overload that
/// accepts a logger to record why a malformed payload was degraded.
/// </remarks>
public static class SemanticTokensDeltaParser
{
	/// <summary>
	/// Parses a semantic token response that may contain either a full token stream or incremental edits.
	/// </summary>
	/// <param name="response">The raw semantic token wire response.</param>
	/// <returns>
	/// The parsed response carrying the full token stream, the incremental edits, or neither when the payload was
	/// empty or malformed; the result identifier is preserved either way.
	/// </returns>
	public static SemanticTokensDeltaResponse Parse(SemanticTokensWireResponse? response)
		=> Parse(response, logger: null);

	/// <summary>
	/// Parses a semantic token response that may contain either a full token stream or incremental edits while
	/// reporting malformed payloads through a logger.
	/// </summary>
	/// <param name="response">The raw semantic token wire response.</param>
	/// <param name="logger">The logger used for malformed-payload diagnostics, or <see langword="null"/> for no logging.</param>
	/// <returns>
	/// The parsed response carrying the full token stream, the incremental edits, or neither when the payload was
	/// empty or malformed; the result identifier is preserved either way.
	/// </returns>
	public static SemanticTokensDeltaResponse Parse(SemanticTokensWireResponse? response, ILogger? logger)
	{
		if (response is not { } payload)
			return new(ResultId: null, Data: null, Edits: null);

		// A payload that carries both members is treated as a full response: the full token stream is
		// authoritative and the edits are dropped.
		if (payload.Data is { } data)
			return new(payload.ResultId, data, Edits: null);

		if (payload.Edits is { } editsPayload)
		{
			var edits = new List<SemanticTokensEdit>(editsPayload.Length);

			// A 64-bit end position keeps the overlap check free of integer overflow.
			long previousEditEnd = 0;

			for (int i = 0; i < editsPayload.Length; i++)
			{
				SemanticTokensEditPayload edit = editsPayload[i];

				if (edit.Start is not { } start || edit.DeleteCount is not { } deleteCount)
					return Degrade(payload.ResultId, logger, "an edit did not carry both 'start' and 'deleteCount'");

				if (start < 0 || deleteCount < 0)
					return Degrade(payload.ResultId, logger, "an edit carried a negative 'start' or 'deleteCount'");

				if (start < previousEditEnd)
					return Degrade(payload.ResultId, logger, "the edits are out of order or overlap");

				previousEditEnd = (long)start + deleteCount;
				edits.Add(new SemanticTokensEdit(start, deleteCount, edit.Data ?? []));
			}

			return new(payload.ResultId, Data: null, edits);
		}

		return new(payload.ResultId, Data: null, Edits: null);
	}

	/// <summary>
	/// Reports a malformed delta payload and returns the degraded result.
	/// </summary>
	/// <param name="resultId">The result identifier to preserve.</param>
	/// <param name="logger">The logger used for malformed-payload diagnostics, or <see langword="null"/> for no logging.</param>
	/// <param name="reason">The human-readable degradation reason.</param>
	/// <returns>The degraded empty result.</returns>
	private static SemanticTokensDeltaResponse Degrade(string? resultId, ILogger? logger, string reason)
	{
		logger?.LogWarning("Ignoring semantic-token delta edits because {Reason}; the response degrades to an empty payload.", reason);
		return new(resultId, Data: null, Edits: null);
	}
}
