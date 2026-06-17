using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace V2rayNMonitor;

public sealed class MainWindow : Window
{
    private readonly MonitorSettings _settings = Paths.LoadSettings();
    private readonly MonitorEngine _engine;
    private readonly ObservableCollection<NodeScore> _rows = [];
    private readonly Border _stable = new(), _fast = new(), _best = new();
    private readonly TextBlock _status = new();
    private readonly Border _stateBadge = new();
    private readonly TextBlock _stateText = new();
    private readonly Border _scopeBadge = new();
    private readonly TextBlock _scopeText = new();
    private readonly Dictionary<DataGridColumn, SortColumnInfo> _sortColumns = [];
    private readonly NetworkIdleMonitor _networkIdle = new();
    private readonly WrapPanel _subscriptions = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly WrapPanel _clients = new() { Margin = new Thickness(0, 0, 0, 8) };
    private List<NodeScore> _allScores = [];
    private string? _selectedSubscription;
    private string? _sortProperty;
    private ListSortDirection _sortDirection;
    private double _subscriptionViewportWidth = 760;
    private int _visibleSubscriptionCards;
    private bool _showUnavailableSubscriptions;
    private readonly Forms.NotifyIcon? _tray;
    private readonly System.Drawing.Icon? _trayIcon;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DateTime _started = DateTime.Now;
    private bool _refreshPending;

    private readonly bool _preview;

