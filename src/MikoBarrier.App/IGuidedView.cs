namespace MikoBarrier;

/// <summary>
/// 页面上下文帮助能力：主窗口帮助按钮据此拿到当前页面 key，并重新演示当前页引导。
/// </summary>
public interface IGuidedView
{
    /// <summary>当前可见功能区的引导 key（HelpContent 常量）。</summary>
    string CurrentGuideKey { get; }

    /// <summary>用户在帮助中心点「重新演示」时触发。</summary>
    void ReplayCurrentGuide();
}
