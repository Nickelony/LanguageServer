using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the bounded-cache invariant under concurrent misses: an overflowing insert trims the cache
/// back, so racing misses cannot leave it permanently above the limit.
/// </summary>
[TestClass]
public sealed class RegexCacheConcurrencyTests
{
	[TestMethod]
	[Timeout(30_000)]
	public void CreateRegex_ConcurrentDistinctMisses_StayWithinTheBound()
	{
		for (int wave = 0; wave < 4; wave++)
		{
			int waveIndex = wave;

			Parallel.For(0, 512, index => RegexCache.CreateRegex(
				$"cache-race-probe-{waveIndex}-{index}",
				(index & 1) == 0 ? RegexOptions.None : RegexOptions.IgnoreCase,
				TimeSpan.Zero));

			Assert.IsTrue(
				RegexCache.CachedRegexCount <= 64,
				$"The cache must stay bounded, but held {RegexCache.CachedRegexCount} entries after wave {wave}.");
		}
	}
}
