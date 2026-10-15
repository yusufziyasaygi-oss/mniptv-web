using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MNIPTV.UI;

public sealed class BrowseView : Grid
{
    private enum Area { Sidebar, Categories, Search, Grid }

    private readonly AppModel _model;
    private readonly Action _onAccount;

    private Area _area = Area.Sidebar;
    private int _sideIndex = 1, _catIndex, _gridIndex;
    private List<Tile> _ordered = new();
    private int _liveCount;
    private CancellationTokenSource? _categoryWork, _searchWork;
    private bool _syncingQuery;

    private readonly List<SideRow> _sideRows = new();
    private readonly SideRow _accountRow = new("Hesap", "");
    private readonly Border _categoryColumn = new() { Width = 250 };
    private readonly TextBlock _categoryTitle = MN.Text("", 13, MN.Muted);
    private readonly VirtualPanel _categories = new() { ContentPadding = new Thickness(0, 0, 0, 20) };
    private readonly TextBox _search = new();
    private readonly Grid _searchHost = new() { Margin = new Thickness(0, 30, 0, 10) };
    private readonly TextBlock _title = MN.Text("", 18, MN.White, bold: true);
    private readonly VirtualPanel _grid = new();
    private readonly Spinner _spinner = new(44) { Visibility = Visibility.Collapsed };
    private readonly TextBlock _empty = MN.Text("", 15, MN.Text2, maxLines: 6);

    private int AccountIndex => SectionExt.All.Length;

    public BrowseView(AppModel model, Action onAccount)
    {
        _model = model;
        _onAccount = onAccount;
        Background = MN.Black;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Children.Add(BuildSidebar().At(0, 0));
        Children.Add(BuildCategories().At(0, 1));
        Children.Add(BuildContent().At(0, 2));

        model.Changed += OnModelChanged;
        _sideIndex = (int)model.Section;
        RenderAll();
    }

    // layout

