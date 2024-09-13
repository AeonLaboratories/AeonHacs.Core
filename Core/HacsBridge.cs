﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.IO;
using System.Resources;

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
            {
                settingsFilename = value;
                int period = settingsFilename.LastIndexOf('.');
                if (period < 0) period = settingsFilename.Length;
                backupSettingsFilename = settingsFilename.Insert(period, ".backup");
            }
        }
    }
    string settingsFilename = "settings.json";
    string backupSettingsFilename = "settings.backup.json";

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
        LoadSettings(settingsFilename);
        if (HacsImplementation == null)
        {
            CloseUI();
            return;
        }
        Hacs.Connect();
        Started?.Invoke();
    }

    private void loadJson(string settingsFile)
    {
        using (var reader = new StreamReader(settingsFile))
            HacsImplementation = (HacsBase)JsonSerializer.Deserialize(reader, typeof(HacsBase));
    }

    protected virtual void LoadSettings(string settingsFile)
    {
        try
        {
            loadJson(settingsFile);
        }
        catch (Exception e) // Unable to load settings.json;
        {
            var subject = "Json Deserialization Error";
            var message = e.ToString();

            Notify.Ask(message, subject, NoticeType.Error);
            HacsImplementation = default;
            return;
        }
        HacsImplementation.SaveSettings = SaveSettings;
        HacsImplementation.SaveSettingsToFile = SaveSettings;
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
            if (filename == SettingsFilename)
            {
                File.Delete(backupSettingsFilename);
                File.Move(settingsFilename, backupSettingsFilename);
            }

            saveJson(filename);
        }
        catch
        {
            // Typically a user has tried to reload settings.json just before a save.
            // Do we need to do anything here? The exception being caught is important
            // so the save loop doesn't break.
        }
    }
}
