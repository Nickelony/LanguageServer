namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Creates process handles from run requests, abstracted so the orchestration in
/// <see cref="ProcessRunner"/> can be tested without launching real processes.
/// </summary>
internal interface IProcessLauncher
{
	/// <summary>
	/// Starts the executable or shell target described by the request.
	/// </summary>
	/// <param name="request">The process run request.</param>
	/// <returns>The started process handle, or <see langword="null"/> when the underlying launch mechanism does not
	/// return a process.</returns>
	IProcessHandle? Start(ProcessRunRequest request);
}