    private UIElement BuildSidebar()
    {
        var root = MN.Rows("auto", "auto", "*", "auto");
        root.Background = MN.Surface;
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 34, 0, 22) };
        brand.Children.Add(new System.Windows.Shapes.Ellipse { Width = 13, Height = 13, Fill = MN.Blue, VerticalAlignment = VerticalAlignment.Center });
        var name = MN.Text("MN IPTV", 19, MN.White, bold: true);
        name.Margin = new Thickness(10, 0, 0, 0);
        brand.Children.Add(name);
        root.Children.Add(brand.At(0));

        var list = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        foreach (var s in SectionExt.All)
        {
            var row = new SideRow(s.Title(), s.Glyph()) { Margin = new Thickness(0, 2, 0, 2) };
            var index = (int)s;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _area = Area.Sidebar;
                _sideIndex = index;
                ActivateSidebar();
                RenderFocus();
            };
            _sideRows.Add(row);
            list.Children.Add(row);
        }
        root.Children.Add(list.At(1));
        _accountRow.Margin = new Thickness(12, 0, 12, 14);
        _accountRow.MouseLeftButtonUp += (_, e) => { e.Handled = true; _onAccount(); };
        root.Children.Add(_accountRow.At(3));
        return root;
    }

    private UIElement BuildCategories()
    {
        var root = MN.Rows("auto", "*");
        _categoryTitle.Margin = new Thickness(24, 34, 0, 14);
        root.Children.Add(_categoryTitle.At(0));
        _categories.Margin = new Thickness(10, 0, 4, 0);
        _categories.Create = _ => new SideRow("");
        _categories.Update = (i, e) =>
        {
            e.Tag = i;
            if (e is SideRow r && i < _model.Categories.Count)
                r.Set(_model.SelectedCategory == i, IsFocusedArea(Area.Categories) && _catIndex == i, _model.Categories[i].Name);
        };
        _categories.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject d) return;
            var row = FindParent<SideRow>(d);
            if (row == null) return;
            var i = IndexOfCategoryRow(row);
            if (i < 0) return;
            e.Handled = true;
            _area = Area.Categories;
            _catIndex = i;
            _model.SelectCategory(i);
            RenderFocus();
        };
        root.Children.Add(_categories.At(1));
        _categoryColumn.Child = root;
        return _categoryColumn;
    }

    private static int IndexOfCategoryRow(SideRow row) => row.Tag is int t ? t : -1;

    private UIElement BuildContent()
    {
        var root = MN.Rows("auto", "auto", "*");
        root.Margin = new Thickness(22, 0, 22, 0);

        _search.Height = 44;
        _search.FontSize = 16;
        _search.FontFamily = MN.Font;
        _search.Foreground = MN.White;
        _search.Background = MN.Card;
        _search.CaretBrush = MN.White;
        _search.BorderBrush = MN.Divider;
        _search.BorderThickness = new Thickness(2);
        _search.Padding = new Thickness(10, 0, 10, 0);
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.GotKeyboardFocus += (_, _) => { _search.BorderBrush = MN.Blue; _area = Area.Search; RenderFocus(); };
        _search.LostKeyboardFocus += (_, _) => _search.BorderBrush = MN.Divider;
        var hint = MN.Text("Kanal, film veya dizi ara", 16, MN.Muted);
        hint.Margin = new Thickness(14, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        _search.TextChanged += (_, _) =>
        {
            hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_syncingQuery) return;
            _model.Query = _search.Text;
            ScheduleSearch();
        };
        _searchHost.Children.Add(_search);
        _searchHost.Children.Add(hint);
        root.Children.Add(_searchHost.At(0));

        _title.Margin = new Thickness(4, 30, 0, 12);
        root.Children.Add(_title.At(1));

        var body = new Grid();
        _grid.Create = i => i < _liveCount ? new LiveTile() : (FrameworkElement)new PosterTile();
        _grid.Update = (i, e) => UpdateTile(i, e);
        _grid.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var i = HitTile(e.OriginalSource);
            if (i < 0) return;
            e.Handled = true;
            _area = Area.Grid;
            _gridIndex = i;
            RenderFocus();
            _model.Open(_ordered[i]);
        };
        _grid.PreviewMouseRightButtonUp += (_, e) =>
        {
            var i = HitTile(e.OriginalSource);
            if (i < 0) return;
            e.Handled = true;
            ShowContextMenu(_ordered[i]);
        };
        body.Children.Add(_grid);
        body.Children.Add(_spinner);
        _empty.TextAlignment = TextAlignment.Center;
        _empty.MaxWidth = 520;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        body.Children.Add(_empty);
        root.Children.Add(body.At(2));
        return root;
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private int HitTile(object source)
    {
        if (source is not DependencyObject d) return -1;
        var tile = (FrameworkElement?)FindParent<LiveTile>(d) ?? FindParent<PosterTile>(d);
        return tile?.Tag is int i && i < _ordered.Count ? i : -1;
    }

    private void UpdateTile(int i, FrameworkElement e)
    {
        if (i >= _ordered.Count) return;
        e.Tag = i;
        var tile = _ordered[i];
        var focused = IsFocusedArea(Area.Grid) && _gridIndex == i;
        if (tile.Media is { } m)
        {
            if (e is LiveTile lt) lt.Set(m, tile.Favorite, focused);
            else if (e is PosterTile poster)
                poster.Set(m.Icon, m.Name, null, tile.ShowKind ? (m.Kind == Kind.Movie ? "Film" : "Dizi") : null, tile.Favorite, null, focused);
        }
        else if (tile.Resume is { } p && e is PosterTile pt)
        {
            var sub = p.Kind == Kind.Series
                ? (p.Position <= 0 ? $"Sıradaki: S{p.Season} B{p.Episode}" : $"S{p.Season} B{p.Episode} · {Fmt.Time(p.Position)}")
                : Fmt.Time(p.Position);
            pt.Set(p.Image, p.Title, sub, p.Kind == Kind.Series ? "Dizi" : "Film", false,
                p.Duration > 0 && p.Position > 0 ? p.Fraction : null, focused);
        }
    }

    private void ShowContextMenu(Tile tile)
    {
        var menu = new ContextMenu();
        void Item(string text, Action action)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        if (tile.Media is { } e)
        {
            Item(e.Kind == Kind.Movie ? "Oynat" : "Aç", () => _model.Open(tile));
            if (e.Kind == Kind.Movie && _model.Store.GetProgress(e.Key) != null) Item("Baştan başla", () => _model.PlayMovie(e, 0));
            Item(tile.Favorite ? "Favorilerden çıkar" : "Favorilere ekle", () => _model.ToggleFavorite(e));
        }
        else if (tile.Resume is { } p)
        {
            Item("Devam et", () => _model.Open(tile));
            Item("İzlemeye Devam'dan kaldır", () => _model.RemoveProgress(p));
        }
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // state

    private bool _active = true;

    /// Whether the catalogue is the visible screen (keyboard highlight only shows then).
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            if (value) Reclaim();
            RenderFocus();
        }
    }

    private bool IsFocusedArea(Area a) => _active && _area == a;

    public void Reclaim()
    {
        if (_area == Area.Search && _search.IsKeyboardFocused) return;
        if (_area == Area.Search) _area = Area.Sidebar;
        Keyboard.ClearFocus();
        Window.GetWindow(this)?.Focus();
    }

    private void OnModelChanged(string what)
    {
        switch (what)
        {
            case "section":
                _sideIndex = (int)_model.Section;
                _catIndex = 0;
                _gridIndex = 0;
                if (_model.Section == Section.Search)
                {
                    _area = Area.Search;
                    _syncingQuery = true;
                    _search.Text = _model.Query;
                    _syncingQuery = false;
                    Dispatcher.BeginInvoke(() => { _search.Focus(); _search.CaretIndex = _search.Text.Length; });
                }
                else if (_area == Area.Search)
                {
                    _area = Area.Sidebar;
                    Reclaim();
                }
                RenderAll();
                break;
            case "categories":
                if (_model.SelectedCategory >= 0 && _area != Area.Categories) _catIndex = _model.SelectedCategory;
                RenderCategories();
                break;
            case "tiles":
                RebuildGrid();
                break;
            case "tiles-state":
                _grid.RefreshItems();
                break;
            case "loading":
                RenderStatus();
                break;
            case "title":
                _title.Text = _model.ContentTitle;
                break;
            case "route":
                Active = _model.RouteStack.Count == 0;
                break;
        }
    }

    private void RenderAll()
    {
        var hasCategories = _model.Section.Kind() != null;
        _categoryColumn.Visibility = hasCategories ? Visibility.Visible : Visibility.Collapsed;
        _categoryTitle.Text = _model.Section.Title();
        _searchHost.Visibility = _model.Section == Section.Search ? Visibility.Visible : Visibility.Collapsed;
        _title.Margin = new Thickness(4, _model.Section == Section.Search ? 4 : 30, 0, 12);
        _title.Text = _model.ContentTitle;
        RenderCategories();
        RebuildGrid();
        RenderFocus();
    }

    private void RenderCategories()
    {
        var count = _model.Categories.Count;
        _categories.Create = i => new SideRow("") { Tag = i };
        _categories.SetBlocks(new[] { new Block { Count = count, MinItemWidth = 100000, ItemHeight = 40, VSpacing = 3 } }, keepOffset: true);
    }

    private void RebuildGrid()
    {
        var lives = _model.Tiles.Where(t => t.IsLive).ToList();
        var posters = _model.Tiles.Where(t => !t.IsLive).ToList();
        _ordered = lives.Concat(posters).ToList();
        _liveCount = lives.Count;
        if (_gridIndex >= _ordered.Count) _gridIndex = Math.Max(0, _ordered.Count - 1);
        if (_ordered.Count == 0 && _area == Area.Grid) _area = _model.Section.Kind() != null ? Area.Categories : Area.Sidebar;
        _grid.SetBlocks(new[]
        {
            new Block { Count = lives.Count, MinItemWidth = 250, ItemHeight = 72, HSpacing = 12, VSpacing = 12 },
            new Block { Count = posters.Count, MinItemWidth = 150, MinColumns = 2, ItemHeight = 274, HSpacing = 12, VSpacing = 14 },
        });
        RenderStatus();
        RenderFocus();
    }

    private void RenderStatus()
    {
        _spinner.Visibility = _model.Loading ? Visibility.Visible : Visibility.Collapsed;
        var empty = !_model.Loading ? _model.EmptyMessage : null;
        _empty.Text = empty ?? "";
        _empty.Visibility = empty == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderFocus()
    {
        for (var i = 0; i < _sideRows.Count; i++)
            _sideRows[i].Set((int)_model.Section == i, IsFocusedArea(Area.Sidebar) && _sideIndex == i);
        _accountRow.Set(false, IsFocusedArea(Area.Sidebar) && _sideIndex == AccountIndex);
        _categories.RefreshItems();
        _grid.RefreshItems();
    }

    // actions

    private void ActivateSidebar()
    {
        if (_sideIndex == AccountIndex) { _onAccount(); return; }
        var s = (Section)_sideIndex;
        _model.Select(s, force: s == _model.Section && s.Kind() != null && _model.Categories.Count == 0);
        if (s == Section.Search) FocusSearch();
    }

    private void EnterFromSidebar()
    {
        if (_sideIndex >= AccountIndex) { _onAccount(); return; }
        var s = (Section)_sideIndex;
        if (s != _model.Section) _model.Select(s);
        if (s == Section.Search) FocusSearch();
        else if (s.Kind() != null)
        {
            _area = Area.Categories;
            _catIndex = Math.Max(0, _model.SelectedCategory);
        }
        else if (_ordered.Count > 0)
        {
            _area = Area.Grid;
            _gridIndex = 0;
            _grid.ScrollIntoView(0);
        }
    }

    /// Types into the search box (used by the self test).
    public void TypeSearch(string text)
    {
        FocusSearch();
        _search.Text = text;
        _search.CaretIndex = text.Length;
    }

    private void FocusSearch()
    {
        _area = Area.Search;
        _search.Focus();
        _search.CaretIndex = _search.Text.Length;
    }

    private void LeaveSearch()
    {
        Keyboard.ClearFocus();
        Window.GetWindow(this)?.Focus();
        if (_ordered.Count > 0)
        {
            _area = Area.Grid;
            _gridIndex = 0;
            _grid.ScrollIntoView(0);
        }
        else _area = Area.Sidebar;
    }

    private async void ScheduleSearch()
    {
        _searchWork?.Cancel();
        var cts = _searchWork = new CancellationTokenSource();
        try { await Task.Delay(500, cts.Token); } catch (TaskCanceledException) { return; }
        _model.RunSearch();
    }

    private async void ScheduleCategory(int i)
    {
        _categoryWork?.Cancel();
        var cts = _categoryWork = new CancellationTokenSource();
        try { await Task.Delay(250, cts.Token); } catch (TaskCanceledException) { return; }
        _model.SelectCategory(i);
    }

    // keyboard

    public bool HandleKey(KeyInput k)
    {
        var handled = Process(k);
        RenderFocus();
        return handled;
    }

    private bool Process(KeyInput k)
    {
        if (_search.IsKeyboardFocused)
        {
            switch (k.Key)
            {
                case System.Windows.Input.Key.Enter:
                    _searchWork?.Cancel();
                    _model.RunSearch();
                    LeaveSearch();
                    return true;
                case System.Windows.Input.Key.Down:
                    LeaveSearch();
                    return true;
                case System.Windows.Input.Key.Escape:
                    Keyboard.ClearFocus();
                    Window.GetWindow(this)?.Focus();
                    _area = Area.Sidebar;
                    return true;
                default:
                    return false;
            }
        }

        var count = _ordered.Count;
        switch (k.Key)
        {
            case System.Windows.Input.Key.Up:
            case System.Windows.Input.Key.Down:
            {
                var up = k.Key == System.Windows.Input.Key.Up;
                switch (_area)
                {
                    case Area.Sidebar:
                        _sideIndex = Math.Min(AccountIndex, Math.Max(0, _sideIndex + (up ? -1 : 1)));
                        if (_sideIndex < AccountIndex && (Section)_sideIndex is var s && s != Section.Search) _model.Select(s);
                        break;
                    case Area.Categories:
                        var n = _model.Categories.Count;
                        if (n == 0) return true;
                        _catIndex = Math.Min(n - 1, Math.Max(0, _catIndex + (up ? -1 : 1)));
                        _categories.ScrollIntoView(_catIndex);
                        ScheduleCategory(_catIndex);
                        break;
                    case Area.Grid:
                        MoveGridVertically(up);
                        break;
                    case Area.Search:
                        if (!up) LeaveSearch();
                        break;
                }
                return true;
            }
            case System.Windows.Input.Key.Left:
                switch (_area)
                {
                    case Area.Grid:
                        if (IsFirstColumn(_gridIndex))
                        {
                            if (_model.Section.Kind() != null) { _area = Area.Categories; _catIndex = Math.Max(0, _model.SelectedCategory); }
                            else _area = Area.Sidebar;
                        }
                        else Move(_gridIndex - 1);
                        break;
                    case Area.Categories:
                    case Area.Search:
                        _area = Area.Sidebar;
                        break;
                }
                return true;
            case System.Windows.Input.Key.Right:
                switch (_area)
                {
                    case Area.Sidebar:
                        if (_model.Section.Kind() != null && _model.Categories.Count > 0)
                        {
                            _area = Area.Categories;
                            _catIndex = Math.Max(0, _model.SelectedCategory);
                        }
                        else if (_model.Section == Section.Search) FocusSearch();
                        else if (count > 0) { _area = Area.Grid; Move(0); }
                        break;
                    case Area.Categories:
                        _categoryWork?.Cancel();
                        if (_catIndex != _model.SelectedCategory) _model.SelectCategory(_catIndex);
                        else if (count > 0) { _area = Area.Grid; Move(0); }
                        break;
                    case Area.Grid:
                        if (!IsLastColumn(_gridIndex) && _gridIndex + 1 < count) Move(_gridIndex + 1);
                        break;
                }
                return true;
            case System.Windows.Input.Key.Enter:
            case System.Windows.Input.Key.Space:
                if (k.Repeat) return true;
                switch (_area)
                {
                    case Area.Sidebar:
                        if (_sideIndex == AccountIndex) _onAccount(); else EnterFromSidebar();
                        break;
                    case Area.Categories:
                        _categoryWork?.Cancel();
                        if (_catIndex != _model.SelectedCategory) _model.SelectCategory(_catIndex);
                        if (_ordered.Count > 0) { _area = Area.Grid; Move(0); }
                        break;
                    case Area.Grid:
                        if (_gridIndex < _ordered.Count) _model.Open(_ordered[_gridIndex]);
                        break;
                    case Area.Search:
                        FocusSearch();
                        break;
                }
                return true;
            case System.Windows.Input.Key.Escape:
            case System.Windows.Input.Key.Back:
                switch (_area)
                {
                    case Area.Grid:
                        if (_model.Section.Kind() != null) _area = Area.Categories;
                        else if (_model.Section == Section.Search) FocusSearch();
                        else _area = Area.Sidebar;
                        break;
                    case Area.Categories:
                        _area = Area.Sidebar;
                        break;
                }
                return true;
            case System.Windows.Input.Key.F:
                if (_area == Area.Grid && _gridIndex < _ordered.Count && _ordered[_gridIndex].Media is { } e && !k.Repeat)
                {
                    _model.ToggleFavorite(e);
                    return true;
                }
                return false;
            case System.Windows.Input.Key.Delete:
                if (_area == Area.Grid && _gridIndex < _ordered.Count && _ordered[_gridIndex].Resume is { } p)
                {
                    _model.RemoveProgress(p);
                    return true;
                }
                return false;
            case System.Windows.Input.Key.Home:
                if (_area == Area.Grid && count > 0) Move(0);
                return true;
            case System.Windows.Input.Key.End:
                if (_area == Area.Grid && count > 0) Move(count - 1);
                return true;
            case System.Windows.Input.Key.PageDown:
            case System.Windows.Input.Key.PageUp:
                if (_area == Area.Grid && count > 0)
                    for (var r = 0; r < 4; r++) MoveGridVertically(k.Key == System.Windows.Input.Key.PageUp);
                return true;
            default:
                return false;
        }
    }

    private void Move(int index)
    {
        _gridIndex = Math.Max(0, Math.Min(index, _ordered.Count - 1));
        _grid.ScrollIntoView(_gridIndex);
    }

    // Grid geometry: channels block (liveCols wide) followed by posters block (posterCols wide).

    private int LiveCols => _grid.ColumnsOf(0);
    private int PosterCols => _grid.ColumnsOf(1);

    private bool IsFirstColumn(int i) => i < _liveCount ? i % LiveCols == 0 : (i - _liveCount) % PosterCols == 0;

    private bool IsLastColumn(int i) =>
        i < _liveCount ? i % LiveCols == LiveCols - 1 : (i - _liveCount) % PosterCols == PosterCols - 1;

    private void MoveGridVertically(bool up)
    {
        int lc = _liveCount, pc = _ordered.Count - _liveCount, i = _gridIndex;
        int liveCols = LiveCols, posterCols = PosterCols;
        if (i < lc)
        {
            var col = i % liveCols;
            if (up)
            {
                if (i - liveCols >= 0) Move(i - liveCols);
                else if (_model.Section == Section.Search) FocusSearch();
            }
            else if (i + liveCols < lc) Move(i + liveCols);
            else if (i / liveCols < (lc - 1) / liveCols) Move(lc - 1);
            else if (pc > 0) Move(lc + Math.Min(col, pc - 1));
        }
        else
        {
            var j = i - lc;
            var col = j % posterCols;
            if (up)
            {
                if (j - posterCols >= 0) Move(i - posterCols);
                else if (lc > 0)
                {
                    var lastRowStart = (lc - 1) / liveCols * liveCols;
                    Move(Math.Min(lc - 1, lastRowStart + Math.Min(col, liveCols - 1)));
                }
                else if (_model.Section == Section.Search) FocusSearch();
            }
            else if (j + posterCols < pc) Move(i + posterCols);
            else if (j / posterCols < (pc - 1) / posterCols) Move(lc + pc - 1);
        }
    }
}
