using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AeonHacs.Utilities;

public class LogFile
{
    static List<LogFile> List = new List<LogFile>();
    static Thread FlusherThread;
    static LogFile()
    {
        FlusherThread = new Thread(flusher)
        {
            Name = "LogFile Flusher",
            IsBackground = true
        };
        FlusherThread.Start();
    }

    static void flusher()
    {
        try
        {
            while (true)
            {
                Thread.Sleep(5000); // wait 5 seconds
                foreach (var log in List)
                {
                    try { log.flush(); }
                    catch (Exception e)
                    {
                        Notify.Announce($"Exception writing to {log.fileName}", 
                            type: NoticeType.Error);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Notify.Announce($"Exception in {typeof(LogFile).Name}.flusher()", 
                e.ToString(), type: NoticeType.Error);
        }
    }

    ConcurrentQueue<string> Q = new ConcurrentQueue<string>();
    Stopwatch sw = new Stopwatch();
    public string FileName
    {
        get => fileName;
        set
        {
            if (fileName != value)
            {
                Close();
                fileName = value;
                sw.Restart();
            }
        }
    }
    string fileName = "Log.txt";
    string FullFileName => LogFolder + FileName;

    public long ElapsedMilliseconds => sw.ElapsedMilliseconds;
    public string TimeStampFormat { get; set; }
    public string TimeStamp()
    { return DateTime.Now.ToString(TimeStampFormat); }

    public bool ArchiveDaily { get; set; }
    public string LogFolder { get; set; }
    public string ArchiveFolder { get; set; }
    public string Header = "";


    public LogFile()
    {
        TimeStampFormat = "MM/dd/yyyy HH:mm:ss.fff\t";
        LogFolder  = @".\";
        ArchiveFolder = @"archive\";
        List.Add(this);
    }

    public LogFile(string filename, bool archiveDaily) : this()
    {
        ArchiveDaily = archiveDaily;
        FileName = filename;
        sw.Restart();
    }

    public LogFile(string filename) : this(filename, false) {}

    ~LogFile() { flush(); }

    public void Close()
    {
        flush();
    }

    /// <summary>
    /// Write the entry into the log file with no newline added.
    /// </summary>
    /// <param name="entry"></param>
    public void Write(string entry)
    {
        Q.Enqueue(entry);
        sw.Restart();
    }

    /// <summary>
    /// Writes a one-line entry into the logfile (adds "\r\n").
    /// </summary>
    /// <param name="entry"></param>
    public void WriteLine(string entry)
    { Write(entry + "\r\n"); }

    /// <summary>
    /// Writes a one-line timestamp and entry to the log file
    /// </summary>
    /// <param name="entry">Text to appear after the time stamp</param>
    public void Record(string entry)
    { WriteLine(TimeStamp() + entry); }

    string recordedEntry = "";
    string culledTimestamp = "";
    /// <summary>
    /// Writes a one-line timestamp and entry to the log file if
    /// the entry is not the same as the last entry.
    /// </summary>
    /// <param name="entry">Text to appear after the time stamp</param>
    public void LogParsimoniously(string entry)
    {
        if (entry == recordedEntry)
            culledTimestamp = TimeStamp();
        else
        {
            if (culledTimestamp != "")
            {
                WriteLine(culledTimestamp + recordedEntry);
                culledTimestamp = "";
            }
            WriteLine(TimeStamp() + entry);
            recordedEntry = entry;
        }
    }

    void archive()
    {
        try
        {
            int dot = FileName.IndexOf(".");
            if (dot < 0) dot = FileName.Length;
            string archiveFileName = ArchiveFolder + FileName.Insert(dot, DateTime.Now.AddDays(-1).ToString(" yyyyMMdd"));
            if (File.Exists(FullFileName) && !File.Exists(archiveFileName))
                File.Move(FullFileName, archiveFileName);
        }
        catch { }
    }

    bool flush()
    {
        if (!Q.IsEmpty)
        {
            try
            {
                if (ArchiveDaily) archive();
                bool newFile = !File.Exists(FullFileName);
                StreamWriter logFile = new StreamWriter(FullFileName, true);
                if (newFile && !string.IsNullOrEmpty(Header))
                    logFile.WriteLine(Header);
                string entry;
                while (Q.TryDequeue(out entry))
                    logFile.Write(entry);
                logFile.Close();
            }
            catch { return false; }
        }
        return true;
    }
}
