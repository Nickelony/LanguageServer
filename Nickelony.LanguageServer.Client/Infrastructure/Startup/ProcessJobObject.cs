using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Wraps a Windows job object configured with <c>JobObjectLimitKillOnJobClose</c> so that any
/// child processes assigned to it are forcibly terminated when the host application crashes or the
/// last handle to the job is released. This prevents stranded server processes if the host never
/// gets a chance to run its disposal path.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessJobObject
{
	private const uint JobObjectLimitKillOnJobClose = 0x2000;

	private static ILogger s_logger = NullLogger.Instance;

	private static readonly object s_syncRoot = new();
	private static IntPtr s_jobHandle = IntPtr.Zero;
	private static bool s_initializationFailed;

	/// <summary>
	/// Sets the logger used for job-object allocation diagnostics.
	/// </summary>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	internal static void InitializeLogger(ILogger logger)
		=> s_logger = logger ?? NullLogger.Instance;

	/// <summary>
	/// Attempts to assign the supplied process to the shared kill-on-close Windows job object.
	/// </summary>
	/// <param name="process">The process to attach.</param>
	public static void TryAssignProcess(Process process)
	{
		if (process is null)
			return;

		if (!OperatingSystem.IsWindows())
			return;

		IntPtr jobHandle = EnsureJobHandle();

		if (jobHandle == IntPtr.Zero)
			return;

		try
		{
			if (!AssignProcessToJobObject(jobHandle, process.Handle))
			{
				int errorCode = Marshal.GetLastWin32Error();

				// ERROR_ACCESS_DENIED is expected when the process is already inside an unbreakable job.
				s_logger.LogDebug("AssignProcessToJobObject failed with Win32 error {ErrorCode} for the language-server process.", errorCode);
			}
		}
		catch (Exception exception)
		{
			s_logger.LogDebug(exception, "Failed to assign the language-server process to the kill-on-close job object.");
		}
	}

	/// <summary>
	/// Creates or returns the shared kill-on-close job-object handle.
	/// </summary>
	/// <returns>The shared job handle, or <see cref="IntPtr.Zero"/> when initialization failed.</returns>
	private static IntPtr EnsureJobHandle()
	{
		if (s_jobHandle != IntPtr.Zero)
			return s_jobHandle;

		if (s_initializationFailed)
			return IntPtr.Zero;

		lock (s_syncRoot)
		{
			if (s_jobHandle != IntPtr.Zero)
				return s_jobHandle;

			if (s_initializationFailed)
				return IntPtr.Zero;

			IntPtr handle = CreateJobObject(IntPtr.Zero, lpName: null);

			if (handle == IntPtr.Zero)
			{
				s_initializationFailed = true;

				s_logger.LogDebug("CreateJobObject returned NULL (Win32 error {ErrorCode}); the language server will rely on graceful shutdown.", Marshal.GetLastWin32Error());

				return IntPtr.Zero;
			}

			JobObjectExtendedLimitInformation extendedLimit = default;
			extendedLimit.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

			int payloadSize = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
			IntPtr payloadPointer = Marshal.AllocHGlobal(payloadSize);

			try
			{
				Marshal.StructureToPtr(extendedLimit, payloadPointer, fDeleteOld: false);

				if (!SetInformationJobObject(handle, JobObjectInformationClass.ExtendedLimitInformation, payloadPointer, (uint)payloadSize))
				{
					int errorCode = Marshal.GetLastWin32Error();

					CloseHandle(handle);
					s_initializationFailed = true;

					s_logger.LogDebug("SetInformationJobObject failed with Win32 error {ErrorCode}; the language server will rely on graceful shutdown.", errorCode);

					return IntPtr.Zero;
				}
			}
			finally
			{
				Marshal.FreeHGlobal(payloadPointer);
			}

			s_jobHandle = handle;

			AppDomain.CurrentDomain.ProcessExit += (_, _) => CloseHandle(handle);
			return handle;
		}
	}

	/// <summary>
	/// Identifies the job object information class used to set extended limit information.
	/// </summary>
	private enum JobObjectInformationClass
	{
		/// <summary>
		/// Selects the extended limit information structure.
		/// </summary>
		ExtendedLimitInformation = 9
	}

	/// <summary>
	/// Mirrors the native Windows I/O counters structure used by job objects.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct IoCounters
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	/// <summary>
	/// Mirrors the native Windows basic limit information structure for job objects.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectBasicLimitInformation
	{
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public uint LimitFlags;
		public UIntPtr MinimumWorkingSetSize;
		public UIntPtr MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public UIntPtr Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	/// <summary>
	/// Mirrors the native Windows extended limit information structure for job objects.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectExtendedLimitInformation
	{
		public JobObjectBasicLimitInformation BasicLimitInformation;
		public IoCounters IoInfo;
		public UIntPtr ProcessMemoryLimit;
		public UIntPtr JobMemoryLimit;
		public UIntPtr PeakProcessMemoryUsed;
		public UIntPtr PeakJobMemoryUsed;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInformationClass infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr hObject);
}