    public MainWindow(bool preview = false, bool startHidden = false)
    {
        _preview = preview;
        Title = "多客户端节点监控";
        Width = 1260; Height = 900; MinWidth = 980; MinHeight = 760;
        Background = UiColors.Window;
        Icon = LoadWindowIcon();
        _settings.V2rayNPath = File.Exists(_settings.V2rayNPath) ? _settings.V2rayNPath : Paths.LocateV2rayN() ?? "";
        if (!preview) { Paths.SaveSettings(_settings); StartupManager.Apply(_settings.StartWithWindows); }
        _engine = new(_settings);
        _engine.Status += text => Dispatcher.Invoke(() => SetStatus(text));
        _engine.Updated += () => Dispatcher.Invoke(RequestRefresh);
        Content = Build();
        Closing += (_, e) => { if (!_preview) { e.Cancel = true; Hide(); } };

        if (preview) { Refresh(); return; }
        _trayIcon = LoadTrayIcon();
        _tray = new Forms.NotifyIcon { Visible = true, Text = "多客户端节点监控", Icon = _trayIcon };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDashboard);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => Dispatcher.Invoke(ShowDashboard));
        menu.Items.Add("立即测延迟", null, async (_, _) => await _engine.RunAsync("delay"));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() => { _tray.Dispose(); _trayIcon?.Dispose(); System.Windows.Application.Current.Shutdown(); }));
        _tray.ContextMenuStrip = menu;
        Loaded += (_, _) => { Refresh(); if (startHidden) Hide(); };
        _timer.Tick += async (_, _) => await Schedule();
        _timer.Start();
    }

    private static ImageSource? LoadWindowIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ProxyMonitor.ico");
        return File.Exists(path) ? BitmapFrame.Create(new Uri(path, UriKind.Absolute)) : null;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ProxyMonitor.ico");
        return File.Exists(path) ? new System.Drawing.Icon(path) : System.Drawing.SystemIcons.Application;
    }

    private UIElement Build()
    {
        var root = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var title = new StackPanel();
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
        titleLine.Children.Add(new TextBlock { Text = "节点健康看板", FontSize = 21, FontWeight = FontWeights.Bold, Foreground = UiColors.Text });
        _stateText.Text = "监控中";
        _stateText.Foreground = UiColors.Text;
        _stateText.FontSize = 12;
        _stateBadge.Child = _stateText;
        _stateBadge.Background = UiColors.Good;
        _stateBadge.CornerRadius = new CornerRadius(8);
        _stateBadge.Padding = new Thickness(8, 3, 8, 3);
        _stateBadge.Margin = new Thickness(10, 5, 0, 0);
        _stateBadge.VerticalAlignment = VerticalAlignment.Center;
        titleLine.Children.Add(_stateBadge);
        _scopeText.Foreground = UiColors.Text;
        _scopeText.FontSize = 12;
        _scopeBadge.Child = _scopeText;
        _scopeBadge.Background = UiColors.Empty;
        _scopeBadge.CornerRadius = new CornerRadius(8);
        _scopeBadge.Padding = new Thickness(8, 3, 8, 3);
        _scopeBadge.Margin = new Thickness(6, 5, 0, 0);
        _scopeBadge.VerticalAlignment = VerticalAlignment.Center;
        titleLine.Children.Add(_scopeBadge);
        title.Children.Add(titleLine);
        title.Children.Add(new TextBlock { Text = "颜色越绿表现越好，越红越需要避开；灰色表示数据不足。", Foreground = UiColors.Muted, Margin = new Thickness(0, 1, 0, 0) });
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        actions.Children.Add(ActionButton("立即测延迟", async () => await _engine.RunAsync("delay"), true));
        actions.Children.Add(ActionButton("立即轻量测速", async () => await _engine.RunAsync("speed")));
        actions.Children.Add(ActionButton("停止当前测试", () => { _engine.Cancel(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton(_settings.Paused ? "恢复监控" : "暂停监控", () => { _settings.Paused = !_settings.Paused; Paths.SaveSettings(_settings); Refresh(); return Task.CompletedTask; }));
        actions.Children.Add(ActionButton("监控设置", OpenSettings));
        actions.Children.Add(ActionButton("设置 v2rayN 路径", PickPath));
        DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);

        var cards = new UniformGrid { Rows = 1, Columns = 3, Height = 132, Margin = new Thickness(0, 0, 0, 6) };
        cards.Children.Add(RecommendationShell("最稳定", "延迟成功率、抖动与 P95 延迟", _stable));
        cards.Children.Add(RecommendationShell("速度最快", "轻量测速中位数", _fast));
        cards.Children.Add(RecommendationShell("综合最佳", "稳定 45% · 速度 30% · 延迟 25%", _best));
        DockPanel.SetDock(cards, Dock.Top); root.Children.Add(cards);

        var overview = new Grid { Height = 240, Margin = new Thickness(0, 0, 0, 6) };
        overview.ColumnDefinitions.Add(new() { Width = new GridLength(0.32, GridUnitType.Star) });
        overview.ColumnDefinitions.Add(new() { Width = new GridLength(0.68, GridUnitType.Star) });

        var clientBox = new GroupBox
        {
            Header = "自动发现的客户端与能力",
            Content = _clients,
            Padding = new Thickness(8), Margin = new Thickness(0, 0, 6, 0), BorderBrush = UiColors.Border
        };
        overview.Children.Add(clientBox);

        var subscriptionBox = new GroupBox
        {
            Header = "客户端与订阅概览（点击筛选）",
            Content = new ScrollViewer
            {
                Content = _subscriptions,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            },
            Padding = new Thickness(8), Margin = new Thickness(6, 0, 0, 0), BorderBrush = UiColors.Border
        };
        subscriptionBox.SizeChanged += (_, e) =>
        {
            _subscriptionViewportWidth = Math.Max(260, e.NewSize.Width - 34);
            UpdateSubscriptionCardWidths();
        };
        Grid.SetColumn(subscriptionBox, 1); overview.Children.Add(subscriptionBox);
        DockPanel.SetDock(overview, Dock.Top); root.Children.Add(overview);

        _status.Margin = new Thickness(5, 0, 0, 4); _status.Text = "就绪"; _status.Foreground = UiColors.Muted;
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);

        var grid = new DataGrid
        {
            ItemsSource = _rows, IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = UiColors.Border,
            RowBackground = Brushes.White, AlternatingRowBackground = UiColors.Alternate, AlternationCount = 2,
            BorderBrush = UiColors.Border, RowHeight = 28, HeadersVisibility = DataGridHeadersVisibility.Column
        };
        AddColumn(grid, TextColumn("客户端", "ClientName", 115), "客户端", "ClientName", false, "节点来自哪个代理客户端。");
        AddColumn(grid, TextColumn("节点", "Name", 225), "节点", "Name", false, "节点名称。双击节点会先复测，再确认切换。");
        AddColumn(grid, TextColumn("订阅", "Subscription", 165), "订阅", "Subscription", false, "节点所属订阅组。点击上方订阅卡片可按组筛选推荐。");
        AddColumn(grid, MetricColumn("延迟成功率", "SuccessText", "SuccessRate", MetricKind.Success, 82), "延迟成功率", "SuccessRate", true, "最近历史窗口内真实代理延迟测试的平均成功率。节点经常偶发连不上时，这里会下降。正式推荐通常要求整体和近期都达到 90%。");
        AddColumn(grid, MetricColumn("中位延迟", "DelayText", "MedianDelay", MetricKind.Delay, 88), "中位延迟", "MedianDelay", false, "最近历史窗口内成功延迟样本的中位数。越低越好；排序时无数据节点永远排最后。");
        AddColumn(grid, MetricColumn("中位速度", "SpeedText", "MedianSpeed", MetricKind.Speed, 105), "中位速度", "MedianSpeed", true, "轻量测速的中位数。它只做相对参考，不会长时间跑满带宽；无有效测速节点排序时排最后。");
        AddColumn(grid, MetricColumn("稳定分", "StabilityText", "StabilityScore", MetricKind.Score, 76), "稳定分", "StabilityScore", true, "稳定分主要惩罚连不上和波动：40% 总成功率、30% 近期成功率、15% 抖动、15% 高位延迟。偶发掉线会明显拉低近期成功率和连续失败惩罚。");
        AddColumn(grid, MetricColumn("综合分", "CombinedText", "CombinedScore", MetricKind.Score, 76), "综合分", "CombinedScore", true, "综合分默认按稳定 45%、速度 30%、延迟 25% 加权，并乘以样本置信度和近期异常惩罚。它适合做最终推荐，不是单纯选最快。");
        AddColumn(grid, TextColumn("样本", "SampleText", 112), "样本", "Samples", true, "当前历史窗口内的延迟样本数和有效速度样本数。样本越多，推荐越可信。");
        AddColumn(grid, MetricColumn("状态", "Status", "Confidence", MetricKind.Confidence, 140), "状态", "Confidence", true, "说明当前节点为什么还不能正式推荐，例如还差几次延迟、还差几次有效测速、成功率未达标或近期连续失败。");
        grid.Sorting += SortGrid;
        grid.MouseDoubleClick += async (_, _) => { if (grid.SelectedItem is NodeScore score) await Switch(score); };
        root.Children.Add(grid);
        return root;
    }

    private void Refresh()
    {
        _refreshPending = false;
        try
        {
            _allScores = _preview ? PreviewData.Scores() : _engine.Store.Scores(_engine.Nodes(), _settings);
            if (_selectedSubscription is not null && !_allScores.Any(x => SubscriptionKey(x) == _selectedSubscription))
                _selectedSubscription = null;
            RefreshClients(); RefreshSubscriptions(); ApplySubscriptionFilter();
            RefreshRecommendations();
            UpdateScopeBadge();
            var clients = _allScores.Select(x => x.ClientName).Distinct().Count();
            var v2 = File.Exists(_settings.V2rayNPath) ? V2rayNReader.Inspect(_settings.V2rayNPath) : null;
            var capability = v2 is null ? "" : $" · v2rayN {v2.Version}: 读取{Yes(v2.CanRead)}/测试{Yes(v2.CanTest)}/切换{Yes(v2.CanSwitch)}";
            SetStatus($"{(_settings.Paused ? "已暂停" : "监控中")} · {clients} 个客户端 · 显示 {_rows.Count}/{_allScores.Count} 个节点{capability} · 双击节点可确认切换");
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void RememberRecommendations(NodeScore? stable, NodeScore? fast, NodeScore? best)
    {
        if (_preview) return;
        var changed = _settings.StableRecommendationNodeId != stable?.NodeId
            || _settings.FastRecommendationNodeId != fast?.NodeId
            || _settings.BestRecommendationNodeId != best?.NodeId;
        if (!changed) return;
        _settings.StableRecommendationNodeId = stable?.NodeId;
        _settings.FastRecommendationNodeId = fast?.NodeId;
        _settings.BestRecommendationNodeId = best?.NodeId;
        Paths.SaveSettings(_settings);
    }

    private void RefreshRecommendations()
    {
        var scope = ActiveScores();
        var stableReady = scope.Where(x => x.StableEnough).ToList();
        var speedReady = scope.Where(x => x.SpeedEnough).ToList();
        var provisionalReady = stableReady.Count > 0 ? stableReady : scope.Where(x => x.Samples > 0).ToList();
        var hint = RecommendationHint(scope);

        NodeScore? stable;
        NodeScore? fast;
        NodeScore? best;
        if (_selectedSubscription is null)
        {
            stable = RecommendationSelector.Select(_settings.StableRecommendationNodeId, stableReady, x => x.StabilityScore, 3);
            fast = RecommendationSelector.SelectByRatio(_settings.FastRecommendationNodeId, speedReady, x => x.MedianSpeed, .10);
            var bestCandidates = speedReady.Count > 0 ? speedReady : provisionalReady;
            best = RecommendationSelector.Select(_settings.BestRecommendationNodeId, bestCandidates, x => x.CombinedScore, 4);
            RememberRecommendations(stable, fast, best);
        }
        else
        {
            stable = stableReady.OrderByDescending(x => x.StabilityScore).FirstOrDefault();
            fast = speedReady.OrderByDescending(x => x.MedianSpeed).FirstOrDefault();
            best = (speedReady.Count > 0 ? speedReady : provisionalReady).OrderByDescending(x => x.CombinedScore).FirstOrDefault();
        }

        SetCard(_stable, stable, "稳定分", x => x.StabilityScore, StableHint(scope));
        SetCard(_fast, fast, "中位速度", x => x.SpeedScore, hint, x => $"中位速度 {x.SpeedText}");
        SetCard(_best, best, speedReady.Count > 0 ? "综合分" : "临时综合分", x => x.CombinedScore, hint);
    }

    private void RequestRefresh()
    {
        if (IsVisible) Refresh();
        else _refreshPending = true;
    }

    private void ShowDashboard()
    {
        Show();
        Activate();
        if (_refreshPending) Refresh();
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        if (_settings.Paused)
        {
            SetBadge("已暂停", UiColors.Empty);
            return;
        }
        if (text.Contains("轻量速度测试", StringComparison.Ordinal) || text.Contains("速度", StringComparison.Ordinal) && text.StartsWith("正在", StringComparison.Ordinal))
        {
            SetBadge("测速中", UiColors.Fair);
            return;
        }
        if (text.Contains("测试真实延迟", StringComparison.Ordinal) || text.Contains("延迟", StringComparison.Ordinal) && text.StartsWith("正在", StringComparison.Ordinal))
        {
            SetBadge("延迟测试中", UiColors.Fair);
            return;
        }
        if (text.StartsWith("测试失败", StringComparison.Ordinal))
        {
            SetBadge("异常", UiColors.Bad);
            return;
        }
        SetBadge("监控中", UiColors.Good);
    }

    private void SetBadge(string text, Brush background)
    {
        _stateText.Text = text;
        _stateBadge.Background = background;
        _stateBadge.ToolTip = _status.Text;
    }

    private void RefreshClients()
    {
        _clients.Children.Clear();
        foreach (var adapter in _engine.Adapters.Where(x => x.IsDetected))
        {
            var c = adapter.Capability;
            var level = c.CanDelay ? 1d : c.CanRead ? .65 : .2;
            _clients.Children.Add(new Border
            {
                Background = UiColors.ForRatio(level), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 5, 5), Width = 190,
                Child = new TextBlock
                {
                    Text = $"{c.Name}\n读取 {Mark(c.CanRead)} · 延迟 {Mark(c.CanDelay)} · 速度 {Mark(c.CanSpeed)} · 切换 {Mark(c.CanSwitch)}\n{c.Integration}",
                    Foreground = UiColors.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 15
                }
            });
        }
    }

    private void RefreshSubscriptions()
    {
        _subscriptions.Children.Clear();
        var groups = _allScores.GroupBy(SubscriptionKey).OrderBy(x => x.Key)
            .Select(x => new SubscriptionGroup(x.Key, x.ToList())).ToList();
        var hidden = groups.Where(x => x.Available == 0 && x.Name != _selectedSubscription).ToList();
        var visible = _showUnavailableSubscriptions ? groups : groups.Except(hidden).ToList();
        _visibleSubscriptionCards = 1 + visible.Count + (hidden.Count > 0 ? 1 : 0);
        AddSubscription("全部订阅", _allScores, null);
        foreach (var group in visible) AddSubscription(group.Name, group.Scores, group.Name);
        if (hidden.Count > 0) AddUnavailableSubscriptionsBadge(hidden);
        UpdateSubscriptionCardWidths();
    }

    private void AddSubscription(string name, IReadOnlyList<NodeScore> scores, string? filter)
    {
        var available = scores.Count(x => x.SuccessRate > 0);
        var availability = scores.Count == 0 ? 0 : (double)available / scores.Count;
        var delay = scores.Where(x => x.MedianDelay > 0).Select(x => x.MedianDelay).DefaultIfEmpty().Average();
        var best = scores.OrderByDescending(x => x.CombinedScore).FirstOrDefault();
        var button = new System.Windows.Controls.Button
        {
            Background = UiColors.ForRatio(availability), BorderBrush = filter == _selectedSubscription ? UiColors.Accent : UiColors.Border,
            BorderThickness = new Thickness(filter == _selectedSubscription ? 2 : 1), Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 6, 6), Width = SubscriptionCardWidth(), MinHeight = 78, HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = "subscription-card",
            Content = new TextBlock
            {
                Text = $"{name}\n可用 {available}/{scores.Count}  ·  {availability:P0}\n平均延迟 {(delay > 0 ? $"{delay:F0} ms" : "-")}\n最佳 {best?.Name ?? "-"}",
                TextWrapping = TextWrapping.Wrap, Foreground = UiColors.Text, FontSize = 12, LineHeight = 15
            },
            ToolTip = $"{name}\n可用 {available}/{scores.Count}  ·  {availability:P0}\n平均延迟 {(delay > 0 ? $"{delay:F0} ms" : "-")}\n最佳 {best?.Name ?? "-"}"
        };
        button.Click += (_, _) =>
        {
            _selectedSubscription = filter;
            RefreshSubscriptions();
            ApplySubscriptionFilter();
            RefreshRecommendations();
            UpdateScopeBadge();
        };
        _subscriptions.Children.Add(button);
    }

    private void AddUnavailableSubscriptionsBadge(IReadOnlyList<SubscriptionGroup> hidden)
    {
        var text = _showUnavailableSubscriptions ? $"收起 {hidden.Count} 个不可用订阅" : $"隐藏 {hidden.Count} 个不可用订阅";
        var tooltip = string.Join("\n", hidden.Select(x => $"{x.Name}: 0/{x.Scores.Count}"));
        var button = new System.Windows.Controls.Button
        {
            Background = UiColors.Empty,
            BorderBrush = UiColors.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 6, 6),
            Width = SubscriptionCardWidth(),
            MinHeight = 42,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = "subscription-card",
            Content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiColors.Muted,
                FontSize = 12,
                LineHeight = 15
            },
            ToolTip = tooltip
        };
        button.Click += (_, _) =>
        {
            _showUnavailableSubscriptions = !_showUnavailableSubscriptions;
            RefreshSubscriptions();
        };
        _subscriptions.Children.Add(button);
    }

    private double SubscriptionCardWidth()
    {
        var count = Math.Max(1, _visibleSubscriptionCards);
        var columns = Math.Min(4, count);
        while (columns > 1 && _subscriptionViewportWidth / columns < 180) columns--;
        return Math.Max(170, Math.Floor(_subscriptionViewportWidth / columns) - 8);
    }

    private void UpdateSubscriptionCardWidths()
    {
        var width = SubscriptionCardWidth();
        foreach (var button in _subscriptions.Children.OfType<System.Windows.Controls.Button>().Where(x => Equals(x.Tag, "subscription-card")))
            button.Width = width;
    }

    private List<NodeScore> ActiveScores() =>
        _allScores.Where(x => _selectedSubscription is null || SubscriptionKey(x) == _selectedSubscription).ToList();

    private void ApplySubscriptionFilter()
    {
        _rows.Clear();
        foreach (var score in _allScores.Where(x => _selectedSubscription is null || SubscriptionKey(x) == _selectedSubscription)) _rows.Add(score);
        ApplyCurrentSort();
    }

    private void SortGrid(object? sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (!_sortColumns.TryGetValue(e.Column, out var info)) return;
        if (_sortProperty == info.Property)
            _sortDirection = _sortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        else
        {
            _sortProperty = info.Property;
            _sortDirection = info.FirstDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        }
        ApplyCurrentSort();
    }

    private void ApplyCurrentSort()
    {
        var view = CollectionViewSource.GetDefaultView(_rows);
        if (view is ListCollectionView listView) listView.CustomSort = null;
        view.SortDescriptions.Clear();
        foreach (var pair in _sortColumns)
        {
            pair.Key.Header = HeaderBlock(pair.Value.Header, pair.Value.Tooltip);
            pair.Key.SortDirection = null;
        }
        if (_sortProperty is null) return;
        if (view is ListCollectionView sortableView)
            sortableView.CustomSort = new NodeScoreComparer(_sortProperty, _sortDirection);
        else
            view.SortDescriptions.Add(new SortDescription(_sortProperty, _sortDirection));
        foreach (var pair in _sortColumns.Where(x => x.Value.Property == _sortProperty))
        {
            pair.Key.Header = HeaderBlock($"{pair.Value.Header} {(_sortDirection == ListSortDirection.Descending ? "↓" : "↑")}", pair.Value.Tooltip);
            pair.Key.SortDirection = _sortDirection;
        }
    }

    private void UpdateScopeBadge()
    {
        _scopeText.Text = $"范围：{_selectedSubscription ?? "全部订阅"}";
        _scopeBadge.ToolTip = $"当前推荐范围：{_selectedSubscription ?? "全部订阅"}";
    }

    private static string StableHint(IReadOnlyList<NodeScore> scores)
    {
        var maxDelay = scores.Count == 0 ? 0 : scores.Max(x => x.Samples);
        if (maxDelay < QualityThresholds.RequiredDelaySamples) return $"还差延迟样本：当前最多 {maxDelay}/{QualityThresholds.RequiredDelaySamples}。";
        return "延迟样本已够，但暂时没有成功率达标且近期失败少的节点。";
    }

    private string RecommendationHint(IReadOnlyList<NodeScore> scores)
    {
        var maxDelay = scores.Count == 0 ? 0 : scores.Max(x => x.Samples);
        var maxSpeed = scores.Count == 0 ? 0 : scores.Max(x => x.SpeedSamples);
        var delayReady = scores.Count(x => x.StableEnough);
        var speedReady = scores.Count(x => x.SpeedEnough);
        if (maxDelay < QualityThresholds.RequiredDelaySamples) return $"还差延迟样本：当前最多 {maxDelay}/{QualityThresholds.RequiredDelaySamples}；有效速度样本 {maxSpeed} 个。";
        if (delayReady == 0) return "延迟样本已够，但没有成功率达标且近期失败少的节点。";
        if (speedReady == 0) return $"延迟样本已够，缺少有效速度样本；点“立即轻量测速”或等每日 {TimeSpan.Parse(_settings.DailySpeedTime):hh\\:mm} 测速。";
        return "已有样本，但没有同时满足稳定性、速度和近期失败门槛的节点。";
    }

    private static void SetCard(Border target, NodeScore? node, string scoreLabel, Func<NodeScore, double> score, string emptyText,
        Func<NodeScore, string>? valueText = null)
    {
        var value = node is null ? 0 : score(node);
        target.Background = node is null ? UiColors.Empty : UiColors.ForScore(value);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = node?.Name ?? "还差样本", FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = UiColors.Text });
        panel.Children.Add(new TextBlock
        {
            Text = node is null ? emptyText : $"{node.ClientName} · {node.Subscription} · {(valueText?.Invoke(node) ?? $"{scoreLabel} {value:F1}")}{(node.DataEnough ? "" : $" · {node.Status}")}\n成功率 {node.SuccessText} · 延迟 {node.DelayText} · 速度 {node.SpeedText}",
            Margin = new Thickness(0, 2, 0, 0), Foreground = UiColors.Text, TextWrapping = TextWrapping.Wrap,
            FontSize = 12, LineHeight = 15
        });
        target.Child = panel;
    }

    private async Task Schedule()
    {
        if (_settings.Paused || _engine.IsRunning) return;
        if (_settings.LastDelayRun is null || DateTime.Now - _settings.LastDelayRun > TimeSpan.FromMinutes(_settings.DelayIntervalMinutes)) await _engine.RunAsync("delay");
        else if ((_settings.LastSpeedRun?.Date ?? DateTime.MinValue.Date) < DateTime.Today && DateTime.Now.TimeOfDay >= TimeSpan.Parse(_settings.DailySpeedTime) && (DateTime.Now.Date > _started.Date || DateTime.Now - _started >= TimeSpan.FromMinutes(10))) await _engine.RunAsync("speed");
        else if (ShouldRunIdleDelayProbe()) await RunIdleDelayProbe();
    }

    private bool ShouldRunIdleDelayProbe()
    {
        if (!_settings.EnableIdleDelayProbe) return false;
        if (_settings.LastIdleDelayRun is not null && DateTime.Now - _settings.LastIdleDelayRun < TimeSpan.FromMinutes(_settings.IdleDelayIntervalMinutes)) return false;
        return _networkIdle.IsIdle(_settings);
    }

    private async Task RunIdleDelayProbe()
    {
        SetStatus("网络空闲，正在补充低流量延迟巡检");
        await _engine.RunAsync("delay");
        _settings.LastIdleDelayRun = DateTime.Now;
        Paths.SaveSettings(_settings);
    }

    private Task PickPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "v2rayN|v2rayN.exe", Title = "选择 v2rayN.exe" };
        if (dialog.ShowDialog() == true) { _settings.V2rayNPath = dialog.FileName; Paths.SaveSettings(_settings); Refresh(); }
        return Task.CompletedTask;
    }
    private Task OpenSettings()
    {
        new SettingsWindow(_settings) { Owner = this }.ShowDialog();
        Paths.SaveSettings(_settings);
        StartupManager.Apply(_settings.StartWithWindows);
        Refresh();
        return Task.CompletedTask;
    }
    private async Task Switch(NodeScore score)
    {
        var node = _engine.Nodes().FirstOrDefault(x => x.Id == score.NodeId);
        if (node is null) { MessageBox.Show("目标节点已不存在。"); return; }
        if (MessageBox.Show($"切换 {node.ClientName} 到“{node.Name}”可能短暂中断连接。继续？", "确认切换", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var live = await _engine.ValidateSwitchTargetAsync(node);
            var liveSuccess = live.SuccessRate ?? 0;
            var liveDelay = live.DelayMs ?? 0;
            var maxDelay = Math.Max(1000, score.MedianDelay > 0 ? score.MedianDelay * 2.5 : 1000);
            if (liveSuccess < 2d / 3 || liveDelay <= 0 || liveDelay > maxDelay)
                throw new InvalidOperationException($"切换前复测未通过：成功率 {liveSuccess:P0}，延迟 {(liveDelay > 0 ? $"{liveDelay:F0} ms" : "失败")}。已取消切换。");
            await _engine.SwitchAsync(node);
            MessageBox.Show("切换完成。");
        }
        catch (Exception ex) { MessageBox.Show($"切换未执行或失败：{ex.Message}"); }
    }

    private static Border RecommendationShell(string title, string subtitle, Border content)
    {
        content.Padding = new Thickness(8); content.CornerRadius = new CornerRadius(8); content.Background = UiColors.Empty;
        var panel = new StackPanel { Margin = new Thickness(3) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = UiColors.Text });
        panel.Children.Add(new TextBlock { Text = subtitle, Foreground = UiColors.Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(content);
        return new Border { Background = Brushes.White, BorderBrush = UiColors.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(3), Padding = new Thickness(6), Child = panel };
    }
    private static System.Windows.Controls.Button ActionButton(string text, Func<Task> action, bool primary = false)
    {
        var button = new System.Windows.Controls.Button { Content = text, Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(0, 0, 7, 4), Background = primary ? UiColors.Accent : Brushes.White, Foreground = primary ? Brushes.White : UiColors.Text, BorderBrush = primary ? UiColors.Accent : UiColors.Border };
        button.Click += async (_, _) => await action(); return button;
    }
    private static string SubscriptionKey(NodeScore score) => $"{score.ClientName} / {score.Subscription}";
    private static string Yes(bool value) => value ? "✓" : "×";
    private static string Mark(bool value) => value ? "✓" : "—";

    private void AddColumn(DataGrid grid, DataGridColumn column, string header, string property, bool firstDescending, string? tooltip = null)
    {
        column.SortMemberPath = property;
        column.Header = HeaderBlock(header, tooltip);
        _sortColumns[column] = new(header, property, firstDescending, tooltip);
        grid.Columns.Add(column);
    }

    private static TextBlock HeaderBlock(string text, string? tooltip)
    {
        return new TextBlock
        {
            Text = text,
            ToolTip = tooltip,
            Foreground = UiColors.Text,
            TextWrapping = TextWrapping.NoWrap
        };
    }

    private static DataGridTextColumn TextColumn(string header, string binding, double width) => new() { Header = header, Binding = new Binding(binding), Width = new DataGridLength(width) };
    private static DataGridTemplateColumn MetricColumn(string header, string text, string value, MetricKind kind, double width)
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); factory.SetValue(Border.PaddingProperty, new Thickness(5, 1, 5, 1)); factory.SetValue(Border.MarginProperty, new Thickness(4, 2, 4, 2));
        factory.SetBinding(Border.BackgroundProperty, new Binding(value) { Converter = new MetricBrushConverter(kind) });
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding(text)); label.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center); label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); label.SetValue(TextBlock.ForegroundProperty, UiColors.Text);
        factory.AppendChild(label);
        return new DataGridTemplateColumn { Header = header, CellTemplate = new DataTemplate { VisualTree = factory }, Width = new DataGridLength(width) };
    }
}

