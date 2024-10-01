using System;
using System.Threading;

namespace AeonHacs
{
    public static class Hacs
    {
        private static readonly CancellationTokenSource cancellationTokenSource = new();
        public static CancellationToken CancellationToken => cancellationTokenSource.Token;

        public static Action CloseApplication;
        public static bool RestartRequested { get; set; }

        static HacsLog eventLog;
        public static HacsLog EventLog => eventLog ??=
            new HacsLog("Event log.txt") { Name = "EventLog", ArchiveDaily = false };

        static HacsLog systemLog;
        public static HacsLog SystemLog => systemLog ??=
            new HacsLog("System log.txt") { Name = "SystemLog", ArchiveDaily = true };

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

        static Hacs()
        {
            Notify.OnActionTaken += notice => SystemLog.Record(notice.Message);
            Notify.OnMajorEvent += notice => EventLog.Record(notice.Message);
            Notify.OnError += notice =>
            {
                EventLog.Record(notice.Message);
                return Notice.NoResponse;
            };
        }

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
            if (Stopping) return;
            Stopping = true;
            cancellationTokenSource.Cancel();
            OnPreStop?.ParallelInvoke();
            OnStop?.ParallelInvoke();
            OnPostStop?.ParallelInvoke();
            HacsLog.List.ForEach(log => { if (log != EventLog) log.Close(); });
            // Event log should be closed immediately before Application exits.
            Stopped = true;
        }
    }
}