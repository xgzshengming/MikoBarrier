using System.Windows;
using System.Windows.Controls;
using MikoBarrier.Core.Models;
using MikoBarrier.Core.Services;

namespace MikoBarrier.Views;

public partial class StatsView : UserControl, IGuidedView
{
    private enum StatsPage
    {
        Hub,
        History,
        TaskStats,
    }

    private StatsPage _page = StatsPage.Hub;

    public StatsView()
    {
        InitializeComponent();
        ShowStatsPage(StatsPage.Hub);
        Loaded += (_, _) =>
        {
            Refresh();
            if (_page == StatsPage.Hub)
            {
                GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.Stats);
            }
        };
    }

    private Window? HostWindow => Window.GetWindow(this);

    public string CurrentGuideKey => _page switch
    {
        StatsPage.History => HelpContent.StatsHistory,
        StatsPage.TaskStats => HelpContent.StatsTaskAwards,
        _ => HelpContent.Stats,
    };

    public void ReplayCurrentGuide() =>
        GuideHooks.ShowOnFirstVisit(HostWindow, CurrentGuideKey, force: true);

    private void HubHistory_Click(object sender, RoutedEventArgs e) => ShowStatsPage(StatsPage.History);

    private void HubTaskStats_Click(object sender, RoutedEventArgs e) => ShowStatsPage(StatsPage.TaskStats);

    private void BackToStatsHub_Click(object sender, RoutedEventArgs e) => ShowStatsPage(StatsPage.Hub);

    private void ShowStatsPage(StatsPage page)
    {
        _page = page;
        var hubVisible = page == StatsPage.Hub;
        StatsHubPanel.Visibility = hubVisible ? Visibility.Visible : Visibility.Collapsed;
        HistoryPagePanel.Visibility = page == StatsPage.History ? Visibility.Visible : Visibility.Collapsed;
        TaskStatsPagePanel.Visibility = page == StatsPage.TaskStats ? Visibility.Visible : Visibility.Collapsed;

        if (page == StatsPage.History)
        {
            Refresh();
            GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.StatsHistory);
        }
        else if (page == StatsPage.TaskStats)
        {
            Refresh();
            GuideHooks.ShowOnFirstVisit(HostWindow, HelpContent.StatsTaskAwards);
        }
    }

    public void Refresh()
    {
        var records = App.State.Records;

        var today = records.Where(r => r.StartedUtc.ToLocalTime().Date == DateTime.Today).ToList();
        TodayCountText.Text = $"{today.Count(r => r.Completed)} 次";
        TotalCountText.Text = $"{records.Count(r => r.Completed)} 次";

        var total = TimeSpan.FromSeconds(records.Sum(r => r.ActualFocusSeconds));
        TotalTimeText.Text = $"{(int)total.TotalHours} 小时 {total.Minutes} 分钟";

        RecordsList.ItemsSource = records
            .OrderByDescending(r => r.StartedUtc)
            .Take(200)
            .Select(r => r.ToDisplayString())
            .ToList();

        var localToday = DateTime.Today;
        var weekStart = localToday.AddDays(-(((int)localToday.DayOfWeek + 6) % 7));
        var monthStart = new DateTime(localToday.Year, localToday.Month, 1);
        WeekSummaryText.Text = MikoText.T(SummarizePeriod(records, weekStart, localToday.AddDays(1)));
        MonthSummaryText.Text = MikoText.T(SummarizePeriod(records, monthStart, localToday.AddDays(1)));

        var tasks = TaskStore.Load()
            .OrderBy(t => t.State)
            .ThenBy(t => t.CreatedUtc)
            .ToList();

        TaskStatsList.ItemsSource = tasks
            .Select(t => $"{t.Title}   累计 {t.CompletedRounds}/{t.PlannedRounds} 回合   {FormatDuration(t.TotalFocusSeconds)}   {StateText(t.State)}")
            .ToList();

        TaskStatsHint.Text = MikoText.T(tasks.Count == 0
            ? "还没有任务，可到「任务清单」页添加。"
            : $"共 {tasks.Count} 个任务，累计自律 {FormatDuration(tasks.Sum(t => t.TotalFocusSeconds))}");

        HintText.Text = MikoText.T($"共 {records.Count} 条记录，今日中断 {today.Count(r => !r.Completed)} 次");
    }

    private static string SummarizePeriod(List<SessionRecord> records, DateTime startLocal, DateTime endLocalExclusive)
    {
        var scoped = records
            .Where(r =>
            {
                var local = r.StartedUtc.ToLocalTime();
                return local >= startLocal && local < endLocalExclusive;
            })
            .ToList();

        var completed = scoped.Count(r => r.Completed);
        return $"完成 {completed} 次，自律 {FormatDuration(scoped.Sum(r => Math.Max(0, r.ActualFocusSeconds)))}，中断 {scoped.Count - completed} 次";
    }

    private static string FormatDuration(int seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalSeconds <= 0)
        {
            return "0 分钟";
        }

        if (span.TotalMinutes < 1)
        {
            return "不足 1 分钟";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)Math.Round(span.TotalMinutes)} 分钟";
        }

        return $"{(int)span.TotalHours} 小时 {span.Minutes} 分钟";
    }

    private static string StateText(FocusTaskState state) => state switch
    {
        FocusTaskState.Done => "已完成",
        FocusTaskState.Active => "进行中",
        _ => "待开始",
    };

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();
}