public enum MetricKind { Success, Delay, Speed, Score, Confidence }

public sealed record SubscriptionGroup(string Name, IReadOnlyList<NodeScore> Scores)
{
    public int Available => Scores.Count(x => x.SuccessRate > 0);
}

public sealed record SortColumnInfo(string Header, string Property, bool FirstDescending, string? Tooltip);

public sealed class NodeScoreComparer(string property, ListSortDirection direction) : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is not NodeScore left) return 1;
        if (y is not NodeScore right) return -1;

        var leftHasValue = HasSortValue(left, property);
        var rightHasValue = HasSortValue(right, property);
        if (!leftHasValue && !rightHasValue) return TieBreak(left, right);
        if (!leftHasValue) return 1;
        if (!rightHasValue) return -1;

        var result = Value(left, property).CompareTo(Value(right, property));
        if (direction == ListSortDirection.Descending) result = -result;
        return result != 0 ? result : TieBreak(left, right);
    }

    private static bool HasSortValue(NodeScore score, string property) => property switch
    {
        nameof(NodeScore.SuccessRate) => score.Samples > 0,
        nameof(NodeScore.MedianDelay) => score.Samples > 0 && score.MedianDelay > 0,
        nameof(NodeScore.MedianSpeed) => score.SpeedSamples > 0 && score.MedianSpeed >= QualityThresholds.MinUsefulSpeedBytesPerSecond,
        nameof(NodeScore.StabilityScore) => score.Samples > 0,
        nameof(NodeScore.CombinedScore) => score.Samples > 0,
        nameof(NodeScore.Samples) => score.Samples > 0,
        nameof(NodeScore.Confidence) => score.Samples > 0 || score.SpeedSamples > 0,
        _ => true
    };

    private static double Value(NodeScore score, string property) => property switch
    {
        nameof(NodeScore.SuccessRate) => score.SuccessRate,
        nameof(NodeScore.MedianDelay) => score.MedianDelay,
        nameof(NodeScore.MedianSpeed) => score.MedianSpeed,
        nameof(NodeScore.StabilityScore) => score.StabilityScore,
        nameof(NodeScore.CombinedScore) => score.CombinedScore,
        nameof(NodeScore.Samples) => score.Samples,
        nameof(NodeScore.Confidence) => score.Confidence,
        _ => 0
    };

    private static int TieBreak(NodeScore left, NodeScore right)
    {
        var client = string.Compare(left.ClientName, right.ClientName, StringComparison.CurrentCultureIgnoreCase);
        if (client != 0) return client;
        var subscription = string.Compare(left.Subscription, right.Subscription, StringComparison.CurrentCultureIgnoreCase);
        if (subscription != 0) return subscription;
        return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
    }
}

