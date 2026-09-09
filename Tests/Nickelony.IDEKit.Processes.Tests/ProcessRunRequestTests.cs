namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Verifies that <see cref="ProcessRunRequest"/> validates its timeout.
/// </summary>
[TestClass]
public class ProcessRunRequestTests
{
	[TestMethod]
	public void Timeout_NegativeValue_IsRejected()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ProcessRunRequest
		{
			FileName = "compiler.exe",
			Timeout = TimeSpan.FromMilliseconds(-5)
		});
	}

	[TestMethod]
	public void Timeout_ExceedingInt32Milliseconds_IsRejected()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ProcessRunRequest
		{
			FileName = "compiler.exe",
			Timeout = TimeSpan.FromDays(30)
		});
	}

	[TestMethod]
	public void Timeout_Infinite_IsAccepted()
	{
		var request = new ProcessRunRequest
		{
			FileName = "compiler.exe",
			Timeout = Timeout.InfiniteTimeSpan
		};

		Assert.AreEqual(Timeout.InfiniteTimeSpan, request.Timeout);
	}

	[TestMethod]
	public void Timeout_Zero_IsAccepted()
	{
		var request = new ProcessRunRequest
		{
			FileName = "compiler.exe",
			Timeout = TimeSpan.Zero
		};

		Assert.AreEqual(TimeSpan.Zero, request.Timeout);
	}
}
