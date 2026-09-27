// MikoBarrier 核心回归测试（tools\SmokeTest）用的「靶子进程」。
//
// 它只做一件事：安静地活够指定秒数，等着被进程巡逻 / 看门狗结束，
// 用来验证「枚举进程 -> 判定黑名单 -> 结束进程」这条链路真的生效。
//
// 用法（与 SmokeTest 启动它时的参数一致）：
//     PatrolVictim.exe -n 300 127.0.0.1
//   -n 后面的数字是存活秒数（默认 300，上限 3600），其余参数一律忽略。
//
// 这个程序不读写任何文件、不改注册表、不碰任何系统设置，只是 Sleep。

var seconds = 300;
var argv = Environment.GetCommandLineArgs();
for (var i = 1; i < argv.Length - 1; i++)
{
    if (argv[i] == "-n" && int.TryParse(argv[i + 1], out var parsed) && parsed > 0)
    {
        seconds = Math.Min(parsed, 3600);
        break;
    }
}

Console.WriteLine($"PatrolVictim: 待命中，{seconds} 秒后自行退出（等待被结束）。");
Console.Out.Flush();
Thread.Sleep(TimeSpan.FromSeconds(seconds));
return 0;