public sealed class MetricBrushConverter(MetricKind kind) : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var number = value is double d ? d : 0;
        return kind switch
        {
            MetricKind.Delay => number <= 0 ? UiColors.Empty : UiColors.ForScore(100 * Math.Exp(-number / 500)),
            MetricKind.Speed => number <= 0 ? UiColors.Empty : UiColors.ForScore(Math.Min(100, number / 1024 / 1024 * 12.5)),
            MetricKind.Success or MetricKind.Confidence => UiColors.ForRatio(number),
            _ => UiColors.ForScore(number)
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public static class UiColors
{
    public static readonly Brush Window = Brush("#F5F7FA"), Alternate = Brush("#FAFBFC"), Text = Brush("#18212F"), Muted = Brush("#657184");
    public static readonly Brush Border = Brush("#DCE2EA"), Accent = Brush("#3375E0"), Empty = Brush("#E9EDF2");
    public static readonly Brush Excellent = Brush("#CDEFD9"), Good = Brush("#DDF6CE"), Fair = Brush("#FFF3BF"),
        Watch = Brush("#FFE0A6"), Poor = Brush("#FFD4C2"), Bad = Brush("#FFD4D4");
    public static Brush ForRatio(double value) => ForScore(value * 100);
    public static Brush ForScore(double value) => value >= 85 ? Excellent : value >= 70 ? Good : value >= 55 ? Fair : value >= 40 ? Watch : value >= 20 ? Poor : value > 0 ? Bad : Empty;
    private static Brush Brush(string value) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!; brush.Freeze(); return brush; }
}

