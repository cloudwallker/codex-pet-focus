using CodexPetFocus.Core;

var cases = new (string, Action)[]
{
    ("版本字段损坏时从有效备份恢复", () => WithDirectory(dir => {
        var store = new JsonStore(dir); var data = new FocusData();
        data.Tasks.Add(new FocusTask { Title = "保留任务", PlannedDate = new(2026,9,15) });
        store.Save(data); store.Save(data);
        File.WriteAllText(Path.Combine(dir,"focus.json"), "{\"schemaVersion\":\"broken\"}");
        var recovered = store.Load(); Equal(1, recovered.Tasks.Count); Contains("备份", store.RecoveryNotice!);
    })),

    ("逐行录入与空白过滤", () => {
        var e = Engine(out _); var tasks = e.AddPlan("  阅读  \r\n\n写作\n", new(2026, 9, 15));
        Equal(2, tasks.Count); Equal("阅读", tasks[0].Title);
    }),
    ("暂停恢复累计且暂停期间不计时", () => {
        var e = Engine(out var c); var t = e.AddPlan("阅读", new(2026,9,15))[0];
        e.Start(t.Id); c.Advance(30); e.Pause(); c.Advance(120); e.Start(t.Id); c.Advance(15); e.Tick();
        Equal(TimeSpan.FromSeconds(45), e.TaskTime(t.Id));
    }),
    ("切换任务不重叠计时", () => {
        var e = Engine(out var c); var t = e.AddPlan("A\nB", new(2026,9,15));
        e.Start(t[0].Id); c.Advance(10); e.Start(t[1].Id); c.Advance(20); e.Tick();
        Equal(TimeSpan.FromSeconds(10), e.TaskTime(t[0].Id)); Equal(TimeSpan.FromSeconds(20), e.TaskTime(t[1].Id));
    }),
    ("重复启动不丢失时间", () => {
        var e = Engine(out var c); var t = e.AddPlan("A", new(2026,9,15))[0];
        e.Start(t.Id); c.Advance(10); e.Start(t.Id); c.Advance(20); e.Pause(); Equal(TimeSpan.FromSeconds(30), e.TaskTime(t.Id));
    }),
    ("完成正在运行的任务会暂停", () => {
        var e = Engine(out var c); var t = e.AddPlan("A", new(2026,9,15))[0];
        e.Start(t.Id); c.Advance(10); e.Complete(t.Id); c.Advance(20); e.Tick();
        Equal(true, t.Completed); Equal<Guid?>(null, e.RunningTaskId); Equal(TimeSpan.FromSeconds(10), e.TaskTime(t.Id));
        Throws<InvalidOperationException>(() => e.Start(t.Id));
    }),
    ("无效任务不暂停当前任务", () => {
        var e = Engine(out _); var t = e.AddPlan("A", new(2026,9,15))[0]; e.Start(t.Id);
        Throws<ArgumentException>(() => e.Start(Guid.NewGuid())); Equal<Guid?>(t.Id, e.RunningTaskId);
    }),
    ("跨午夜分配每日统计", () => {
        var c = new FakeClock(new DateTimeOffset(2026,9,15,23,59,50,TimeSpan.Zero));
        var e = new FocusEngine(new(), c, TimeZoneInfo.Utc); var t = e.AddPlan("A", new(2026,9,15))[0];
        e.Start(t.Id); c.Advance(30); e.Pause();
        Equal(TimeSpan.FromSeconds(10), e.DayTime(new(2026,9,15))); Equal(TimeSpan.FromSeconds(20), e.DayTime(new(2026,9,16)));
    }),
    ("系统时间回拨不改变持续时间", () => {
        var e = Engine(out var c); var t = e.AddPlan("A", new(2026,9,15))[0]; e.Start(t.Id);
        c.Advance(20); c.JumpWall(TimeSpan.FromHours(-2)); e.Tick(); c.Advance(5); e.Pause(); Equal(TimeSpan.FromSeconds(25), e.TaskTime(t.Id));
    }),
    ("昨日转入幂等且不复制已完成任务或历史时长", () => {
        var e = Engine(out var c); var t = e.AddPlan("A\nB", new(2026,9,14));
        e.Start(t[0].Id); c.Advance(9); e.Pause(); e.Complete(t[1].Id);
        Equal(1, e.CarryYesterday(new(2026,9,15))); Equal(0, e.CarryYesterday(new(2026,9,15)));
        var carried = e.Data.Tasks.Single(x => x.PlannedDate == new DateOnly(2026,9,15)); Equal(TimeSpan.Zero, e.TaskTime(carried.Id));
    }),
    ("运行中的昨日任务转入今日后仍继续原任务计时", () => {
        var e = Engine(out var c); var original = e.AddPlan("A", new(2026,9,14))[0]; e.Start(original.Id); c.Advance(9);
        Equal(1, e.CarryYesterday(new(2026,9,15))); Equal<Guid?>(original.Id, e.RunningTaskId);
        var carried = e.Data.Tasks.Single(x => x.PlannedDate == new DateOnly(2026,9,15));
        Equal<Guid?>(original.Id, carried.CarriedFromId); Equal(TimeSpan.Zero, e.TaskTime(carried.Id)); Equal(0, e.CarryYesterday(new(2026,9,15)));
        c.Advance(1); e.Pause(); Equal(TimeSpan.FromSeconds(10), e.TaskTime(original.Id));
    }),
    ("检查点重启恢复为暂停", () => WithDirectory(dir => {
        var store = new JsonStore(dir); var e = Engine(out var c); var t = e.AddPlan("A", new(2026,9,15))[0];
        e.Start(t.Id); c.Advance(5); e.Tick(); store.Save(e.Data); c.Advance(4);
        var restored = new FocusEngine(new JsonStore(dir).Load(), c); Equal<Guid?>(null, restored.RunningTaskId); Equal(TimeSpan.FromSeconds(5), restored.TaskTime(t.Id));
    })),
    ("损坏主文件恢复上一份备份并保留损坏文件", () => WithDirectory(dir => {
        var store = new JsonStore(dir); var e = Engine(out _); e.AddPlan("A", new(2026,9,15)); store.Save(e.Data);
        e.AddPlan("B", new(2026,9,15)); store.Save(e.Data); File.WriteAllText(Path.Combine(dir,"focus.json"), "broken");
        var recovered = store.Load(); Equal(1, recovered.Tasks.Count); Contains("备份", store.RecoveryNotice!);
        Equal(true, Directory.GetFiles(dir, "*.corrupt-*").Length > 0);
    })),
    ("无法恢复时不静默返回空数据", () => WithDirectory(dir => {
        File.WriteAllText(Path.Combine(dir,"focus.json"), "broken"); var error = Throws<InvalidDataException>(() => new JsonStore(dir).Load()); Contains("无法", error.Message);
    })),
    ("未来版本拒绝降级覆盖", () => WithDirectory(dir => {
        File.WriteAllText(Path.Combine(dir,"focus.json"), "{\"schemaVersion\":99}"); var error = Throws<InvalidDataException>(() => new JsonStore(dir).Load());
        Contains("版本", error.Message); Contains("99", error.Message); Equal("{\"schemaVersion\":99}", File.ReadAllText(Path.Combine(dir,"focus.json")));
    })),
    ("空任务集合拒绝保存", () => WithDirectory(dir => {
        var data = new FocusData { Tasks = null! }; var error = Throws<InvalidDataException>(() => new JsonStore(dir).Save(data)); Contains("集合", error.Message);
    })),
    ("负数计时拒绝保存", () => WithDirectory(dir => {
        var data = new FocusData(); var task = new FocusTask { Title = "A", PlannedDate = new(2026,9,15) }; data.Tasks.Add(task);
        data.Slices.Add(new TimeSlice { TaskId = task.Id, Date = new(2026,9,15), DurationTicks = -1 });
        var error = Throws<InvalidDataException>(() => new JsonStore(dir).Save(data)); Contains("计时", error.Message);
    })),
    ("冻结时钟只累计冻结边界前的时间，重复冻结不延长边界，解冻后可手动重启", () => {
        var inner = new FakeClock(new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero));
        var clock = new FreezableTimeProvider(inner);
        var engine = new FocusEngine(new(), clock, TimeZoneInfo.Utc);
        var task = engine.AddPlan("A", new(2026,9,15))[0];

        engine.Start(task.Id);
        inner.Advance(10);
        clock.Freeze();
        inner.Advance(3600);
        clock.Freeze();
        inner.Advance(600);
        engine.Pause();
        clock.Unfreeze();
        inner.Advance(100);

        Equal(TimeSpan.FromSeconds(10), engine.TaskTime(task.Id));
        Equal<Guid?>(null, engine.RunningTaskId);

        engine.Start(task.Id);
        inner.Advance(5);
        engine.Pause();
        Equal(TimeSpan.FromSeconds(15), engine.TaskTime(task.Id));
    }),
};
int failed = 0;
foreach (var (name, test) in cases) { try { test(); Console.WriteLine($"PASS {name}"); } catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); } }
Console.WriteLine($"{cases.Length-failed}/{cases.Length} passed"); return failed == 0 ? 0 : 1;

static FocusEngine Engine(out FakeClock c) { c = new(new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero)); return new(new(), c, TimeZoneInfo.Utc); }
static void Equal<T>(T want, T got) { if (!EqualityComparer<T>.Default.Equals(want, got)) throw new Exception($"expected {want}, got {got}"); }
static T Throws<T>(Action f) where T : Exception { try { f(); } catch(T error) { return error; } throw new Exception($"expected {typeof(T).Name}"); }
static void Contains(string expected, string actual) { if (!actual.Contains(expected, StringComparison.Ordinal)) throw new Exception($"expected text containing '{expected}', got '{actual}'"); }
static void WithDirectory(Action<string> f) { var p = Path.Combine(Path.GetTempPath(),"pet-focus-test-"+Guid.NewGuid()); Directory.CreateDirectory(p); try { f(p); } finally { Directory.Delete(p,true); } }
sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset utc = now;
    private long stamp;
    public override DateTimeOffset GetUtcNow() => utc;
    public override long GetTimestamp() => stamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public void Advance(int seconds) { stamp += TimeSpan.FromSeconds(seconds).Ticks; utc += TimeSpan.FromSeconds(seconds); }
    public void JumpWall(TimeSpan delta) => utc += delta;
}
