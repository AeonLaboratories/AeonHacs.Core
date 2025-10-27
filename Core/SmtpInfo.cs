using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using static AeonHacs.Notify;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace AeonHacs;

/// <summary>
/// An Email account configuration (name, email address, server, port, etc).
/// </summary>
public class SmtpInfo : BindableObject
{
    #region static
    // TODO: Add a DefaultSerializer with 'standard' settings to AeonHacs.Core
    static JsonSerializer Serializer = new JsonSerializer()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    const string DefaultCredentialsFilename = "Credentials.json";

    /// <summary>
    /// Load Email account configuration from a file.
    /// </summary>
    /// <param name="credentialsFileName">The name of the json file to load.</param>
    /// <returns>An SmtpInfo instance containing the loaded Email account configuration</returns>
    public static SmtpInfo Load(string credentialsFileName)
    {
        SmtpInfo info = default;
        try
        {
            using (var reader = new StreamReader(credentialsFileName))
                info = (SmtpInfo)Serializer.Deserialize(reader, typeof(SmtpInfo));
            info.loadingCredentialsFile = false;
            credentialsOk = true;
        }
        catch (Exception e)
        {
            if (credentialsOk)          // avoid nagging
            {
                credentialsOk = false;
                if (e is FileNotFoundException)
                {
                    Announce($"Credentials file \"{credentialsFileName}\" is missing.");
                }
                else
                {
                    Announce("Exception loading credentials",
                        e.ToString(), type: NoticeType.Error);
                }
            }
        }
        return info;
    }
    static bool credentialsOk = true;

    /// <summary>
    /// Default Email account configuration, loaded from "Credentials.json".
    /// </summary>
    public static SmtpInfo DefaultSmtpInfo => Load(DefaultCredentialsFilename);

    #endregion static


    /// <summary>
    /// Email service provider (default: mail.aeonhacs.com)
    /// </summary>
    [JsonProperty, DefaultValue("mail.aeonhacs.com")]
    public string Host
    {
        get => host;
        set => Ensure(ref host, value, OnPropertyChanged);
    }
    string host = "mail.aeonhacs.com";

    /// <summary>
    /// Email SMTP port (default: 465)
    /// </summary>
    [JsonProperty, DefaultValue(465)]
    public int Port
    {
        get => port;
        set => Ensure(ref port, value, OnPropertyChanged);
    }
    int port = 465;

    /// <summary>
    /// The name of the Email Sender, typically the HACS instrument or application.
    /// </summary>
    [JsonProperty, DefaultValue("MySystemName")]
    public string SenderName
    {
        get => senderName;
        set => Ensure(ref senderName, value, OnPropertyChanged);
    }
    string senderName = "MySystemName";

    /// <summary>
    /// Full email address / account name (e.g. "MySystemName@aeonhacs.com")
    /// </summary>
    [JsonProperty]
    public string EmailAddress
    {
        get => emailAddress;
        set => Ensure(ref emailAddress, value, OnPropertyChanged);
    }
    string emailAddress;

    /// <summary>
    /// Email account password
    /// </summary>
    [JsonProperty]
    public string Password
    {
        get => password;
        set => Ensure(ref password, value, OnPropertyChanged);
    }
    string password;

    /// <summary>
    /// Name of json file with Email account and configuration.
    /// </summary>
    public string CredentialsFilename = DefaultCredentialsFilename;
    private bool loadingCredentialsFile = true;
    void Save()
    {
        if (loadingCredentialsFile) return;
        var backup = CredentialsFilename + ".backup";
        try { File.Delete(backup); } catch { }
        try { File.Move(CredentialsFilename, backup); } catch { }

        try
        {
            using (var stream = File.CreateText(CredentialsFilename))
                Serializer.Serialize(stream, this);
        }
        catch (Exception e)
        {
            Announce($"Unable to save {CredentialsFilename}", 
                e.ToString(), type: NoticeType.Error);
        }
    }


    /// <summary>
    /// Event handler called when a property of the instance changes.
    /// </summary>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">PropertyChanged event data</param>
    protected virtual void OnPropertyChanged(object sender = null, PropertyChangedEventArgs e = null)
    {
        Save();
    }
}