public sealed class SettingsWindow : Window
{
    public SettingsWindow(MonitorSettings settings)
    {
        Title = "监控设置"; Width = 460; Height = 760; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var startup = new CheckBox { Content = "开机后自动启动监控", IsChecked = settings.StartWithWindows, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(startup);
        var delay = Field(panel, "延迟测试间隔（分钟）", settings.DelayIntervalMinutes.ToString());
        var idleProbe = new CheckBox
        {
            Content = "网络空闲时自动补充延迟巡检",
            IsChecked = settings.EnableIdleDelayProbe,
            Margin = new Thickness(0, 8, 0, 4),
            ToolTip = "只做小请求延迟测试，不做下载测速；用于抓出偶发连不上的节点。"
        };
        panel.Children.Add(idleProbe);
        var idleInterval = Field(panel, "空闲巡检最短间隔（分钟）", settings.IdleDelayIntervalMinutes.ToString());
        var idleMinutes = Field(panel, "判定网络空闲需连续（分钟）", settings.IdleRequiredMinutes.ToString());
        var idleThreshold = Field(panel, "网络空闲阈值（KiB/s）", settings.IdleNetworkThresholdKBps.ToString());
        var time = Field(panel, "每日速度测试时间（HH:mm）", settings.DailySpeedTime);
        var limit = Field(panel, "全节点初筛上限（MiB）", (settings.SpeedLimitBytes / 1048576d).ToString("0.##"));
        var refineLimit = Field(panel, "高速候选精测上限（MiB）", (settings.RefineSpeedLimitBytes / 1048576d).ToString("0.##"));
        var refineCount = Field(panel, "进入精测的节点数量", settings.RefineTopCount.ToString());
        var timeout = Field(panel, "单节点测速最长时间（秒）", settings.SpeedTimeoutSeconds.ToString());
        var days = Field(panel, "推荐历史窗口（天）", settings.HistoryDays.ToString());
        var stability = Field(panel, "稳定性权重（%）", (settings.StabilityWeight * 100).ToString("0"));
        var speed = Field(panel, "速度权重（%）", (settings.SpeedWeight * 100).ToString("0"));
        var latency = Field(panel, "延迟权重（%）", (settings.DelayWeight * 100).ToString("0"));
        var save = new System.Windows.Controls.Button { Content = "保存", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 14, 0, 0), Background = UiColors.Accent, Foreground = Brushes.White };
        save.Click += (_, _) =>
        {
            try
            {
                var weights = new[] { double.Parse(stability.Text), double.Parse(speed.Text), double.Parse(latency.Text) };
                if (weights.Sum() <= 0) throw new InvalidDataException("权重总和必须大于零");
                settings.DelayIntervalMinutes = Math.Max(5, int.Parse(delay.Text)); settings.DailySpeedTime = TimeSpan.Parse(time.Text).ToString(@"hh\:mm");
                settings.EnableIdleDelayProbe = idleProbe.IsChecked == true;
                settings.IdleDelayIntervalMinutes = Math.Clamp(int.Parse(idleInterval.Text), 5, 240);
                settings.IdleRequiredMinutes = Math.Clamp(int.Parse(idleMinutes.Text), 1, 30);
                settings.IdleNetworkThresholdKBps = Math.Clamp(int.Parse(idleThreshold.Text), 16, 4096);
                settings.SpeedLimitBytes = Math.Clamp((int)(double.Parse(limit.Text) * 1048576), 65536, 20 * 1048576);
                settings.RefineSpeedLimitBytes = Math.Clamp((int)(double.Parse(refineLimit.Text) * 1048576), settings.SpeedLimitBytes, 50 * 1048576);
                settings.RefineTopCount = Math.Clamp(int.Parse(refineCount.Text), 1, 50); settings.SpeedTimeoutSeconds = Math.Clamp(int.Parse(timeout.Text), 2, 30); settings.HistoryDays = Math.Clamp(int.Parse(days.Text), 1, 30);
                settings.StartWithWindows = startup.IsChecked == true;
                settings.StabilityWeight = weights[0] / weights.Sum(); settings.SpeedWeight = weights[1] / weights.Sum(); settings.DelayWeight = weights[2] / weights.Sum(); DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "设置无效"); }
        };
        panel.Children.Add(save);
    }
    private static TextBox Field(Panel panel, string label, string value) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 7, 0, 3) }); var box = new TextBox { Text = value, Padding = new Thickness(6) }; panel.Children.Add(box); return box; }
}
