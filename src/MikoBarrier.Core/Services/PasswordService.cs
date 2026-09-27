using System.Security.Cryptography;
using System.Text;
using MikoBarrier.Core.Models;

namespace MikoBarrier.Core.Services;

public sealed record PasswordCheckResult(bool IsValid, IReadOnlyList<string> Problems)
{
    public string ProblemsText => Problems.Count == 0 ? "符合要求" : string.Join("；", Problems);
}

/// <summary>
/// 密码策略：至少 16 位，必须同时包含大写字母、小写字母、数字、特殊字符。
/// 只保存 PBKDF2-SHA512 哈希，不保存明文。
/// </summary>
public static class PasswordService
{
    public const int MinLength = 16;
    public const int DefaultIterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static PasswordCheckResult Validate(string? password)
    {
        var problems = new List<string>();
        var value = password ?? string.Empty;

        if (value.Length < MinLength)
        {
            problems.Add($"长度至少 {MinLength} 位（当前 {value.Length} 位）");
        }

        if (!value.Any(char.IsUpper))
        {
            problems.Add("缺少大写字母");
        }

        if (!value.Any(char.IsLower))
        {
            problems.Add("缺少小写字母");
        }

        if (!value.Any(char.IsDigit))
        {
            problems.Add("缺少数字");
        }

        if (!value.Any(c => !char.IsLetterOrDigit(c)))
        {
            problems.Add("缺少特殊字符（如 !@#$%^&*）");
        }

        return new PasswordCheckResult(problems.Count == 0, problems);
    }

    public static void SetPassword(AppSettings settings, string password)
    {
        var (hash, salt, iterations) = Hash(password);
        settings.PasswordHash = hash;
        settings.PasswordSalt = salt;
        settings.PasswordIterations = iterations;
    }

    public static bool VerifyPassword(AppSettings settings, string? password)
    {
        if (!settings.HasPassword || string.IsNullOrEmpty(password))
        {
            return false;
        }

        return Verify(password, settings.PasswordHash, settings.PasswordSalt, settings.PasswordIterations);
    }

    // ---------------------------------------------------------------- 安全问题（忘记密码）

    /// <summary>答案归一化：去首尾空白、转小写、去掉所有空格，避免"答案明明对却验证失败"。</summary>
    public static string NormalizeAnswer(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var ch in answer.Trim().ToLowerInvariant())
        {
            if (!char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    public static void SetSecurityQuestions(AppSettings settings, IReadOnlyList<(string Question, string Answer)> entries)
    {
        settings.SecurityQuestions = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Question) && !string.IsNullOrWhiteSpace(e.Answer))
            .Select(e =>
            {
                var (hash, salt, iterations) = Hash(NormalizeAnswer(e.Answer));
                return new SecurityQuestion
                {
                    Question = e.Question.Trim(),
                    AnswerHash = hash,
                    AnswerSalt = salt,
                    Iterations = iterations,
                };
            })
            .ToList();
    }

    /// <summary>三个问题全部答对才算通过。</summary>
    public static bool VerifySecurityAnswers(AppSettings settings, IReadOnlyList<string> answers)
    {
        if (settings.SecurityQuestions.Count == 0 || answers.Count != settings.SecurityQuestions.Count)
        {
            return false;
        }

        for (var i = 0; i < answers.Count; i++)
        {
            var question = settings.SecurityQuestions[i];
            if (!Verify(NormalizeAnswer(answers[i]), question.AnswerHash, question.AnswerSalt, question.Iterations))
            {
                return false;
            }
        }

        return true;
    }

    public static string GenerateRecoveryCode(int groups = 4, int groupLength = 4)
    {
        var sb = new StringBuilder();
        for (var g = 0; g < groups; g++)
        {
            if (g > 0)
            {
                sb.Append('-');
            }

            for (var i = 0; i < groupLength; i++)
            {
                sb.Append(RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)]);
            }
        }

        return sb.ToString();
    }

    public static void SetRecoveryCode(AppSettings settings, string code)
    {
        var (hash, salt, iterations) = Hash(NormalizeCode(code));
        settings.RecoveryHash = hash;
        settings.RecoverySalt = salt;
        settings.RecoveryIterations = iterations;
    }

    public static bool VerifyRecoveryCode(AppSettings settings, string? code)
    {
        if (!settings.HasRecoveryCode || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        return Verify(NormalizeCode(code), settings.RecoveryHash, settings.RecoverySalt, settings.RecoveryIterations);
    }

    private static string NormalizeCode(string code) =>
        code.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    private static (string Hash, string Salt, int Iterations) Hash(string value)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(value),
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA512,
            HashBytes);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), DefaultIterations);
    }

    private static bool Verify(string value, string hashBase64, string saltBase64, int iterations)
    {
        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(hashBase64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(value),
                salt,
                iterations <= 0 ? DefaultIterations : iterations,
                HashAlgorithmName.SHA512,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }
}
