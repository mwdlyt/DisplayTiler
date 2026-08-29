namespace DisplayTiler.Core;

public sealed record ApplicationGroup(
    string Key,
    string Name,
    IReadOnlyList<WindowRecord> Windows,
    long LastActivatedUnixMilliseconds);
