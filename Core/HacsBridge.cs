using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Resources;
using System.Security.Cryptography.X509Certificates;

namespace AeonHacs;

/// <summary>
/// Coordinates the startup and shutdown of a user interface and a hacs implementation
/// </summary>
public class HacsBridge
{
    public static ResourceManager Resources { get; set; }
    public Action CloseUI;
    public Action Started;

    public HacsBase HacsImplementation { get; protected set; }

    public bool Initialized { get; protected set; }

    protected JsonSerializer JsonSerializer { get; set; }

    public string SettingsFilename
    {
        get => settingsFilename;
        set
        {
            if (!value.IsBlank())
                settingsFilename = value;
        }
    }
    static string settingsFilename = "settings.json";
    static string SettingsFile(string which) => $"{settingsFilename.Split(".")[0]}.{which}.json";
    static string backupSettingsFilename(int i) => SettingsFile($"backup{i}");

    int backupsToKeep { get; set; } = 5;
    static TimeSpan backupInterval { get; set; } = TimeSpan.FromMinutes(5);
    static DateTime getLatestBackupTime()
    {
        var ltBackup = backupSettingsFilename(2);

        if (File.Exists(ltBackup))
        {
            var dt = File.GetLastWriteTime(ltBackup);
            if (dt < DateTime.Now)
                return dt;
        }
        return DateTime.Now.Subtract(backupInterval);
    }

    DateTime latestBackupTime { get; set; } = getLatestBackupTime();

    public HacsBridge()
    {
        JsonSerializer = new JsonSerializer()
        {
            //Converters = { new StringEnumConverter(), HideNameInDictionaryConverter.Default },
            Converters = { new StringEnumConverter() },
            //DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,
            DefaultValueHandling = DefaultValueHandling.Populate,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            FloatFormatHandling = FloatFormatHandling.String,
            TypeNameHandling = TypeNameHandling.Auto
        };
    }

    public virtual void Start()
    {
        Hacs.CloseApplication = CloseUI;

        List<string> files = [
            settingsFilename,
            backupSettingsFilename(1)
        ];

        for (int i = 2; i <= backupsToKeep; i++)
            files.Add(backupSettingsFilename(i));

        var works = files.FirstOrDefault(LoadSettings);

        string subject, message;

        if (works.IsBlank())
        {
            Hacs.EventLog.Record("No settings file found. Application closing.");
            CloseUI();
            return;
        }
       
        if (works != settingsFilename)
        {
            if (!Notify.Warn("Settings loaded from a backup",
                $"Successfully loaded settings from '{works}'.\r\n" +
                $"Backup timestamp: {File.GetLastWriteTime(works)}.\r\n" +
                $"Ok to continue with these settings?\r\n" +
                $"Cancel to close application.").Ok())
            {
                CloseUI();
                return;
            }
        }

        Hacs.Connect();
        Started?.Invoke();
    }

    private void loadJson(string settingsFile)
    {
        using (var reader = new StreamReader(settingsFile))
            HacsImplementation = (HacsBase)JsonSerializer.Deserialize(reader, typeof(HacsBase));
    }

    protected virtual bool LoadSettings(string settingsFile)
    {
        try
        {
            loadJson(settingsFile);
        }
        catch (Exception e) // Unable to load settings.json;
        {
            if (e is not FileNotFoundException)
            {
                Notify.Pause("Json Deserialization Exception", $"{e}\r\nApplication will close.", NoticeType.Error);
            }
            HacsImplementation = default;
            return false;
        }
        if (HacsImplementation == null) return false;
        HacsImplementation.SaveSettings = SaveSettings;
        HacsImplementation.SaveSettingsToFile = SaveSettings;
        return true;
    }

    private void saveJson(string filename)
    {
        using (var stream = File.CreateText(filename))
            JsonSerializer?.Serialize(stream, HacsImplementation, typeof(HacsBase));
    }

    protected virtual void SaveSettings() { SaveSettings(SettingsFilename); }

    protected virtual void SaveSettings(string filename)
    {
        if (filename.IsBlank())
            throw new NullReferenceException("Settings filename can not be null or whitespace.");

        try
        {
            if (filename != SettingsFilename)
            {
                saveJson(filename);
                return;
            }
            saveJson(SettingsFile("~temp~"));
            File.Delete(backupSettingsFilename(1));
            if (File.Exists(settingsFilename))
                File.Move(settingsFilename, backupSettingsFilename(1));
            File.Move(SettingsFile("~temp~"), settingsFilename);

            if (DateTime.Now.Subtract(latestBackupTime) > backupInterval)
            {
                File.Delete(backupSettingsFilename(backupsToKeep));
                for (int i = backupsToKeep - 1; i > 0; i--)
                {
                    if (File.Exists(backupSettingsFilename(i)))
                        File.Move(backupSettingsFilename(i), backupSettingsFilename(i + 1));
                }
                latestBackupTime = DateTime.Now;
            }
        }
        catch
        {
            // Typically a user has tried to reload settings.json just before a save.
            // Do we need to do anything here? The exception being caught is important
            // so the save loop doesn't break.
        }
    }
}
