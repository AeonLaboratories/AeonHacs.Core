using System;

namespace AeonHacs
{
    public static class Hacs
    {
        public static Action CloseApplication;
        public static HacsLog EventLog
        {
            get => eventLog ??= new HacsLog("Event log.txt") { Name = "EventLog", ArchiveDaily = false };
            set => eventLog = value;
        }
        static HacsLog eventLog;
        public static HacsLog SystemLog
        {
            get => systemLog ??= new HacsLog("System log.txt") { Name = "SystemLog", ArchiveDaily = true };
            set => systemLog = value;
        }
        static HacsLog systemLog;
        public static bool Connected { get; private set; }
        public static bool Initialized { get; private set; }
        public static bool Started { get; private set; }
        public static bool Stopping { get; private set; }
        public static bool Stopped { get; private set; }

        public static Action OnPreConnect;
        public static Action OnConnect;
        public static Action OnPostConnect;

        public static Action OnPreInitialize;
        public static Action OnInitialize;
        public static Action OnPostInitialize;

        public static Action OnPreStart;
        public static Action OnStart;
        public static Action OnPostStart;

        public static Action OnPreUpdate;
        public static Action OnUpdate;
        public static Action OnPostUpdate;

        public static Action OnPreStop;
        public static Action OnStop;
        public static Action OnPostStop;

        public static void Connect()
        {
            OnPreConnect?.ParallelInvoke();
            OnConnect?.ParallelInvoke();
            OnPostConnect?.ParallelInvoke();
            Connected = true;
        }

        public static void Initialize()
        {
            OnPreInitialize?.ParallelInvoke();
            OnInitialize?.ParallelInvoke();
            OnPostInitialize?.ParallelInvoke();
            Initialized = true;
        }

        public static void Start()
        {
            OnPreStart?.ParallelInvoke();
            OnStart?.ParallelInvoke();
            OnPostStart?.ParallelInvoke();
            Started = true;
        }

        public static void Update()
        {
            OnPreUpdate?.ParallelInvoke();
            OnUpdate?.ParallelInvoke();
            OnPostUpdate?.ParallelInvoke();
        }

        public static void Stop()
        {
            Stopping = true;
            OnPreStop?.ParallelInvoke();
            OnStop?.ParallelInvoke();
            OnPostStop?.ParallelInvoke();
            HacsLog.List.ForEach(log => { if (log != EventLog) log.Close(); });
            // Event log should be closed immediately before Application exits.
            Stopped = true;
        }
    }
}