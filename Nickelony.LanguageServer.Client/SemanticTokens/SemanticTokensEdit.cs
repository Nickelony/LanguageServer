namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a single edit against a cached semantic token integer stream.
/// </summary>
/// <param name="Start">The zero-based start index within the cached token data.</param>
/// <param name="DeleteCount">The number of integers to remove starting at <paramref name="Start"/>.</param>
/// <param name="Data">The replacement integer payload to insert.</param>
/// <remarks>
/// The parser substitutes an empty array when the payload omitted <c>data</c>, so an omitted replacement and an
/// empty replacement are not distinguishable on this type.
/// </remarks>
public readonly record struct SemanticTokensEdit(int Start, int DeleteCount, int[] Data);
