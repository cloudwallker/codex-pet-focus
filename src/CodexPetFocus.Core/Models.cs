namespace CodexPetFocus.Core;

public sealed class FocusTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public DateOnly PlannedDate { get; set; }
    public bool Completed { get; set; }
    public Guid? CarriedFromId { get; set; }
}

public sealed class TimeSlice
{
    public Guid TaskId { get; set; }
    public DateOnly Date { get; set; }
    public long DurationTicks { get; set; }
}

public sealed class FocusSettings
{
    public bool DoNotDisturb { get; set; }
}

public sealed class FocusData
{
    public int SchemaVersion { get; set; } = 1;
    public List<FocusTask> Tasks { get; set; } = [];
    public List<TimeSlice> Slices { get; set; } = [];
    public FocusSettings Settings { get; set; } = new();
}
