using System.Diagnostics;

namespace Nickelony.IDEKit.Tooling;

/// <summary>
/// Adapts <see cref="Process"/> to <see cref="IProcessHandle"/>.
/// </summary>
/// <remarks>
/// Standard output and error are read on demand, so callers should read them only when the corresponding stream was
/// redirected and after the process has exited when complete output is required.
/// </remarks>
internal sealed class ProcessHandle : IProcessHandle
{
	private readonly Process _process;

	public ProcessHandle(Process process)
		=> _process = process;

	public int ExitCode
		=> _process.ExitCode;

	public string? StandardOutput
		=> _process.StandardOutput.ReadToEnd();

	public string? StandardError
		=> _process.StandardError.ReadToEnd();

	public void WaitForExit()
		=> _process.WaitForExit();

	public bool WaitForExit(int timeoutMilliseconds)
		=> _process.WaitForExit(timeoutMilliseconds);

	public void Kill()
		=> _process.Kill();

	public void KillEntireProcessTree()
		=> _process.Kill(entireProcessTree: true);

	public void Dispose()
		=> _process.Dispose();
}
