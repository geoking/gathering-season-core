using System.Text.Json;
using GatheringSeason.Core.Ducks.Persistence;
using GatheringSeason.Core.Ducks.Runtime;
using GatheringSeason.Core.Match;

namespace GatheringSeason.Cli;

/// <summary>The host owns JSON and atomic local writes; Core owns the saved rules state.</summary>
internal sealed class DuckSaveStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true
    };

    internal DuckSaveStore(string path)
    {
        SavePath = Path.GetFullPath(path);
    }
    internal string SavePath { get; }
    internal string BackupPath => SavePath + ".bak";
    internal bool HasSavedGame => File.Exists(SavePath) || File.Exists(BackupPath);

    internal MatchSession<DuckMatchView> Load(out bool recoveredBackup)
    {
        recoveredBackup = false;
        Exception primaryError;
        try { return Read(SavePath); }
        catch (Exception exception) when (IsSaveError(exception)) { primaryError = exception; }

        try
        {
            var match = Read(BackupPath);
            recoveredBackup = true;
            return match;
        }
        catch (Exception exception) when (IsSaveError(exception))
        {
            throw new InvalidDataException($"Could not continue the saved game. Primary save: {primaryError.Message} Backup: {exception.Message}", primaryError);
        }
    }

    internal void Save(MatchSession<DuckMatchView> match)
    {
        var json = JsonSerializer.Serialize(DuckSaves.Capture(match), JsonOptions);
        var directory = Path.GetDirectoryName(SavePath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".gathering-season-save-" + Guid.NewGuid().ToString("N") + ".tmp");
        var backupTemporary = temporary + ".bak";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            // Keep a validated previous action. A corrupt primary must not replace a good backup.
            if (File.Exists(SavePath) && IsValidSave(SavePath))
            {
                File.Copy(SavePath, backupTemporary);
                File.Move(backupTemporary, BackupPath, overwrite: true);
            }
            File.Move(temporary, SavePath, overwrite: true);
        }
        finally
        {
            RemoveTemporary(temporary);
            RemoveTemporary(backupTemporary);
        }
    }

    private static MatchSession<DuckMatchView> Read(string path)
    {
        var data = JsonSerializer.Deserialize<DuckSaveData>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The save file is empty.");
        return DuckSaves.Restore(data);
    }

    private static bool IsValidSave(string path)
    {
        try { Read(path); return true; }
        catch (Exception exception) when (IsSaveError(exception)) { return false; }
    }

    private static bool IsSaveError(Exception exception) => exception is IOException or InvalidDataException
        or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException;

    private static void RemoveTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
