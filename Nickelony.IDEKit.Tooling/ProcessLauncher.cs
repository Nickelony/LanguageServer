using System.Diagnostics;

namespace Nickelony.IDEKit.Tooling;

/// <summary>
/// Builds a <see cref="ProcessStartInfo"/> from a <see cref="ProcessRunRequest"/> and starts the process.
/// </summary>
internal sealed class ProcessLauncher : IProcessLauncher
{
	public IProcessHandle? Start(ProcessRunRequest request)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = request.FileName,
			Arguments = request.Arguments,
			WorkingDirectory = request.WorkingDirectory,
			UseShellExecute = request.UseShellExecute,
			RedirectStandardOutput = request.RedirectStandardOutput,
			RedirectStandardError = request.RedirectStandardError,
			StandardOutputEncoding = request.StandardOutputEncoding,
			StandardErrorEncoding = request.StandardErrorEncoding
		};

		foreach ((string name, string value) in request.EnvironmentVariables)
			startInfo.Environment[name] = value;

		Process? process = Process.Start(startInfo);

		return process is null
			? null
			: new ProcessHandle(process);
	}
}
