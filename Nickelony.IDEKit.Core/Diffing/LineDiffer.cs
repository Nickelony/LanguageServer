namespace Nickelony.IDEKit.Core.Diffing;

/// <summary>
/// Computes changed lines between two line-split texts using the Myers diff algorithm.
/// </summary>
public static class LineDiffer
{
	/// <summary>
	/// Gets the one-based line numbers of <paramref name="current"/> that are not part of the longest
	/// common subsequence with <paramref name="baseline"/>; that is, the lines that were inserted or
	/// modified relative to the baseline.
	/// </summary>
	/// <param name="baseline">The earlier line-split text.</param>
	/// <param name="current">The later line-split text.</param>
	/// <returns>The one-based line numbers in <paramref name="current"/> that changed.</returns>
	public static HashSet<int> GetChangedLines(string[] baseline, string[] current)
	{
		var changed = new HashSet<int>();
		int baselineCount = baseline.Length;
		int currentCount = current.Length;

		if (baselineCount == 0)
		{
			for (int lineNumber = 1; lineNumber <= currentCount; lineNumber++)
				changed.Add(lineNumber);

			return changed;
		}

		if (currentCount == 0)
			return changed;

		int prefix = CountCommonPrefix(baseline, current);
		int suffix = CountCommonSuffix(baseline, current, prefix);

		int middleBaselineCount = baselineCount - prefix - suffix;
		int middleCurrentCount = currentCount - prefix - suffix;

		if (middleBaselineCount == 0)
		{
			for (int i = 0; i < middleCurrentCount; i++)
				changed.Add(prefix + i + 1);

			return changed;
		}

		if (middleCurrentCount == 0)
			return changed;

		CollectChangedLines(
			baseline, prefix, middleBaselineCount,
			current, prefix, middleCurrentCount,
			changed);

		return changed;
	}

	private static int CountCommonPrefix(string[] baseline, string[] current)
	{
		int count = 0;
		int limit = Math.Min(baseline.Length, current.Length);

		while (count < limit && string.Equals(baseline[count], current[count], StringComparison.Ordinal))
			count++;

		return count;
	}

	private static int CountCommonSuffix(string[] baseline, string[] current, int prefix)
	{
		int baselineIndex = baseline.Length - 1;
		int currentIndex = current.Length - 1;
		int count = 0;

		while (count < baseline.Length - prefix
			&& count < current.Length - prefix
			&& string.Equals(baseline[baselineIndex], current[currentIndex], StringComparison.Ordinal))
		{
			count++;
			baselineIndex--;
			currentIndex--;
		}

		return count;
	}

	private static void CollectChangedLines(
		string[] baseline, int baselineStart, int baselineCount,
		string[] current, int currentStart, int currentCount,
		HashSet<int> changed)
	{
		// Greedy Myers forward pass over the middle slices; records the furthest-reaching path per
		// edit-distance step so the minimal edit script can be reconstructed on backtracking.
		int max = baselineCount + currentCount;
		int offset = max;
		var v = new int[(2 * max) + 2];
		var trace = new List<int[]>((max / 2) + 1);
		int endDistance = -1;

		for (int distance = 0; distance <= max && endDistance < 0; distance++)
		{
			for (int k = -distance; k <= distance && endDistance < 0; k += 2)
			{
				int x;

				if (k == -distance || (k != distance && v[offset + k - 1] < v[offset + k + 1]))
					x = v[offset + k + 1];
				else
					x = v[offset + k - 1] + 1;

				int y = x - k;

				while (x < baselineCount
					&& y < currentCount
					&& string.Equals(baseline[baselineStart + x], current[currentStart + y], StringComparison.Ordinal))
				{
					x++;
					y++;
				}

				v[offset + k] = x;

				if (x >= baselineCount && y >= currentCount)
					endDistance = distance;
			}

			trace.Add((int[])v.Clone());
		}

		if (endDistance < 0)
			return;

		// Backtrack through the trace to find the current lines that were inserted.
		int xEnd = baselineCount;
		int yEnd = currentCount;

		for (int distance = endDistance; distance >= 1; distance--)
		{
			int[] previousV = trace[distance - 1];
			int k = xEnd - yEnd;
			bool cameFromAbove = k == -distance || (k != distance && previousV[offset + k - 1] < previousV[offset + k + 1]);
			int previousK = cameFromAbove ? k + 1 : k - 1;
			int previousX = previousV[offset + previousK];
			int previousY = previousX - previousK;

			// Walk the trailing snake (diagonal matches) back to the single non-diagonal move.
			while (xEnd > previousX && yEnd > previousY)
			{
				xEnd--;
				yEnd--;
			}

			if (xEnd == previousX)
			{
				// The move was vertical: the current line at index previousY was inserted.
				changed.Add(currentStart + previousY + 1);
				yEnd--;
			}
			else
			{
				// The move was horizontal: a baseline line was deleted, so nothing is marked.
				xEnd--;
			}
		}
	}
}
