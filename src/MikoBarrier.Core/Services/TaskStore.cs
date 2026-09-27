using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

/// <summary>待办任务的持久化（data/tasks.json）。</summary>
public static class TaskStore
{
    public static List<FocusTask> Load() =>
        JsonStore.Load(StoragePaths.TasksFile, static () => new List<FocusTask>());

    public static void Save(List<FocusTask> tasks) => JsonStore.Save(StoragePaths.TasksFile, tasks);

    public static FocusTask Add(string title, int plannedRounds)
    {
        var tasks = Load();
        var task = new FocusTask { Title = title.Trim(), PlannedRounds = Math.Max(1, plannedRounds) };
        tasks.Add(task);
        Save(tasks);
        return task;
    }

    public static void Remove(string id)
    {
        var tasks = Load();
        tasks.RemoveAll(t => t.Id == id);
        Save(tasks);
    }

    public static void Update(string id, string title, int plannedRounds, FocusTaskState state)
    {
        var tasks = Load();
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task is null)
        {
            return;
        }

        task.Title = title.Trim();
        task.PlannedRounds = Math.Max(1, plannedRounds);
        task.State = state;
        Save(tasks);
    }
    /// <summary>自律结束时把成绩记到任务上。</summary>
    public static void ApplySession(string? taskId, int roundsCompleted, int focusSeconds)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        var tasks = Load();
        var task = tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null)
        {
            return;
        }

        task.CompletedRounds += Math.Max(0, roundsCompleted);
        task.TotalFocusSeconds += Math.Max(0, focusSeconds);
        if (task.State == FocusTaskState.Pending)
        {
            task.State = FocusTaskState.Active;
        }

        if (task.PlannedRounds > 0 && task.CompletedRounds >= task.PlannedRounds)
        {
            task.State = FocusTaskState.Done;
        }

        Save(tasks);
    }
}