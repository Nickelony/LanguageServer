using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>
/// Provides the bounded regex cache that backs the <see cref="FindReplaceText"/> helpers.
/// </summary>
/// <remarks>
/// <para>
/// The cache is keyed by pattern, options, the effective timeout, and - for case-insensitive
/// patterns that do not opt into <see cref="RegexOptions.CultureInvariant"/> - the culture captured
/// at creation, so repeated searches over one pattern do not re-parse it. It is bounded: when it is
/// full, the oldest entry (by insertion sequence) is
/// evicted, so a burst of distinct patterns - exactly what typing a pattern produces - churns the
/// cache predictably instead of evicting an arbitrary entry.
/// </para>
/// <para>
/// The BCL's <see cref="Regex.CacheSize"/> governs a process-wide cache that a library must not
/// reconfigure on a consumer's behalf, and its eviction order is not observable; this cache is
/// bounded, insertion-ordered, and keyed explicitly - pattern, options, timeout, and, for
/// case-insensitive patterns without <see cref="RegexOptions.CultureInvariant"/>, the captured
/// culture - which keeps its behavior deterministic and testable.
/// </para>
/// <para>
/// The cache is trimmed back to the limit after an insert that exceeds it: racing misses can
/// briefly hold a few entries more than the limit, but every overflowing insert trims the cache
/// back, so the count cannot keep growing under concurrency.
/// </para>
/// </remarks>
internal static class RegexCache
{
	private const int MaximumCachedRegexCount = 64;

	private static readonly ConcurrentDictionary<RegexCacheKey, CacheEntry> s_regexCache = new();

	private static readonly object s_evictionLock = new();

	private static long s_insertionSequence;

	/// <summary>
	/// Gets the number of cached regexes. Internal so the test assembly can verify the bound without
	/// exposing the cache.
	/// </summary>
	internal static int CachedRegexCount => s_regexCache.Count;

	/// <summary>
	/// Returns a cached regex for the supplied pattern, options, and timeout, creating it on first
	/// use.
	/// </summary>
	/// <param name="pattern">The regular expression pattern.</param>
	/// <param name="options">The options used for matching.</param>
	/// <param name="matchTimeout">
	/// The maximum time for a single match attempt. A non-positive value maps to
	/// <see cref="Regex.InfiniteMatchTimeout"/>, matching the public helpers' documented default.
	/// </param>
	/// <returns>The cached or newly created regex.</returns>
	internal static Regex CreateRegex(string pattern, RegexOptions options, TimeSpan matchTimeout)
	{
		// Normalize the timeout before it enters the key so equivalent requests - zero and
		// Regex.InfiniteMatchTimeout both mean unbounded - share one cache slot and one parse.
		TimeSpan effectiveMatchTimeout = matchTimeout > TimeSpan.Zero ? matchTimeout : Regex.InfiniteMatchTimeout;

		// A case-insensitive pattern without CultureInvariant resolves case equivalence with the
		// culture captured at construction, so that culture joins the key; every other pattern shares
		// one culture-independent slot.
		string? capturedCultureName = (options & RegexOptions.IgnoreCase) != 0
			&& (options & RegexOptions.CultureInvariant) == 0
				? CultureInfo.CurrentCulture.Name
				: null;
		var key = new RegexCacheKey(pattern, options, effectiveMatchTimeout, capturedCultureName);

		if (s_regexCache.TryGetValue(key, out CacheEntry cachedEntry))
			return cachedEntry.Regex;

		var regex = new Regex(pattern, options, effectiveMatchTimeout);
		var entry = new CacheEntry(regex, Interlocked.Increment(ref s_insertionSequence));

		s_regexCache.TryAdd(key, entry);

		// An insert that pushes the cache over the limit trims it back immediately, so racing misses
		// cannot leave the cache permanently above its bound: every overflowing insert drains the
		// overflow, and a lost eviction race cannot leak an entry (unlike a check-then-evict pass,
		// whose loser discards a failed removal and still inserts).
		if (s_regexCache.Count > MaximumCachedRegexCount)
			TrimToMaximumCount();

		return regex;
	}

	/// <summary>
	/// Evicts entries until the cache is at or below the limit. The trim is serialized by
	/// <see cref="s_evictionLock"/> so racing inserts cannot starve one another's evictions, and it
	/// runs only when an insert overflowed the cache, so the read path stays lock-free.
	/// </summary>
	private static void TrimToMaximumCount()
	{
		lock (s_evictionLock)
		{
			while (s_regexCache.Count > MaximumCachedRegexCount)
			{
				if (!EvictOldestCachedRegex())
					return;
			}
		}
	}

	/// <summary>
	/// Evicts the least recently inserted cached regex. The eviction is insertion-ordered rather
	/// than least-recently-used: a hit does not reorder the cache, which keeps the read path
	/// allocation- and write-free.
	/// </summary>
	/// <returns><see langword="true"/> when an entry was evicted, or <see langword="false"/> when the cache was empty.</returns>
	private static bool EvictOldestCachedRegex()
	{
		RegexCacheKey? oldestKey = null;
		long oldestSequence = long.MaxValue;

		foreach (KeyValuePair<RegexCacheKey, CacheEntry> pair in s_regexCache)
		{
			if (pair.Value.InsertionSequence < oldestSequence)
			{
				oldestSequence = pair.Value.InsertionSequence;
				oldestKey = pair.Key;
			}
		}

		return oldestKey is { } key && s_regexCache.TryRemove(key, out _);
	}

	private readonly record struct CacheEntry(Regex Regex, long InsertionSequence);

	private readonly record struct RegexCacheKey(string Pattern, RegexOptions Options, TimeSpan MatchTimeout, string? CapturedCultureName);
}
