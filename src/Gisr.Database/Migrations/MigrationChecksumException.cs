namespace Gisr.Database.Migrations;

public sealed class MigrationChecksumException(string scriptId)
    : Exception($"Applied migration '{scriptId}' has changed since it was applied. Applied migrations must not be edited; add a new migration instead.")
{
    public string ScriptId { get; } = scriptId;
}
