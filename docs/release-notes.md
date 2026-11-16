X-Plane 12「飞行教学」课程标题 + 口播字幕汉化。

## 下载哪个？

| 平台 | 文件 | 说明 |
|---|---|---|
| Windows x64（Intel / AMD，推荐） | `XPlane12-FlightLessons-zh-CN-win-x64.zip` | 解压即用 |
| Windows ARM64（骁龙 X 等 ARM 电脑） | `XPlane12-FlightLessons-zh-CN-win-arm64.zip` | ⚠️ 未经实机测试（实验性） |
| Linux x64（Steam / Proton） | `XPlane12-FlightLessons-zh-CN-linux-x64.tar.gz` | `tar xzf` 后直接运行 |
| Linux ARM64（ARMv8-A / arm64-v8a） | `XPlane12-FlightLessons-zh-CN-linux-arm64.tar.gz` | ⚠️ 未经实机测试（实验性） |

> 不确定就选 x64 包：绝大多数 Windows / Linux 电脑都是 x64；ARM64 包只用于
> Windows on ARM 或 ARM64 Linux。X-Plane 12 游戏本体仍是 x64（Windows on ARM 由系统模拟运行）。

> ⚠️ **ARM64 包未经实机测试（实验性）**：作者没有 Windows on ARM / ARM64 Linux 硬件，
> `win-arm64` / `linux-arm64` 两个包没有在真实 ARM 设备上运行过，也没有对真实游戏目录
> 打过补丁；仅通过 CI 的冒烟测试与 `--selftest`（合成数据）。在 ARM 设备上使用前请先备份
> 原版，建议先用「预演（不写盘）」/ `--dry-run` 确认；遇到问题欢迎反馈。

用法：**退出游戏 → 运行 → 点「打补丁」→ 看到绿色「补丁完成」**。
详细步骤见包里的 `使用说明.md`，或仓库里的 `docs/使用说明.md`。

## 校验

下载后可用 `SHA256SUMS.txt` 核对文件完整性：
`sha256sum -c SHA256SUMS.txt`（Windows 可用 `Get-FileHash`）。

## 兼容版本

程序只对下列可执行文件版本直接打补丁；其他版本会提示「未验证版本」并要求二次确认
（GUI 需勾选确认，CLI 需加 `--force`）。完整清单见仓库 `compat/known-versions.json`。

<!-- 下面的表格由 CI 根据 compat/known-versions.json 自动生成，请勿手改 -->
