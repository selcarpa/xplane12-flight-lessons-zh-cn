X-Plane 12「飞行教学」课程标题 + 口播字幕汉化。

## 下载哪个？

| 平台 | 文件 |
|---|---|
| Windows x64（Intel / AMD，推荐） | `XPlane12-FlightLessons-zh-CN-win-x64.zip` |
| Windows ARM64（骁龙 X 等 ARM 电脑） | `XPlane12-FlightLessons-zh-CN-win-arm64.zip` ⚠️ 未经实机测试 |
| Linux x64（Steam / Proton） | `XPlane12-FlightLessons-zh-CN-linux-x64.tar.gz` |
| Linux ARM64（ARMv8-A / arm64-v8a） | `XPlane12-FlightLessons-zh-CN-linux-arm64.tar.gz` ⚠️ 未经实机测试 |

> 不确定就选 x64 包；X-Plane 12 游戏本体仍是 x64（Windows on ARM 由系统模拟运行）。
> ⚠️ ARM64 包未经实机测试，详见仓库 `AGENT.md` §9。

用法：**退出游戏 → 运行 → 点「打补丁」→ 看到绿色「补丁完成」**。详细步骤见包里的 `使用说明.md`。

## 校验

下载后用 `SHA256SUMS.txt` 核对完整性：`sha256sum -c SHA256SUMS.txt`（Windows 用 `Get-FileHash`）。

## 兼容版本

程序只对下列可执行文件版本直接打补丁；其他版本会提示「未验证版本」并要求二次确认
（GUI 需勾选确认，CLI 需加 `--force`）。完整清单见仓库 `compat/known-versions.json`。

<!-- 下面的表格由 CI 根据 compat/known-versions.json 自动生成，请勿手改 -->
