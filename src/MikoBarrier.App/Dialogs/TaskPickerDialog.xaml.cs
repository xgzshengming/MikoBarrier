using System.Windows;
using MikoBarrier.Controls;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Dialogs;

public partial class TaskPickerDialog : Window
{
    public TaskPickerDialog(IReadOnlyList<FocusTask> tasks, IEnumerable<string>? selectedIds)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowFx.ApplyRoundedCorners(this);

        TaskList.ItemsSource = tasks.ToList();

        var selected = selectedIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(selectedIds, StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            if (selected.Contains(task.Id))
            {
                TaskList.SelectedItems.Add(task);
            }
        }
    }

    public List<string> SelectedTaskIds => TaskList.SelectedItems
        .Cast<FocusTask>()
        .Select(t => t.Id)
        .ToList();

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void SelectAll_Click(object sender, RoutedEventArgs e) => TaskList.SelectAll();

    private void Clear_Click(object sender, RoutedEventArgs e) => TaskList.UnselectAll();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
