# 文档索引

本目录按**读者**分类。先看你属于哪种情况：

| 你是 | 看哪份 | 什么时候看 |
|---|---|---|
| **用户**（只想给 X-Plane 12 打汉化补丁） | [user/使用说明.md](user/使用说明.md) | 拿到压缩包后；这份也会随包发布 |
| **维护者 / 接手的 AI** | [../AGENT.md](../AGENT.md) | 已验证事实、设计约束、踩过的坑（金标准 sha256 在 §1.1） |
| | [maintainer/development.md](maintainer/development.md) | 第一次上手：目录规划、研发路径、脚手架 |
| | [maintainer/plan.md](maintainer/plan.md) | 历史设计记录（跨平台 GUI 的设计由来；部分内容已与实现脱节） |
| **运维 / CI** | [ops/steam-auth.md](ops/steam-auth.md) | 要取回 Steam 上的可执行文件：认证、部署、失效处理、轮换 |
| **发版** | [release/release-notes.md](release/release-notes.md) | 打 tag 前的发布说明正文；CI 会自动追加兼容版本表 |

## 目录约定

- `user/`（使用者，会随包发布）· `maintainer/`（维护者与 AI）· `ops/`（运维与 CI）· `release/`（发版）
  —— 完整目录树见 [maintainer/development.md](maintainer/development.md) §2。
- 同一件事只写在一处，不做多处副本。
- 新增文档请在本索引登记一行。
- 文档里的相对链接必须指向**真实存在的文件**（`scripts/check/links.sh` 会检查）。
