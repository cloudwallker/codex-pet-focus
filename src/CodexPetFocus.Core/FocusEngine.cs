namespace CodexPetFocus.Core;

/// <summary>Only this object accrues time. UI refreshes and persistence never invent elapsed time.</summary>
public sealed class FocusEngine
{
    private readonly TimeProvider clock;
    private readonly TimeZoneInfo zone;
    private long lastStamp;
    private DateTimeOffset lastWall;
    public FocusData Data { get; }
    public Guid? RunningTaskId { get; private set; }

    public FocusEngine(FocusData data, TimeProvider? clock = null, TimeZoneInfo? zone = null)
    {
        Data = data;
        this.clock = clock ?? TimeProvider.System;
        this.zone = zone ?? TimeZoneInfo.Local;
    }

    public IReadOnlyList<FocusTask> AddPlan(string text, DateOnly date)
    {
        var titles = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        if (titles.Any(x => x.Length > 500))
            throw new ArgumentException("任务标题不能超过 500 个字符。");
        var added = titles.Select(title => new FocusTask { Title = title, PlannedDate = date }).ToArray();
        Data.Tasks.AddRange(added);
        return added;
    }

    public void Start(Guid id)
    {
        var task = RequireTask(id);
        if (task.Completed) throw new InvalidOperationException("已完成的任务不能启动。");
        if (RunningTaskId == id) { Tick(); return; }
        Pause();
        RunningTaskId = id;
        lastStamp = clock.GetTimestamp();
        lastWall = clock.GetUtcNow();
    }

    public void Pause()
    {
        Tick();
        RunningTaskId = null;
    }

    public void Tick()
    {
        if (RunningTaskId is not Guid id) return;
        var nowStamp = clock.GetTimestamp();
        var nowWall = clock.GetUtcNow();
        var elapsed = clock.GetElapsedTime(lastStamp, nowStamp);
        if (elapsed < TimeSpan.Zero) throw new InvalidOperationException("单调时钟不能倒退。");
        if (elapsed > TimeSpan.Zero) AddTime(id, lastWall, elapsed);
        lastStamp = nowStamp;
        // A wall-clock correction changes the calendar anchor only, never the elapsed duration.
        lastWall = nowWall;
    }

    private void AddTime(Guid id, DateTimeOffset start, TimeSpan elapsed)
    {
        var remaining = elapsed.Ticks;
        while (remaining > 0)
        {
            var local = TimeZoneInfo.ConvertTime(start, zone);
            var day = DateOnly.FromDateTime(local.DateTime);
            var nextLocal = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            // Some time zones skip midnight during a DST transition.
            while (zone.IsInvalidTime(nextLocal)) nextLocal = nextLocal.AddMinutes(1);
            var nextUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(nextLocal, zone));
            var untilBoundary = (nextUtc - start).Ticks;
            if (untilBoundary <= 0) throw new InvalidOperationException("无法计算本地日期边界。");
            var ticks = Math.Min(remaining, untilBoundary);
            var slice = Data.Slices.FirstOrDefault(s => s.TaskId == id && s.Date == day);
            if (slice is null) Data.Slices.Add(new TimeSlice { TaskId = id, Date = day, DurationTicks = ticks });
            else slice.DurationTicks = checked(slice.DurationTicks + ticks);
            start += TimeSpan.FromTicks(ticks);
            remaining -= ticks;
        }
    }

    public void Complete(Guid id, bool completed = true)
    {
        var task = RequireTask(id);
        if (completed && RunningTaskId == id) Pause();
        task.Completed = completed;
    }

    public int CarryYesterday(DateOnly today)
    {
        var sources = Data.Tasks.Where(t => t.PlannedDate == today.AddDays(-1) && !t.Completed).ToArray();
        int added = 0;
        foreach (var source in sources)
        {
            if (Data.Tasks.Any(t => t.PlannedDate == today && t.CarriedFromId == source.Id)) continue;
            Data.Tasks.Add(new FocusTask { Title = source.Title, PlannedDate = today, CarriedFromId = source.Id });
            added++;
        }
        return added;
    }

    public TimeSpan TaskTime(Guid id) => TimeSpan.FromTicks(Data.Slices.Where(s => s.TaskId == id).Sum(s => s.DurationTicks));
    public TimeSpan DayTime(DateOnly date) => TimeSpan.FromTicks(Data.Slices.Where(s => s.Date == date).Sum(s => s.DurationTicks));
    private FocusTask RequireTask(Guid id) => Data.Tasks.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException("找不到指定任务。");
}
