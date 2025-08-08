using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AeonHacs;

public class StepTracker : INotifyPropertyChanged
{
    public static StepTracker DefaultMajor = new StepTracker(); // major steps
    public static StepTracker Default = new StepTracker();        // minor steps

    public event PropertyChangedEventHandler PropertyChanged;
    public string Name { get; set; }

    public Step CurrentStep
    {
        get => currentStep;
        set
        {
            void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(Step.Description))
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            }

            if (currentStep != null)
                currentStep.PropertyChanged -= OnPropertyChanged;
            currentStep = value;
            if (currentStep != null)
                currentStep.PropertyChanged += OnPropertyChanged;
            
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentStep)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Elapsed)));
        }
    }
    Step currentStep;

    public Stack<Step> Stack { get; set; }
    public TimeSpan LastElapsed { get; set; }

    public StepTracker() { }

    public StepTracker(string name)
    {
        Name = name;
        Stack = new Stack<Step>();
        LastElapsed = TimeSpan.Zero;
    }

    public void Start(string desc)
    {
        if (CurrentStep != null)
            Stack.Push(CurrentStep);
        CurrentStep = new Step(desc);
        Hacs.SystemLog.Record($"{Name}: Start {CurrentStep.Description}");
    }

    public void End()
    {
        LastElapsed = Elapsed;
        if (CurrentStep == null)
        {
            Notify.Announce($"{Name} Push/Pop mismatch",
                type: NoticeType.Error);
        }
        else if (Stack.Count > 0)
        {
            Hacs.SystemLog.Record($"{Name}: End {CurrentStep.Description}");
            CurrentStep = Stack.Pop();
        }
        else
            CurrentStep = null;
    }

    public void Clear()
    {
        if (Stack.Count > 0)
            Hacs.SystemLog.Record($"{Name}: Cleared");
        LastElapsed = Elapsed;
        CurrentStep = null;
        Stack.Clear();
    }

    public string Description
    {
        get
        {
            if (CurrentStep == null)
                return "";
            else
                return CurrentStep.Description;
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            if (CurrentStep == null)
                return LastElapsed;
            else
                return DateTime.Now.Subtract(CurrentStep.StartTime);
        }
    }
    public override string ToString()
    {
        return Description;
    }
}

public class Step : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public string Description
    {
        get => description;
        set { description = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description))); }
    }
    string description;

    public DateTime StartTime
    {
        get => startTime;
        set { startTime = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartTime))); }
    }
    DateTime startTime;

    public Step() { }

    public Step(string desc)
    {
        Description = desc;
        StartTime = DateTime.Now;
    }
}
