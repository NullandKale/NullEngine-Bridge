using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NullEngine.Video
{
    public static class WindowsJob
    {
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint CreateJobObject(nint lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(nint hJob, JOBOBJECTINFOCLASS infoClass,
            nint lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(nint hProcess, uint uExitCode);

        private enum JOBOBJECTINFOCLASS
        {
            BasicLimitInformation = 2,
            ExtendedLimitInformation = 9,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        private static readonly nint jobHandle;

        static WindowsJob()
        {
            jobHandle = CreateJobObject(nint.Zero, null);

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int length = Marshal.SizeOf(info);
            nint infoPtr = Marshal.AllocHGlobal(length);
            Marshal.StructureToPtr(info, infoPtr, false);

            SetInformationJobObject(jobHandle, JOBOBJECTINFOCLASS.ExtendedLimitInformation,
                infoPtr, (uint)length);

            Marshal.FreeHGlobal(infoPtr);
        }

        public static void AddProcess(Process process)
        {
            if (process != null && !process.HasExited)
            {
                AssignProcessToJobObject(jobHandle, process.Handle);
            }
        }
    }
}
