using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MNIPTV.UI;

public sealed class SeriesView : Grid
{
    private enum Area { Buttons, Seasons, Episodes }

    private readonly MediaEntry _entry;
    private readonly AppModel _model;
    private readonly Action<ChoiceDialog> _present;

    private SeriesInfo? _info;
    private string? _error;
    private int _seasonIndex, _buttonIndex, _episodeIndex;
    private Area _area = Area.Episodes;

    private readonly RemoteImage _backdrop = new(fill: true) { Opacity = 0.16 };
    private readonly RemoteImage _poster = new(fill: true);
    private readonly TextBlock _name = MN.Text("", 30, MN.White, bold: true, maxLines: 2);
    private readonly TextBlock _plot = MN.Text("", 14, MN.Text2, maxLines: 3);
    private readonly Pill _resume = new("");
    private readonly Pill _favorite = new("");
    private readonly StackPanel _seasons = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _seasonScroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Margin = new Thickness(0, 22, 0, 0), Focusable = false,
    };
    private readonly VirtualPanel _episodes = new() { Margin = new Thickness(0, 10, 0, 0), ContentPadding = new Thickness(0, 4, 0, 30) };
    private readonly Spinner _spinner = new(36) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 40, 0, 0) };
    private readonly TextBlock _message = MN.Text("", 15, MN.Text2, maxLines: 3);
    private ChoiceDialog? _dialog;

    private string DisplayName => string.IsNullOrEmpty(_info?.Name) ? _entry.Name : _info!.Name;
    private string? Cover => _entry.Icon ?? _info?.Cover;
    private List<int> SeasonList => _info?.Seasons ?? new List<int>();
    private List<Episode> Episodes =>
        _info != null && _seasonIndex < SeasonList.Count && _info.EpisodesBySeason.TryGetValue(SeasonList[_seasonIndex], out var l) ? l : new List<Episode>();

    private (Progress P, Episode E)? Resume
    {
        get
        {
            if (_model.Store.GetProgress("SERIES:" + _entry.Id) is not { EpisodeId: { } id } p) return null;
            var ep = _info?.AllEpisodes.FirstOrDefault(e => e.Id == id);
            return ep == null ? null : (p, ep);
        }
    }

    private int ButtonCount => Resume == null ? 1 : 2;

    public MediaEntry Entry => _entry;

    public SeriesView(MediaEntry entry, AppModel model, Action<ChoiceDialog> present)
    {
        _entry = entry;
        _model = model;
        _present = present;
        Background = MN.Black;
        ClipToBounds = true;

        Children.Add(_backdrop);

        var layout = MN.Columns("220", "*");
        layout.Margin = new Thickness(48, 70, 48, 0);
        var posterFrame = new Border
        {
            Width = 220, Height = 330, CornerRadius = new CornerRadius(10), Background = MN.Card,
            VerticalAlignment = VerticalAlignment.Top, Child = _poster, ClipToBounds = true,
        };
        posterFrame.Loaded += (_, _) => _poster.Clip = new RectangleGeometry(new Rect(0, 0, 220, 330), 10, 10);
        layout.Children.Add(posterFrame.At(0, 0));

        var details = MN.Rows("auto", "auto", "auto", "auto", "*");
        details.Margin = new Thickness(36, 0, 0, 0);
        details.Children.Add(_name.At(0));
        _plot.Margin = new Thickness(0, 8, 0, 0);
        details.Children.Add(_plot.At(1));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        _resume.Margin = new Thickness(0, 0, 12, 0);
        _resume.Click += () => { if (Resume is { } r) Play(r.E, r.P.Position); };
        _favorite.Click += () => { _model.ToggleFavorite(_entry); Render(); };
        buttons.Children.Add(_resume);
        buttons.Children.Add(_favorite);
        details.Children.Add(buttons.At(2));
        _seasonScroll.Content = _seasons;
        details.Children.Add(_seasonScroll.At(3));
        var body = new Grid();
        body.Children.Add(_episodes);
        body.Children.Add(_spinner);
        _message.Margin = new Thickness(0, 40, 0, 0);
        _message.VerticalAlignment = VerticalAlignment.Top;
        body.Children.Add(_message);
        details.Children.Add(body.At(4));
        layout.Children.Add(details.At(0, 1));
        Children.Add(layout);

        var back = new Pill("Geri", "")
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(48, 20, 0, 0),
        };
        back.Click += () => _model.Back();
        Children.Add(back);

        _episodes.Create = _ => new EpisodeRow();
        _episodes.Update = (i, e) =>
        {
            var eps = Episodes;
            if (e is EpisodeRow row && i < eps.Count)
            {
                row.Tag = i;
                row.Set(eps[i], _model.Store, _area == Area.Episodes && _episodeIndex == i);
            }
        };
        _episodes.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var row = FindRow(e.OriginalSource as DependencyObject);
            if (row?.Tag is not int i || i >= Episodes.Count) return;
            e.Handled = true;
            _area = Area.Episodes;
            _episodeIndex = i;
            Render();
            Choose(Episodes[i]);
        };

        _backdrop.Url = Cover;
        _poster.Url = Cover;
        _name.Text = entry.Name;
        Render();
        Load();
    }

    private static EpisodeRow? FindRow(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is EpisodeRow r) return r;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private async void Load()
    {
        try
        {
            var info = await _model.GetSeriesInfo(_entry.Id);
            _info = info;
            if (info.Seasons.Count == 0)
            {
                _error = "Bu dizide bölüm bulunamadı";
                _area = Area.Buttons;
            }
            else if (_model.Store.GetProgress("SERIES:" + _entry.Id) is { } p)
            {
                var si = info.Seasons.IndexOf(p.Season);
                if (si >= 0)
                {
                    _seasonIndex = si;
                    var ei = info.EpisodesBySeason[p.Season].FindIndex(e => e.Id == p.EpisodeId);
                    if (ei >= 0) _episodeIndex = ei;
                    _area = Area.Buttons;
                    _buttonIndex = 0;
                }
            }
        }
        catch (Exception e)
        {
            _error = "Dizi bilgisi alınamadı: " + AppModel.Describe(e);
            _area = Area.Buttons;
        }
        _backdrop.Url = Cover;
        _poster.Url = Cover;
        RebuildSeasons();
        RebuildEpisodes();
        Render();
        if (_episodeIndex > 0) _episodes.ScrollIntoView(_episodeIndex, center: true);
    }

    public void Refresh()
    {
        Render();
        _episodes.RefreshItems();
    }

    /// Back from the player: put the keyboard on "Devam et" and show the episode to continue.
    public void ReturnedFromPlayer()
    {
        if (_info != null && Resume is { } r)
        {
            var si = SeasonList.IndexOf(r.E.Season);
            if (si >= 0 && si != _seasonIndex) { _seasonIndex = si; RebuildEpisodes(); }
            var ei = Episodes.FindIndex(e => e.Id == r.E.Id);
            if (ei >= 0) _episodeIndex = ei;
            _area = Area.Buttons;
            _buttonIndex = 0;
            _episodes.ScrollIntoView(_episodeIndex, center: true);
        }
        Refresh();
    }

    private void RebuildSeasons()
    {
        _seasons.Children.Clear();
        for (var i = 0; i < SeasonList.Count; i++)
        {
            var index = i;
            var label = MN.Text($"Sezon {SeasonList[i]}", 14);
            label.VerticalAlignment = VerticalAlignment.Center;
            var chip = new Border
            {
                Height = 36, CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(2, 2, 10, 2),
                BorderThickness = new Thickness(2), Child = label, Cursor = Cursors.Hand,
            };
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _seasonIndex = index;
                _episodeIndex = 0;
                _area = Area.Seasons;
                RebuildEpisodes();
                Render();
            };
            _seasons.Children.Add(chip);
        }
    }

    private void RebuildEpisodes()
    {
        _episodes.SetBlocks(new[] { new Block { Count = Episodes.Count, MinItemWidth = 100000, ItemHeight = 62, VSpacing = 6 } });
    }

    private void Render()
    {
        _name.Text = DisplayName;
        _plot.Text = _info?.Plot ?? "";
        _plot.Visibility = string.IsNullOrEmpty(_info?.Plot) ? Visibility.Collapsed : Visibility.Visible;

        if (Resume is { } r)
        {
            _resume.Visibility = Visibility.Visible;
            _resume.Text = r.P.Position > 0
                ? $"Devam et · S{r.E.Season} B{r.E.Number} · {Fmt.Time(r.P.Position)}"
                : $"Sıradaki · S{r.E.Season} B{r.E.Number}";
            _resume.Focused = _area == Area.Buttons && _buttonIndex == 0;
        }
        else
        {
            _resume.Visibility = Visibility.Collapsed;
        }
        if (_buttonIndex >= ButtonCount) _buttonIndex = ButtonCount - 1;
        _favorite.Text = _model.Store.IsFavorite(_entry.Key) ? "Favorilerden çıkar" : "Favorilere ekle";
        _favorite.Focused = _area == Area.Buttons && _buttonIndex == ButtonCount - 1;

        for (var i = 0; i < _seasons.Children.Count; i++)
        {
            if (_seasons.Children[i] is not Border chip || chip.Child is not TextBlock label) continue;
            var focused = _area == Area.Seasons && _seasonIndex == i;
            var current = _seasonIndex == i;
            chip.Background = focused ? MN.Blue : MN.Card;
            chip.BorderBrush = current && !focused ? MN.Blue : MN.Transparent;
            label.Foreground = focused ? MN.White : (current ? MN.Blue : MN.Text2);
            if (focused) chip.BringIntoView();
        }

        var loading = _info == null && _error == null;
        _spinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        _message.Text = _error ?? "";
        _message.Visibility = _error == null ? Visibility.Collapsed : Visibility.Visible;
        _seasonScroll.Visibility = _info != null && _error == null ? Visibility.Visible : Visibility.Collapsed;
        _episodes.Visibility = _seasonScroll.Visibility;
        _episodes.RefreshItems();
    }

    private void Choose(Episode ep)
    {
        var saved = _model.Store.EpisodePosition(ep.Id);
        if (saved > 30 && !_model.Store.IsWatched(ep.Id))
        {
            _dialog = new ChoiceDialog($"S{ep.Season} B{ep.Number} · {ep.Title}", null)
                .Add($"Kaldığın yerden devam et ({Fmt.Time(saved)})", () => Play(ep, saved))
                .Add("Baştan başla", () => Play(ep, 0))
                .Cancel();
            _dialog.Closed += () => _dialog = null;
            _present(_dialog);
        }
        else
        {
            Play(ep, 0);
        }
    }

    private void Play(Episode ep, double start)
    {
        if (_info == null) return;
        var all = _info.AllEpisodes;
        var index = all.FindIndex(e => e.Id == ep.Id);
        if (index < 0) return;
        _model.PlaySeries(_entry.Id, DisplayName, Cover, all, index, start);
    }

    public bool HandleKey(KeyInput k)
    {
        if (_dialog != null) return false;
        var seasons = SeasonList;
        var eps = Episodes;
        var oldSeason = _seasonIndex;
        switch (k.Key)
        {
            case Key.Escape:
            case Key.Back:
                _model.Back();
                return true;
            case Key.Up:
                switch (_area)
                {
                    case Area.Episodes:
                        if (_episodeIndex > 0) _episodeIndex--;
                        else if (seasons.Count > 0) _area = Area.Seasons;
                        else _area = Area.Buttons;
                        break;
                    case Area.Seasons:
                        _area = Area.Buttons;
                        _buttonIndex = 0;
                        break;
                }
                break;
            case Key.Down:
                switch (_area)
                {
                    case Area.Buttons:
                        if (seasons.Count > 0) _area = Area.Seasons;
                        else if (eps.Count > 0) _area = Area.Episodes;
                        break;
                    case Area.Seasons:
                        if (eps.Count > 0) { _area = Area.Episodes; _episodeIndex = 0; }
                        break;
                    case Area.Episodes:
                        if (_episodeIndex + 1 < eps.Count) _episodeIndex++;
                        break;
                }
                break;
            case Key.Left:
                if (_area == Area.Seasons && _seasonIndex > 0) { _seasonIndex--; _episodeIndex = 0; }
                if (_area == Area.Buttons && _buttonIndex > 0) _buttonIndex--;
                break;
            case Key.Right:
                if (_area == Area.Seasons && _seasonIndex + 1 < seasons.Count) { _seasonIndex++; _episodeIndex = 0; }
                if (_area == Area.Buttons && _buttonIndex + 1 < ButtonCount) _buttonIndex++;
                break;
            case Key.Enter:
            case Key.Space:
                if (k.Repeat) return true;
                switch (_area)
                {
                    case Area.Buttons:
                        if (_buttonIndex == ButtonCount - 1) _model.ToggleFavorite(_entry);
                        else if (Resume is { } r) Play(r.E, r.P.Position);
                        break;
                    case Area.Seasons:
                        if (eps.Count > 0) { _area = Area.Episodes; _episodeIndex = 0; }
                        break;
                    case Area.Episodes:
                        if (_episodeIndex < eps.Count) Choose(eps[_episodeIndex]);
                        break;
                }
                break;
            case Key.F:
                if (!k.Repeat) _model.ToggleFavorite(_entry);
                break;
            default:
                return false;
        }
        if (oldSeason != _seasonIndex) RebuildEpisodes();
        Render();
        if (_area == Area.Episodes) _episodes.ScrollIntoView(_episodeIndex);
        return true;
    }
}

