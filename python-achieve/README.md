# python-achieve —— 旧的 Python 实现（即将删除）

这里是**重写前的 Python 命令行版本**，保留只是为了对照历史行为与排查问题。

- 主程序（GUI 版）在仓库根目录，源码在 `src/`，文档见 [../README.md](../README.md) 与 [../AGENT.md](../AGENT.md)。
- 这些 `.py` 文件**不再维护**，计划在 GUI 版稳定后删除。
- 它们依赖 `../translations/zh.tsv`（GUI 版同样使用这份数据，构建时嵌入 exe）。
- 用法（需要 Python 3.10+）：

```bash
python apply.py "D:/Games/X-Plane 12"
python verify.py "D:/Games/X-Plane 12"
```

> 注意：路径、锚点、哈希基线等信息以 `../AGENT.md` 为准；Python 版与 GUI 版的行为已通过
> 「打完补丁后 sha256 = `36f5b21e…`」这一金标准验证为逐字节一致。
