# packaging — 分发清单

发布物（GitHub Releases 里的 `MikoBarrier-<版本>-win-x64.zip`）可以直接被 winget / scoop 引用；
两个 exe 必须放在同一目录，所以统一用 **zip** 分发，而不是散装 exe。

## winget

1. 先发 GitHub Release，拿到 zip 的 SHA256（Release 的 `SHA256SUMS.txt` 第一行）；
2. 生成清单：

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\make-winget-manifests.ps1 `
       -Tag v0.6.0-Miko -Sha256 <64 位十六进制>
   ```

   结果在 `packaging\winget\out\<版本>\`，对应 winget-pkgs 的
   `manifests\m\MikoBarrier\MikoBarrier\<版本>\`；
3. fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs)，复制 3 个 yaml 进去；
4. 本地校验（装了 winget 的 Windows）：

   ```powershell
   winget validate --manifest .\manifests\m\MikoBarrier\MikoBarrier\0.6.0\
   winget install --manifest .\manifests\m\MikoBarrier\MikoBarrier\0.6.0\
   ```

5. 提 PR：`New package: MikoBarrier.MikoBarrier version 0.6.0`。

> 注意：winget 的 `PackageVersion` 用纯数字三段（`0.6.0`），`-Miko` 后缀只出现在 tag 和 zip 文件名里。

## scoop

`scoop/mikobarrier.json` 是清单模板：

1. 新建一个 bucket 仓库（例如 `xgzshengming/scoop-bucket`），把清单放到 `bucket/mikobarrier.json`；
2. 第一次安装前，把 `"hash"` 换成 zip 的 SHA256（也可以用 `scoop checkver` / `scoop update` 自动更新）；
3. 用户安装：

   ```powershell
   scoop bucket add mikobarrier https://github.com/xgzshengming/scoop-bucket
   scoop install mikobarrier
   ```

`scoop bucket add` 的地址写在 README / Release 说明里即可，无需先合并进官方 bucket。

## Chocolatey（可选）

Chocolatey 社区源审核较慢，且需要单独的 `.nuspec` + 安装脚本；优先级低于 winget / scoop，需要时再补。

## 版本号约定

- Git tag：`v0.6.0-Miko`（大小写固定，供 scoop `checkver` 与 Release 资产名匹配）；
- zip：`MikoBarrier-0.6.0-Miko-win-x64.zip`；
- winget `PackageVersion`：`0.6.0`；
- 程序集 `InformationalVersion`：`0.6.0-Miko`（界面显示 `v0.6.0-Miko`）。