public sealed class EpisodeRow : Border
{
    private readonly TextBlock _number = MN.Text("", 15, MN.Blue, bold: true);
    private readonly TextBlock _title = MN.Text("", 15);
    private readonly TextBlock _meta = MN.Text("", 12, MN.Text2);
    private readonly ProgressLine _progress = new() { Margin = new Thickness(0, 4, 0, 0), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _play = MN.Icon("", 13, MN.Muted);
    private bool _hover, _focused;

    public EpisodeRow()
    {
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(2.5);
        Padding = new Thickness(16, 0, 16, 0);
        Cursor = Cursors.Hand;
        var g = MN.Columns("46", "*", "auto");
        _number.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(_number.At(0, 0));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_title);
        texts.Children.Add(_meta);
        texts.Children.Add(_progress);
        g.Children.Add(texts.At(0, 1));
        g.Children.Add(_play.At(0, 2));
        Child = g;
        MouseEnter += (_, _) => { _hover = true; Paint(); };
        MouseLeave += (_, _) => { _hover = false; Paint(); };
    }

    public void Set(Episode ep, Store store, bool focused)
    {
        var watched = store.IsWatched(ep.Id);
        var pos = store.EpisodePosition(ep.Id);
        var meta = new List<string>();
        if (ep.DurationSec > 0) meta.Add($"{(ep.DurationSec + 59) / 60} dk");
        if (watched) meta.Add("İzlendi");
        else if (pos > 0) meta.Add($"Kaldığın yer {Fmt.Time(pos)}");
        _number.Text = $"B{ep.Number}";
        _title.Text = ep.Title;
        _meta.Text = string.Join("  ·  ", meta);
        _meta.Visibility = meta.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var showProgress = !watched && pos > 0 && ep.DurationSec > 0;
        _progress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (showProgress) _progress.Fraction = pos / ep.DurationSec;
        _focused = focused;
        Paint();
    }

    private void Paint()
    {
        Background = _focused || _hover ? MN.CardFocus : MN.Card;
        BorderBrush = _focused ? MN.Blue : MN.Transparent;
        _play.Foreground = _focused ? MN.White : MN.Muted;
    }
}
