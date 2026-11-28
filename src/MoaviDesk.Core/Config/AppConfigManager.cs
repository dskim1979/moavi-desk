/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using SystemIO = System.IO;

namespace MoaviDesk.Config;

internal static class AppConfigManager
{
    private static readonly string ConfigDir = SystemIO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".moavi-desk");

    private static readonly string ConfigFile = SystemIO.Path.Combine(ConfigDir, "config");

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Copy of the configuration file that could not be read by <see cref="Load"/>, shown to the user
    /// at login; null when the file was read (or there was none).
    /// </summary>
    public static string? UnreadableConfigBackup { get; private set; }

    // the user's own values as last read or written: what a managed setting keeps in their file
    private static AppConfig _userValues = new();

    /// <summary>
    /// Reads in this order, each over the one before: defaults, the user's file, the PC's config
    /// (%ProgramData%\MOAVI Desk\config) and the terminal's key (%ProgramData%\MOAVI Desk\terminal).
    /// </summary>
    public static AppConfig Load()
    {
        var config = LoadUser();
        _userValues = LoadUser();
        MachineConfig.ApplyTo(config);
        return config;
    }

    private static AppConfig LoadUser()
    {
        if (!File.Exists(ConfigFile)) { return new AppConfig(); }
        try
        {
            var config = Deserializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile)) ?? new AppConfig();

            return config;
        }
        catch
        {
            // Preserve the unreadable file instead of letting the next Save overwrite it
            // with a fresh default config.
            try
            {
                var backup = $"{ConfigFile}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
                WritePrivate(backup, File.ReadAllBytes(ConfigFile), FileMode.CreateNew);
                UnreadableConfigBackup = backup;
            }
            catch { }

            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);

        // the PC's settings stay out of the user's file: it keeps the user's own values for them
        var own = Deserializer.Deserialize<AppConfig>(Serializer.Serialize(config)) ?? new AppConfig();
        foreach (var name in config.Managed)
        {
            var p = typeof(AppConfig).GetProperty(name)!;
            p.SetValue(own, p.GetValue(_userValues));
        }
        _userValues = own;

        // Write to a temp file in the same directory, then move over the target:
        // a crash mid-write must not leave a truncated config behind.
        // The temp file is created 600, so the config takes that mode with the move.
        var tmpFile = $"{ConfigFile}.tmp";
        File.Delete(tmpFile);
        WritePrivate(tmpFile, System.Text.Encoding.UTF8.GetBytes(Serializer.Serialize(own)), FileMode.CreateNew);
        File.Move(tmpFile, ConfigFile, overwrite: true);

        // A readable config is on disk again: the login notice about the backup is no longer needed.
        UnreadableConfigBackup = null;
    }

    /// <summary>
    /// Writes a file readable only by the user (600 on Linux/macOS) from the moment it is created:
    /// the configuration holds the kiosk password hash and the terminal key, so no copy of it may be world-readable.
    /// </summary>
    private static void WritePrivate(string path, byte[] content, FileMode mode)
    {
        var options = new FileStreamOptions
        {
            Mode = mode,
            Access = FileAccess.Write
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(path, options);
        stream.Write(content);
    }
}
