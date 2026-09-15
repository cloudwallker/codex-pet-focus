using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexPetFocus.Core;
using CodexPetFocus.Native;
using CoreTask = CodexPetFocus.Core.FocusTask;

namespace CodexPetFocus.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly JsonStore? store;
    private readonly FreezableTimeProvider clock;
    private readonly FocusEngine engine;
    private readonly DispatcherTimer timer;
    private readonly BannerWindow banner = new();
    private readonly bool customDataDirectory;
    private readonly bool smokeTest;
    private DateOnly displayedDate;
    private long lastCheckpoint = Stopwatch.GetTimestamp();
    private bool storageFailed;
    private string transientStatus = "";
    private string petStatus = "原桌宠：正在等待定位";
    private Task<PetLookupResult>? petLookup;
    private long petLookupStarted;

    public MainWindow(string dataDirectory, bool customDataDirectory, bool smokeTest)
    {
        InitializeComponent();
        this.customDataDirectory = customDataDirectory;
        this.smokeTest = smokeTest;

        FocusData data;
        if (smokeTest)
        {
            data = CreateSmokeData();
        }
        else
        {
            store = new JsonStore(dataDirectory);
            data = store.Load();
        }

        clock = new FreezableTimeProvider(TimeProvider.System);
        engine = new FocusEngine(data, clock);
        displayedDate = Today();
        DataContext = this;
        DoNotDisturbBox.IsChecked = engine.Data.Settings.DoNotDisturb;
        StartupBox.IsChecked = !customDataDirectory && StartupShortcut.IsEnabled;
        StartupBox.IsEnabled = !customDataDirectory && !smokeTest;
        if (customDataDirectory)
            transientStatus = "测试数据目录模式：开机启动设置已禁用。";
        else if (!string.IsNullOrWhiteSpace(store?.RecoveryNotice))
            transientStatus = store.RecoveryNotice!;

        RefreshRows();
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTimerTick, Dispatcher);
        timer.Start();
        Closing += OnClosing;
    }

    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];

    public string HeaderDate => displayedDate.ToDateTime(TimeOnly.MinValue).ToString("yyyy年M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN"));

    public string DailyTime => DurationFormatter.Format(engine.DayTime(displayedDate));

    public string CompletionSummary
    {
        get
        {
            var todayTasks = engine.Data.Tasks.Where(task => task.PlannedDate == displayedDate).ToArray();
            return $"{todayTasks.Count(task => task.Completed)} / {todayTasks.Length}";
        }
    }

    public Visibility EmptyVisibility => Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string StatusMessage => storageFailed
        ? transientStatus
        : string.IsNullOrWhiteSpace(transientStatus)
            ? "数据仅保存在本机；应用不会联网，也不会读取认证信息或聊天内容。"
            : transientStatus;

    public string PetStatus
    {
        get => petStatus;
        private set
        {
            if (petStatus == value)
                return;
            petStatus = value;
            OnPropertyChanged();
        }
    }

    public bool AllowClose { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void RequestSystemPause(string reason)
    {
        clock.Freeze();
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!PauseForSystem(reason))
                        ShowAndActivate();
                }
                finally
                {
                    clock.Unfreeze();
                }
            }));
        }
        catch
        {
            clock.Unfreeze();
            throw;
        }
    }

    public bool PauseForSystem(string reason)
    {
        if (engine.RunningTaskId is not null)
            engine.Pause();
        banner.HideBanner();
        var saved = SaveOrStop($"系统已{reason}，任务已暂停。");
        RefreshRowValues();
        return saved;
    }

    public bool TryPrepareExit()
    {
        try
        {
            engine.Pause();
        }
        catch (Exception error)
        {
            SetPersistentFailure("退出前暂停计时失败", error);
            return false;
        }

        if (!SaveOrStop("任务已暂停并保存，可以退出。"))
            return false;
        timer.Stop();
        banner.HideBanner();
        return true;
    }

    public void DisposeWindows()
    {
        timer.Stop();
        banner.Close();
    }

    private static FocusData CreateSmokeData()
    {
        var today = Today();
        return new FocusData
        {
            Tasks =
            [
                new CoreTask { Title = "整理今天最重要的一件事", PlannedDate = today },
                new CoreTask { Title = "完成一段专注工作", PlannedDate = today },
                new CoreTask { Title = "给自己留一点休息时间", PlannedDate = today, Completed = true }
            ]
        };
    }

    private void OnTimerTick(object? sender, EventArgs args)
    {
        var today = Today();
        if (today != displayedDate)
        {
            displayedDate = today;
            RefreshRows();
            OnPropertyChanged(nameof(HeaderDate));
        }

        if (engine.RunningTaskId is not null && !storageFailed)
        {
            try
            {
                engine.Tick();
                if (Stopwatch.GetElapsedTime(lastCheckpoint) >= TimeSpan.FromSeconds(5))
                {
                    lastCheckpoint = Stopwatch.GetTimestamp();
                    SaveOrStop("计时检查点已保存。", quietSuccess: true);
                }
            }
            catch (Exception error)
            {
                SetPersistentFailure("计时失败", error);
                TryPauseAfterFailure();
            }
        }

        RefreshRowValues();
        UpdateBanner();
    }

    private void UpdateBanner()
    {
        if (engine.RunningTaskId is not Guid runningId || storageFailed)
        {
            banner.HideBanner();
            return;
        }

        var runningTask = engine.Data.Tasks.FirstOrDefault(task => task.Id == runningId);
        if (runningTask is null)
        {
            banner.HideBanner();
            return;
        }

        if (petLookup is not null)
        {
            if (Stopwatch.GetElapsedTime(petLookupStarted) > TimeSpan.FromSeconds(2))
            {
                banner.HideBanner();
                PetStatus = "原桌宠：定位超过 2 秒，横幅已安全隐藏";
            }
            return;
        }

        petLookupStarted = Stopwatch.GetTimestamp();
        petLookup = Task.Run(() =>
        {
            var locator = new CodexPetLocator();
            var target = locator.FindPet();
            return new PetLookupResult(target, locator.LastStatus, locator.LastReason);
        });
        _ = ObservePetLookupAsync(petLookup, petLookupStarted);
    }

    private async Task ObservePetLookupAsync(Task<PetLookupResult> lookup, long started)
    {
        PetLookupResult result;
        try
        {
            result = await lookup;
        }
        catch (Exception error)
        {
            if (ReferenceEquals(petLookup, lookup))
                petLookup = null;
            banner.HideBanner();
            PetStatus = $"原桌宠：定位异常（{error.GetType().Name}）";
            return;
        }

        if (!ReferenceEquals(petLookup, lookup))
            return;
        petLookup = null;

        if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(2))
        {
            banner.HideBanner();
            PetStatus = "原桌宠：定位超过 2 秒，已丢弃过期结果";
            return;
        }

        if (engine.RunningTaskId is not Guid runningId || storageFailed)
        {
            banner.HideBanner();
            return;
        }

        if (result.Target is null)
        {
            banner.HideBanner();
            PetStatus = $"原桌宠：未定位（{result.Status} / {result.Reason}）";
            return;
        }

        var runningTask = engine.Data.Tasks.FirstOrDefault(task => task.Id == runningId);
        if (runningTask is null)
        {
            banner.HideBanner();
            return;
        }

        banner.UpdateText($"{runningTask.Title} · {DurationFormatter.Format(engine.TaskTime(runningId))}");
        banner.ShowNear(result.Target.SpriteBounds, result.Target.TopLevelHwnd);
        PetStatus = $"原桌宠：已定位 · Codex {result.Target.Version}；仅显示横幅，不控制桌宠";
    }

    private void AddPlanClick(object sender, RoutedEventArgs args)
    {
        if (!CanMutate())
            return;
        if (string.IsNullOrWhiteSpace(PlanInput.Text))
        {
            SetStatus("请先输入计划；每行会创建一个任务。");
            return;
        }

        try
        {
            var added = engine.AddPlan(PlanInput.Text, displayedDate);
            if (added.Count == 0)
            {
                SetStatus("没有可加入的任务。");
                return;
            }
            if (SaveOrStop($"已加入 {added.Count} 个今日任务。"))
            {
                PlanInput.Clear();
                RefreshRows();
            }
        }
        catch (Exception error)
        {
            SetStatus($"无法加入任务：{error.Message}");
        }
    }

    private void CarryYesterdayClick(object sender, RoutedEventArgs args)
    {
        if (!CanMutate())
            return;
        var count = engine.CarryYesterday(displayedDate);
        if (count == 0)
        {
            SetStatus("昨日没有需要转入的新任务。");
            return;
        }
        SaveOrStop($"已转入 {count} 个昨日未完成任务。");
        RefreshRows();
    }

    private void StartTaskClick(object sender, RoutedEventArgs args)
    {
        if (!CanMutate() || !TryGetId(sender, out var id))
            return;
        try
        {
            engine.Start(id);
            lastCheckpoint = Stopwatch.GetTimestamp();
            SaveOrStop("已开始专注。", quietSuccess: true);
        }
        catch (Exception error)
        {
            SetStatus($"无法启动任务：{error.Message}");
        }
        RefreshRowValues();
    }

    private void StopTaskClick(object sender, RoutedEventArgs args)
    {
        if (!CanMutate() || !TryGetId(sender, out var id) || engine.RunningTaskId != id)
            return;
        engine.Pause();
        banner.HideBanner();
        SaveOrStop("任务已暂停。");
        RefreshRowValues();
    }

    private void PauseAllClick(object sender, RoutedEventArgs args)
    {
        if (!CanMutate())
            return;
        if (engine.RunningTaskId is null)
        {
            SetStatus("当前没有正在运行的任务。");
            return;
        }
        engine.Pause();
        banner.HideBanner();
        SaveOrStop("当前任务已暂停。");
        RefreshRowValues();
    }

    private void CompleteChanged(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox checkBox || !TryGetId(sender, out var id))
            return;
        var task = engine.Data.Tasks.FirstOrDefault(candidate => candidate.Id == id);
        var requested = checkBox.IsChecked == true;
        if (task is null || task.Completed == requested)
            return;
        if (!CanMutate())
        {
            RefreshRowValues();
            return;
        }

        try
        {
            engine.Complete(id, requested);
            banner.HideBanner();
            SaveOrStop(requested ? "任务已完成。" : "已取消完成标记。");
        }
        catch (Exception error)
        {
            SetStatus($"无法修改完成状态：{error.Message}");
        }
        RefreshRowValues();
    }

    private void DoNotDisturbChanged(object sender, RoutedEventArgs args)
    {
        var requested = DoNotDisturbBox.IsChecked == true;
        if (engine.Data.Settings.DoNotDisturb == requested)
            return;
        if (!CanMutate())
        {
            DoNotDisturbBox.IsChecked = engine.Data.Settings.DoNotDisturb;
            return;
        }
        engine.Data.Settings.DoNotDisturb = requested;
        SaveOrStop(engine.Data.Settings.DoNotDisturb
            ? "勿扰偏好已开启；当前版本未启用自动动作，计时和横幅继续。"
            : "勿扰偏好已关闭；计时和横幅继续。");
    }

    private void StartupChanged(object sender, RoutedEventArgs args)
    {
        var requested = StartupBox.IsChecked == true;
        if (!customDataDirectory && !smokeTest && requested == StartupShortcut.IsEnabled)
            return;
        if (customDataDirectory || smokeTest)
        {
            StartupBox.IsChecked = false;
            SetStatus("测试数据目录模式禁止修改开机启动。");
            return;
        }

        try
        {
            StartupShortcut.SetEnabled(requested);
            StartupBox.IsChecked = StartupShortcut.IsEnabled;
            SetStatus(StartupBox.IsChecked == true ? "已开启开机启动。" : "已关闭开机启动。");
        }
        catch (Exception error)
        {
            StartupBox.IsChecked = StartupShortcut.IsEnabled;
            SetStatus($"无法修改开机启动：{error.Message}");
        }
    }

    private bool SaveOrStop(string successMessage, bool quietSuccess = false)
    {
        if (smokeTest)
        {
            if (!quietSuccess)
                SetStatus(successMessage);
            return true;
        }

        try
        {
            store!.Save(engine.Data);
            if (!quietSuccess)
                SetStatus(successMessage);
            return true;
        }
        catch (Exception error)
        {
            TryPauseAfterFailure();
            SetPersistentFailure("保存失败，计时已暂停；在修复存储问题并重新启动应用前，不会再修改数据", error);
            return false;
        }
    }

    private void TryPauseAfterFailure()
    {
        try
        {
            engine.Pause();
        }
        catch
        {
            // The persistent error below remains visible; no further timing or save is attempted.
        }
        banner.HideBanner();
    }

    private void SetPersistentFailure(string message, Exception error)
    {
        storageFailed = true;
        transientStatus = $"{message}。{error.GetType().Name}：{error.Message}";
        OnPropertyChanged(nameof(StatusMessage));
        RefreshRowValues();
    }

    private bool CanMutate()
    {
        if (!storageFailed)
            return true;
        OnPropertyChanged(nameof(StatusMessage));
        return false;
    }

    private void SetStatus(string message)
    {
        if (storageFailed)
            return;
        transientStatus = message;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void RefreshRows()
    {
        var rows = engine.Data.Tasks
            .Where(task => task.PlannedDate == displayedDate || task.Id == engine.RunningTaskId)
            .OrderBy(task => task.Completed)
            .Select(task => new TaskRowViewModel(task))
            .ToArray();

        Tasks.Clear();
        foreach (var row in rows)
            Tasks.Add(row);
        RefreshRowValues();
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    private void RefreshRowValues()
    {
        foreach (var row in Tasks)
            row.Refresh(engine, displayedDate, storageFailed);
        OnPropertyChanged(nameof(DailyTime));
        OnPropertyChanged(nameof(CompletionSummary));
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (AllowClose)
            return;
        args.Cancel = true;
        Hide();
        SetStatus("窗口已隐藏到托盘，任务计时仍会继续。");
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    private static bool TryGetId(object sender, out Guid id)
    {
        if (sender is FrameworkElement { Tag: Guid value })
        {
            id = value;
            return true;
        }
        id = Guid.Empty;
        return false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record PetLookupResult(PetTarget? Target, PetFindStatus Status, string Reason);
}

public sealed class TaskRowViewModel(CoreTask task) : INotifyPropertyChanged
{
    private bool completed;
    private bool isRunning;
    private bool canStart;
    private string elapsed = "00:00:00";
    private string status = "等待开始";

    public Guid Id => task.Id;
    public string Title => task.Title;

    public bool Completed
    {
        get => completed;
        private set => Set(ref completed, value);
    }

    public bool IsRunning
    {
        get => isRunning;
        private set => Set(ref isRunning, value);
    }

    public bool CanStart
    {
        get => canStart;
        private set => Set(ref canStart, value);
    }

    public string Elapsed
    {
        get => elapsed;
        private set => Set(ref elapsed, value);
    }

    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh(FocusEngine engine, DateOnly displayedDate, bool storageFailed)
    {
        Completed = task.Completed;
        IsRunning = engine.RunningTaskId == task.Id;
        CanStart = !task.Completed && !storageFailed && !IsRunning;
        Elapsed = DurationFormatter.Format(engine.TaskTime(task.Id));
        Status = IsRunning && task.PlannedDate != displayedDate
            ? $"跨日进行中 · {task.PlannedDate:yyyy-MM-dd}"
            : IsRunning
                ? "进行中"
                : task.Completed
                    ? "已完成"
                    : "等待开始";
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class DurationFormatter
{
    internal static string Format(TimeSpan duration)
    {
        var totalHours = (long)Math.Floor(duration.TotalHours);
        return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
