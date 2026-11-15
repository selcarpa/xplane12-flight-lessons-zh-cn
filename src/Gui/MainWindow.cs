using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using XpLessonZhCn.Platform;
using XpLessonZhCn.Service;

namespace XpLessonZhCn.Gui;

public sealed class MainWindow : Window
{
    private readonly TextBox _pathBox = new();
    private readonly TextBlock _versionText = new();
    private readonly TextBlock _fontText = new();
    private readonly TextBlock _statsText = new();
    private readonly TextBlock _statusText = new();
    private readonly ProgressBar _progress = new();
    private readonly TextBox _log = new();
    private readonly Button _browseButton = new();
    private readonly Button _checkButton = new();
    private readonly Button _dryRunButton = new();
    private readonly Button _applyButton = new();
    private readonly Button _restoreButton = new();
    private readonly Button _backupButton = new();

    private PatchService? _service;
    private Analysis? _analysis;
    private bool _busy;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startupGameDir)
    {
        Title = "X-Plane 12 飞行教学汉化";
        Width = 920;
        Height = 660;
        MinWidth = 780;
        MinHeight = 540;
        FontFamily = UiFonts.Main;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildUi();

        Loaded += async (_, _) => await InitializeAsync(startupGameDir);
    }

    private Control BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(18) };

        var top = new StackPanel { Spacing = 10 };

        // ── 路径行 ─────────────────────────────────────────
        var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var pathLabel = new TextBlock
        {
            Text = "游戏目录",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };

        _pathBox.Watermark = @"例如 F:\SteamLibrary\steamapps\common\X-Plane 12";
        _pathBox.FontFamily = UiFonts.Main;
        _pathBox.VerticalAlignment = VerticalAlignment.Center;

        _browseButton.Content = "浏览…";
        _browseButton.MinWidth = 84;
        _browseButton.Margin = new Thickness(8, 0, 0, 0);
        _browseButton.Click += async (_, _) => await BrowseAsync();

        Grid.SetColumn(pathLabel, 0);
        Grid.SetColumn(_pathBox, 1);
        Grid.SetColumn(_browseButton, 2);
        pathRow.Children.Add(pathLabel);
        pathRow.Children.Add(_pathBox);
        pathRow.Children.Add(_browseButton);
        top.Children.Add(pathRow);

        // ── 状态信息 ───────────────────────────────────────
        _versionText.Text = "版本：未检查";
        _versionText.TextWrapping = TextWrapping.Wrap;
        _versionText.Foreground = Brushes.DimGray;
        top.Children.Add(_versionText);

        _fontText.Text = "字体：未检查";
        _fontText.TextWrapping = TextWrapping.Wrap;
        _fontText.Foreground = Brushes.DimGray;
        top.Children.Add(_fontText);

        _statsText.Text = "译文：未加载";
        _statsText.TextWrapping = TextWrapping.Wrap;
        _statsText.Margin = new Thickness(0, 2, 0, 0);
        top.Children.Add(_statsText);

        // ── 操作按钮 ───────────────────────────────────────
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };

        _checkButton.Content = "检查状态";
        _checkButton.Click += async (_, _) => await AnalyzeAsync();

        _dryRunButton.Content = "预演（不写盘）";
        _dryRunButton.Click += async (_, _) => await DryRunAsync();

        _applyButton.Content = "打补丁";
        _applyButton.Click += async (_, _) => await ApplyAsync();

        _restoreButton.Content = "还原原版";
        _restoreButton.Click += async (_, _) => await RestoreAsync();

        _backupButton.Content = "打开备份目录";
        _backupButton.Click += (_, _) => OpenBackupDir();

        foreach (var button in new[] { _checkButton, _dryRunButton, _applyButton, _restoreButton, _backupButton })
        {
            button.MinWidth = 104;
            button.Margin = new Thickness(0, 0, 8, 8);
            button.FontFamily = UiFonts.Main;
            buttons.Children.Add(button);
        }
        top.Children.Add(buttons);

        // ── 进度 + 状态 ────────────────────────────────────
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        _progress.Height = 6;
        _progress.IsIndeterminate = true;
        _progress.IsVisible = false;
        top.Children.Add(_progress);

        _statusText.Text = "就绪";
        _statusText.Foreground = Brushes.DimGray;
        _statusText.TextWrapping = TextWrapping.Wrap;
        top.Children.Add(_statusText);

        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // ── 日志区 ─────────────────────────────────────────
        var logPanel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };

        var logHeader = new DockPanel();
        var logTitle = new TextBlock
        {
            Text = "日志",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray,
        };
        var copyButton = new Button { Content = "复制日志", MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Right };
        copyButton.FontFamily = UiFonts.Main;
        copyButton.Click += async (_, _) => await CopyLogAsync();

        DockPanel.SetDock(logTitle, Dock.Left);
        DockPanel.SetDock(copyButton, Dock.Right);
        logHeader.Children.Add(logTitle);
        logHeader.Children.Add(copyButton);
        DockPanel.SetDock(logHeader, Dock.Top);
        logPanel.Children.Add(logHeader);

        _log.IsReadOnly = true;
        _log.AcceptsReturn = true;
        _log.TextWrapping = TextWrapping.NoWrap;
        _log.FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace");
        _log.MinHeight = 220;
        logPanel.Children.Add(_log);

        root.Children.Add(logPanel);

        UpdateButtons();
        return root;
    }

    private async Task InitializeAsync(string? startupGameDir)
    {
        AppendLog($"X-Plane 12 飞行教学汉化 v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}");
        AppendLog($"运行平台：{(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "其他")} · 支持 Windows 与 Linux(Wine/Proton)");

        if (!UiFonts.CjkAvailable)
        {
            AppendLog("提示：系统没有检测到中文字体，界面可能显示为方块。");
            AppendLog("      这只影响本工具界面；游戏字幕用的是游戏自带字体。");
        }

        if (!string.IsNullOrWhiteSpace(startupGameDir) && GameLocator.IsGameDir(startupGameDir))
        {
            _pathBox.Text = startupGameDir;
            await AnalyzeAsync();
            return;
        }

        SetStatus("正在自动查找 X-Plane 12…");
        string? auto = await Task.Run(() =>
        {
            string? found = GameLocator.Find(null, null);
            if (found is not null) return found;

            var settings = Settings.Load();
            return GameLocator.IsGameDir(settings.LastGameDir) ? settings.LastGameDir : null;
        });

        if (auto is not null)
        {
            _pathBox.Text = auto;
            await AnalyzeAsync();
        }
        else
        {
            SetStatus("没有自动找到游戏目录，请点「浏览…」选择 X-Plane 12 安装目录。");
        }
    }

    private async Task BrowseAsync()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择 X-Plane 12 安装目录",
                AllowMultiple = false,
            });

            if (folders.Count == 0) return;

            string? path = folders[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            _pathBox.Text = path;
            await AnalyzeAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"选择目录失败：{ex.Message}");
        }
    }

    private async Task AnalyzeAsync()
    {
        if (_busy) return;

        string dir = (_pathBox.Text ?? "").Trim();
        if (dir.Length == 0)
        {
            SetStatus("请先选择 X-Plane 12 游戏目录。");
            return;
        }

        await RunBusyAsync("正在读取 exe 并检查…", async () =>
        {
            var result = await Task.Run(() =>
            {
                string? located = GameLocator.IsGameDir(dir) ? dir : GameLocator.Find(dir, AppendLog);
                if (located is null) return ((PatchService?)null, (Analysis?)null);

                var service = new PatchService(located) { Log = AppendLog };
                Analysis analysis = service.Analyze();
                return (service, analysis);
            });

            if (result.Item1 is null || result.Item2 is null)
            {
                _service = null;
                _analysis = null;
                SetStatus("没有在所选目录找到 X-Plane.exe，请确认路径是否指向游戏根目录。");
                UpdateButtons();
                return;
            }

            _service = result.Item1;
            _analysis = result.Item2;
            _pathBox.Text = _service.GameDir;

            RememberGameDir(_service.GameDir);
            RenderAnalysis();
        });
    }

    private void RenderAnalysis()
    {
        if (_analysis is null) return;
        var analysis = _analysis;

        if (analysis.FatalError is not null)
        {
            _versionText.Text = "版本：" + analysis.FatalError;
            _versionText.Foreground = Brushes.Firebrick;
            UpdateButtons();
            return;
        }

        string patchState = analysis.AlreadyPatched ? "已打补丁" : "未打补丁";
        string known = analysis.Known is not null
            ? "兼容清单已验证"
            : analysis.AlreadyPatched ? "兼容清单已验证" : "⚠ 不在兼容清单中（需二次确认）";

        _versionText.Text = $"版本：{analysis.VersionText} · {patchState} · {known}";
        _versionText.Foreground = analysis.NeedsSecondaryConfirmation ? Brushes.OrangeRed : Brushes.DimGray;

        _fontText.Text = analysis.Font.Ok
            ? $"字体：{analysis.Font.Detail}"
            : $"⚠ 字体：{analysis.Font.Detail} —— 补丁本身没问题，但中文可能显示为方块；" +
              "修复方法：Steam → 右键 X-Plane 12 → 属性 → 已安装文件 → 验证游戏文件完整性。";
        _fontText.Foreground = analysis.Font.Ok ? Brushes.DimGray : Brushes.OrangeRed;

        _statsText.Text = $"译文 {analysis.Entries} 条 ｜ 已应用 {analysis.Applied} ｜ 待应用 {analysis.Pending} ｜ " +
                          $"缺失 {analysis.Missing} ｜ 候选字符串 {analysis.Candidates} 条 ｜ 备份 {analysis.BackupCount} 份";
        _statsText.Foreground = analysis.Missing > 0 ? Brushes.OrangeRed : Brushes.Black;

        if (analysis.GameRunning)
        {
            SetStatus("⚠ 检测到 X-Plane 12 正在运行，请退出游戏后再打补丁。");
        }
        else if (analysis.AlreadyPatched)
        {
            SetStatus("当前 exe 已经打好补丁。如需重打，请先「还原原版」。");
        }
        else if (analysis.NeedsElevationGain)
        {
            SetStatus("⚠ 当前账户没有写入游戏目录的权限：打补丁时会先说明原因，再询问是否以管理员身份重启。");
        }
        else if (analysis.NeedsSecondaryConfirmation)
        {
            SetStatus("这个版本未经验证，打补丁前需要二次确认（弹窗中勾选确认）。");
        }
        else
        {
            SetStatus("可以打补丁。");
        }

        UpdateButtons();
    }

    /// <summary>
    /// 权限不足：先把原因和做法讲清楚，再（经用户同意后）请求系统权限。
    /// Windows 走 UAC 提权重启；Linux 只给 sudo 提示，不自动提权。
    /// </summary>
    private async Task OfferElevationAsync(Analysis analysis, string reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            string hint = Elevation.ManualHint(analysis.GameDir, Environment.ProcessPath);
            AppendLog("没有写入权限。手动提权方式：");
            AppendLog("  " + hint.Replace("\n", "\n  "));

            await ConfirmWindow.ShowInfoAsync(this, "需要写入权限",
                reason + "\n\n" + hint + "\n\n（本工具不会自动提权，也不会修改系统权限设置）");
            return;
        }

        if (Elevation.IsElevated)
        {
            await ConfirmWindow.ShowInfoAsync(this, "仍然无法写入",
                reason + "\n\n当前已经是管理员权限，但仍然写不进去。可能原因：\n" +
                "• 文件被占用（X-Plane 或安全软件正在使用该文件）\n" +
                "• 磁盘只读 / 文件夹被安全软件保护\n\n" +
                "建议：Steam → 右键 X-Plane 12 → 属性 → 已安装文件 → 验证游戏文件完整性，然后再试。");
            return;
        }

        string message =
            reason + "\n\n" +
            $"游戏目录：\n{analysis.GameDir}\n\n" +
            "解决办法：以管理员身份重新启动本工具。\n" +
            "点「以管理员身份重启」后，Windows 会弹出 UAC 授权窗口（需要你点「是」）；\n" +
            "授权后程序会自动带着当前游戏目录重开，届时再点一次「打补丁」即可。\n\n" +
            "是否现在请求管理员权限并重启？";

        bool ok = await ConfirmWindow.ShowAsync(
            this, "需要管理员权限", message,
            checkText: null,
            okText: "以管理员身份重启",
            cancelText: "取消");

        if (!ok) return;

        if (Elevation.TryRestartAsAdministrator(analysis.GameDir, out string error))
        {
            AppendLog("已请求管理员权限，正在以管理员身份重新启动…");
            Close();
            return;
        }

        await ConfirmWindow.ShowInfoAsync(this, "无法以管理员身份启动",
            error + "\n\n可以手动操作：右键本程序 → 「以管理员身份运行」。");
    }

    private async Task DryRunAsync()
    {
        if (_service is null) { await AnalyzeAsync(); if (_service is null) return; }

        await RunBusyAsync("正在预演（不写盘）…", async () =>
        {
            var result = await Task.Run(() =>
            {
                var engine = _service.DryRun(out string reportPath);
                return (engine, reportPath);
            });

            if (result.engine is null)
            {
                SetStatus("预演失败，详见日志。");
                return;
            }

            AppendLog($"预演完成：可写入 {result.engine.Patched} 条，跳过 {result.engine.Skipped} 条，" +
                      $"放不下 {result.engine.TooLong} 条，未找到 {result.engine.NotFound} 条。");
            if (!string.IsNullOrEmpty(result.reportPath)) AppendLog($"报告：{result.reportPath}");
            SetStatus("预演完成，未写入任何文件。");
        });
    }

    private async Task ApplyAsync()
    {
        if (_service is null)
        {
            await AnalyzeAsync();
            if (_service is null) return;
        }

        var analysis = _analysis;
        if (analysis is null) return;

        if (analysis.GameRunning)
        {
            await ConfirmWindow.ShowInfoAsync(this, "游戏正在运行",
                "请先完全退出 X-Plane 12 再打补丁。\n\n" + analysis.GameRunningDetail);
            return;
        }

        if (analysis.AlreadyPatched)
        {
            await ConfirmWindow.ShowInfoAsync(this, "已经是补丁后的状态",
                "当前 exe 已经打好补丁。如需重新打，请先点「还原原版」。");
            return;
        }

        // 权限不足时先解释、再请求提权，不要闷头失败
        if (analysis.NeedsElevationGain)
        {
            await OfferElevationAsync(analysis, "打补丁需要替换游戏目录里的 X-Plane.exe，但当前账户对该目录没有写权限。");
            return;
        }

        bool needsConfirm = analysis.NeedsSecondaryConfirmation;
        string message = needsConfirm
            ? "当前 exe 的版本未在兼容清单中：\n" +
              $"sha256 = {analysis.Sha256}\n\n" +
              "继续打补丁可能让游戏出现异常（锚点可能失效、补丁位置可能不准）。\n" +
              "程序会先自动备份原版；如果校验不通过会自动回滚。\n\n是否仍要继续？"
            : $"将对这个文件打补丁：\n{analysis.ExePath}\n\n" +
              $"版本：{analysis.VersionText}\n" +
              $"写入 {analysis.Pending} 条译文（共 {analysis.Entries} 条）\n\n" +
              "会先自动备份原版 exe；文件大小不会改变；完成后会自动校验。";

        bool ok = await ConfirmWindow.ShowAsync(
            this,
            needsConfirm ? "⚠ 未验证版本：需要二次确认" : "确认打补丁",
            message,
            needsConfirm ? "我已了解风险，仍要继续" : null);

        if (!ok) return;

        await RunBusyAsync("正在打补丁…", async () =>
        {
            var outcome = await Task.Run(() => _service.Apply(allowUnknownVersion: needsConfirm));
            AppendLog(outcome.Message);

            if (outcome.Engine is not null)
            {
                AppendLog($"统计：写入 {outcome.Engine.Patched} 条，跳过 {outcome.Engine.Skipped} 条，" +
                          $"放不下 {outcome.Engine.TooLong} 条，未找到 {outcome.Engine.NotFound} 条。");
            }

            switch (outcome.Status)
            {
                case ApplyStatus.Applied:
                    _service.StoreReport(outcome.Engine!);
                    SetStatus("补丁完成，正在校验…");
                    var verify = await Task.Run(() => _service.Verify());
                    AppendLog(verify.Message);
                    if (verify.Ok)
                    {
                        SetStatus($"补丁完成并通过校验（{outcome.Engine!.Patched} 条）。可以启动游戏了。");
                        await ConfirmWindow.ShowInfoAsync(this, "补丁完成",
                            $"已写入 {outcome.Engine!.Patched} 条译文并通过校验。\n\n" +
                            $"新 sha256：\n{outcome.PatchedSha256}\n\n" +
                            "现在可以启动 X-Plane 12，进入「飞行教学」查看中文标题与字幕。");
                    }
                    else
                    {
                        SetStatus("⚠ 补丁已写入但校验未通过，建议点「还原原版」。");
                        await ConfirmWindow.ShowInfoAsync(this, "校验未通过", verify.Message + "\n\n建议先还原原版，再联系维护者。");
                    }
                    break;

                case ApplyStatus.NeedsSecondaryConfirmation:
                    SetStatus("需要二次确认后才能继续。");
                    break;

                case ApplyStatus.NeedsPermission:
                    SetStatus("⚠ 没有写入权限。");
                    await OfferElevationAsync(analysis, outcome.Message);
                    break;

                case ApplyStatus.AlreadyPatched:
                    SetStatus("当前 exe 已经是补丁后的状态。");
                    break;

                case ApplyStatus.Failed:
                    SetStatus("⚠ 打补丁失败：" + outcome.Message);
                    await ConfirmWindow.ShowInfoAsync(this, "打补丁失败", outcome.Message);
                    break;

                default:
                    SetStatus("没有需要写入的内容。");
                    break;
            }

            var fresh = await Task.Run(() => _service.Analyze());
            _analysis = fresh;
            RenderAnalysis();
        });
    }

    private async Task RestoreAsync()
    {
        if (_service is null) { await AnalyzeAsync(); if (_service is null) return; }

        var backups = await Task.Run(() => _service.ListBackups());
        if (backups.Count == 0)
        {
            await ConfirmWindow.ShowInfoAsync(this, "没有备份",
                "没有找到原版 exe 的备份。\n\n可以用 Steam「验证游戏文件完整性」恢复原版 exe。");
            return;
        }

        var latest = backups[0];
        bool ok = await ConfirmWindow.ShowAsync(this, "确认还原原版",
            $"将用备份还原 X-Plane.exe：\n{latest.Name}\n（{latest.Size:N0} 字节，{latest.Modified:yyyy-MM-dd HH:mm}）\n\n还原后所有汉化会消失。");

        if (!ok) return;

        await RunBusyAsync("正在还原原版…", async () =>
        {
            var outcome = await Task.Run(() => _service.Restore(latest.Name));
            AppendLog(outcome.Message);

            if (!outcome.Ok && outcome.Message.Contains("权限", StringComparison.Ordinal))
            {
                SetStatus("⚠ 没有写入权限，无法还原。");
                if (_analysis is not null)
                {
                    await OfferElevationAsync(_analysis, outcome.Message);
                }
            }
            else
            {
                SetStatus(outcome.Ok ? "已还原为原版。" : "⚠ 还原失败：" + outcome.Message);
                if (!outcome.Ok) await ConfirmWindow.ShowInfoAsync(this, "还原失败", outcome.Message);
            }

            var fresh = await Task.Run(() => _service.Analyze());
            _analysis = fresh;
            RenderAnalysis();
        });
    }

    private void OpenBackupDir()
    {
        if (_service is null) return;

        string dir = Path.Combine(_service.WorkDir, "backup");
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{dir}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            AppendLog($"打开目录失败：{ex.Message}");
        }
    }

    private async Task CopyLogAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(_log.Text ?? "");
                SetStatus("日志已复制到剪贴板。");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"复制失败：{ex.Message}");
        }
    }

    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        if (_busy) return;

        _busy = true;
        _progress.IsVisible = true;
        SetStatus(status);
        UpdateButtons();

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppendLog($"出错：{ex.Message}");
            SetStatus("操作失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
            _progress.IsVisible = false;
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        var analysis = _analysis;
        bool ready = analysis is not null && analysis.ExeExists && !_busy;

        _browseButton.IsEnabled = !_busy;
        _checkButton.IsEnabled = !_busy;
        _dryRunButton.IsEnabled = ready && analysis!.RegionOk;
        _applyButton.IsEnabled = ready && analysis!.CanPatch;
        _restoreButton.IsEnabled = ready;
        _backupButton.IsEnabled = _service is not null && !_busy;
    }

    private void SetStatus(string text)
    {
        _statusText.Text = text;
    }

    private void AppendLog(string line)
    {
        void Append()
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            _log.Text = (_log.Text ?? "") + $"[{stamp}] {line}" + Environment.NewLine;
            _log.CaretIndex = (_log.Text ?? "").Length;
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Append();
        else Avalonia.Threading.Dispatcher.UIThread.Post(Append);
    }

    private static void RememberGameDir(string dir)
    {
        var settings = Settings.Load();
        settings.LastGameDir = dir;
        settings.Save();
    }
}
