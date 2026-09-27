namespace MikoBarrier.Core.Models;

/// <summary>
/// "忘记密码"用的安全问题。答案只保存 PBKDF2 哈希，且做了归一化（忽略大小写与空格）。
/// </summary>
public sealed class SecurityQuestion
{
    public string Question { get; set; } = string.Empty;

    public string AnswerHash { get; set; } = string.Empty;

    public string AnswerSalt { get; set; } = string.Empty;

    public int Iterations { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public override string ToString() => Question;
}
