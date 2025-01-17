﻿using AeonHacs.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static AeonHacs.Notify;
using static AeonHacs.Utilities.Utility;

namespace AeonHacs.Components
{
    public class ProcessManager : HacsBase, IProcessManager
    {
        #region HacsComponent

        public ProcessManager()
        {
            StepTracker.DefaultMajor = ProcessStep;
            StepTracker.Default = ProcessSubStep;
            BuildProcessDictionary();
        }

        #endregion HacsComponent

        public enum ProcessStateCode { Ready, Busy, Finished }

        // TODO delete after testing
        //public HacsLog EventLog => Hacs.EventLog;

        #region process manager

        /// <summary>
        /// ProcessDictionary list group separators
        /// </summary>
        public List<int> Separators = new List<int>();

        public Dictionary<string, ThreadStart> ProcessDictionary { get; protected set; } = new Dictionary<string, ThreadStart>();
        public List<string> ProcessNames => ProcessDictionary.Keys.ToList();

        protected virtual void BuildProcessDictionary() { }

        [JsonProperty(Order = -98)]
        public Dictionary<string, ProcessSequence> ProcessSequences { get; set; } = new Dictionary<string, ProcessSequence>();

        public ProcessStateCode ProcessState
        {
            get => processState;
            protected set
            {
                if (Ensure(ref processState, value))
                    Busy = processState != ProcessStateCode.Ready;
            }
        }
        ProcessStateCode processState = ProcessStateCode.Ready;

        public virtual bool Busy
        {
            get => busy;
            protected set
            {
                if (Ensure(ref busy, value))
                    NotBusy = !busy;
            }
        }
        bool busy = false;

        public virtual bool NotBusy
        {
            get => notBusy;
            protected set => Ensure(ref notBusy, value);
        }
        bool notBusy = true;

        protected Thread ManagerThread { get; set; } = null;
        protected Thread ProcessThread { get; set; } = null;
        protected Stopwatch ProcessTimer { get; set; } = new Stopwatch();
        public TimeSpan ProcessTime => ProcessTimer.Elapsed;
        public StepTracker ProcessStep { get; protected set; } = new StepTracker("ProcessStep");
        public StepTracker ProcessSubStep { get; protected set; } = new StepTracker("ProcessSubStep");

        public virtual string ProcessToRun
        {
            get => processToRun;
            set => Ensure(ref processToRun, value);
        }
        string processToRun;
        public enum ProcessTypeCode { Simple, Sequence }
        public ProcessTypeCode ProcessType
        {
            get => processType;
            protected set => Ensure(ref processType, value);
        }
        ProcessTypeCode processType;
        public bool RunCompleted
        {
            get => runCompleted;
            protected set => Ensure(ref runCompleted, value);
        }
        bool runCompleted = false;

        public virtual bool ProcessSequenceIsRunning =>
            ProcessType == ProcessTypeCode.Sequence && !RunCompleted;

        public virtual void RunProcess(string processToRun)
        {
            Hacs.SystemLog.Record($"RunProcess: {processToRun}");
            if (ProcessState != ProcessStateCode.Ready)
            {
                Hacs.SystemLog.Record($"Can't start [{processToRun}]. [{ProcessToRun}] is running.");
                return;         // silently fail, for now
                //throw new Exception($"Can't start [{processToRun}]. [{ProcessToRun}] is running.");
            }
            ProcessToRun = processToRun;

            lock(ProcessTimer) RunCompleted = false;
            Busy = true;
            ManagerThread = new Thread(ManageProcess) { Name = $"{Name} ProcessManager", IsBackground = true };
            ManagerThread.Start();
        }

        public virtual void AbortRunningProcess()
        {
            if (ProcessThread?.IsAlive ?? false)
                ProcessThread.Abort();
        }

        // A Process runs in its own thread.
        // Only one Process can be executing at a time.
        protected void ManageProcess()
        {
            try
            {
                while (true)
                {
                    var priorState = ProcessState;
                    switch (ProcessState)
                    {
                        case ProcessStateCode.Ready:
                            if (!string.IsNullOrEmpty(ProcessToRun))
                            {
                                ProcessState = ProcessStateCode.Busy;
                                if (ProcessDictionary.TryGetValue(ProcessToRun, out ThreadStart process))
                                {
                                    ProcessType = ProcessTypeCode.Simple;
                                }
                                else
                                {
                                    ProcessType = ProcessTypeCode.Sequence;
                                    process = RunProcessSequence;
                                }

                                ProcessThread = new Thread(() => RunProcess(process))
                                {
                                    IsBackground = true,
                                    Name = $"{Name} RunProcess"
                                };
                                ProcessTimer.Restart();
                                ProcessStarting();
                                ProcessThread.Start();
                            }
                            break;
                        case ProcessStateCode.Busy:
                            if (ProcessThread == null || !ProcessThread.IsAlive)
                            {
                                ProcessState = ProcessStateCode.Finished;
                            }
                            break;
                        case ProcessStateCode.Finished:
                            ProcessStep.Clear();
                            ProcessSubStep.Clear();
                            ProcessTimer.Stop();

                            ProcessEnded();

                            ProcessThread = null;
                            ProcessToRun = null;
                            ProcessState = ProcessStateCode.Ready;

                            break;
                        default:
                            break;
                    }
                    if (priorState == ProcessStateCode.Finished)
                        break;
                    if (priorState == ProcessState)
                        WaitMilliseconds(200, null);
                }
            }
            catch { }
        }

        void RunProcess(ThreadStart process)
        {
            process?.Invoke();
            lock (ProcessTimer)
                RunCompleted = true;            // if the process is aborted, RunCompleted will not be set true;
        }

        protected virtual void ProcessStarting(string message = "")
        {
            if (message.IsBlank())
                message = $"Process{(ProcessType == ProcessTypeCode.Sequence ? " sequence" : "")} starting: {ProcessToRun}";
            Hacs.SystemLog.Record(message);
        }

        protected virtual void ProcessEnded(string message = "")
        {
            if (message.IsBlank())
                message = $"Process {(RunCompleted ? "completed" : "aborted")}: {ProcessToRun}";
            Hacs.SystemLog.Record(message);
        }

        #region ProcessSequences
        public ProcessSequence CurrentProcessSequence { get; set; } = null;

        void RunProcessSequence()
        {
            CurrentProcessSequence = ProcessSequences.Values.ToList().Find(x => x?.Name == ProcessToRun);

            if (CurrentProcessSequence == null)
                throw new Exception("No such Process Sequence: \"" + ProcessToRun + "\"");

            foreach (ProcessSequenceStep step in CurrentProcessSequence.Steps)
            {
                ProcessStep.Start(step.Name);
                if (step is CombustionStep cs)
                    Combust(cs.Temperature, cs.Minutes, cs.AdmitO2, cs.WaitForSetpoint);
                else if (step is WaitMinutesStep wms)
                    WaitMinutes(wms.Minutes);
                else if (step is ParameterStep sps)
                    SetParameter(sps.Parameter);
                else
                    ProcessDictionary[step.Name]();
                ProcessStep.End();
            }
            CurrentProcessSequence = null;
        }

        #region parameterized process steps
        public virtual void SetParameter(Parameter parameter) { }

        // The derived class can implement a Combust() process
        // (if it doesn't those steps won't do anything)
        protected virtual void Combust(int temperature, int minutes, bool admitO2, bool waitForSetpoint) { }

        #endregion parameterized process steps

        #endregion ProcessSequences

        #endregion process manager
    }
}