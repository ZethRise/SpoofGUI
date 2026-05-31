using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SpoofGUI.Core;
using SpoofGUI.GUI.ViewModels;
using SpoofGUI.Models;
using Windows.Foundation;

namespace SpoofGUI.GUI.Pages;

public sealed partial class ConfigPage : Page
{
    private readonly ConfigPageViewModel _vm;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private SpoofProfile? _selected;
    private SpoofProfile? _highlighted;
    private bool _suppressSelection;
    private bool _fetching;

    public ConfigPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<ConfigPageViewModel>();
        Loaded += (_, _) => ReloadList();
    }

    private void ReloadList(long? selectId = null)
    {
        var profiles = _vm.All();
        var target = selectId is long id
            ? profiles.FirstOrDefault(p => p.Id == id)
            : profiles.FirstOrDefault(p => p.Id == _selected?.Id) ?? profiles.FirstOrDefault();

        _suppressSelection = true;
        ProfileList.ItemsSource = profiles;
        ProfileList.SelectedItem = target;
        _suppressSelection = false;

        _selected = target;
        _highlighted = null;
        LoadEditor(target);
        UpdateHeader(profiles.Count);
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => HighlightSelection(pop: false));
    }

    private void UpdateHeader(int count)
    {
        CountText.Text = $"{count:D2} / {ConfigPageViewModel.MaxProfiles}";
        AddButton.IsEnabled = count < ConfigPageViewModel.MaxProfiles;
        DeleteButton.IsEnabled = count > 1 && _selected is not null;
    }

    private void LoadEditor(SpoofProfile? p)
    {
        var has = p is not null;
        NameBox.IsEnabled = has;
        ListenHost.IsEnabled = has;
        ListenPort.IsEnabled = has;
        ConnectIp.IsEnabled = has;
        ConnectPort.IsEnabled = has;
        FakeSni.IsEnabled = has;
        SaveButton.IsEnabled = has;
        RevertButton.IsEnabled = has;
        SetActiveButton.IsEnabled = has && !p!.IsActive;

        if (!has)
        {
            EditorTitle.Text = "no profile";
            EditorActiveBadge.Visibility = Visibility.Collapsed;
            return;
        }

        EditorTitle.Text = p!.Name;
        NameBox.Text = p.Name;
        ListenHost.Text = p.ListenHost;
        ListenPort.Text = p.ListenPort.ToString();
        ConnectIp.Text = p.ConnectIp;
        ConnectPort.Text = p.ConnectPort.ToString();
        FakeSni.Text = p.FakeSni;
        EditorActiveBadge.Visibility = p.IsActive ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = "";
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        _selected = ProfileList.SelectedItem as SpoofProfile;
        LoadEditor(_selected);
        DeleteButton.IsEnabled = _vm.Count() > 1 && _selected is not null;
        HighlightSelection(pop: true);
    }

    private void HighlightSelection(bool pop)
    {
        if (_highlighted is not null && !ReferenceEquals(_highlighted, _selected)
            && ProfileList.ContainerFromItem(_highlighted) is FrameworkElement previous
            && FindByTag<Border>(previous, "ring") is Border previousRing)
        {
            FadeOpacity(previousRing, previousRing.Opacity, 0, 140);
        }

        _highlighted = _selected;
        if (_selected is null) return;
        if (ProfileList.ContainerFromItem(_selected) is not FrameworkElement container) return;

        if (FindByTag<Border>(container, "ring") is Border ring)
            FadeOpacity(ring, ring.Opacity, 1, 200);
        if (pop && FindByTag<Border>(container, "card") is Border card)
            PopCard(card);
    }

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && (element.Tag as string) == tag) return element;
            if (FindByTag<T>(child, tag) is T found) return found;
        }

        return null;
    }

    private static void FadeOpacity(UIElement target, double from, double to, int milliseconds)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private static void PopCard(Border card)
    {
        if (card.RenderTransform is not ScaleTransform scale)
        {
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            scale = new ScaleTransform();
            card.RenderTransform = scale;
        }

        var storyboard = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(120),
                Value = 1.035,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(300),
                Value = 1.0,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            Storyboard.SetTarget(animation, scale);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        storyboard.Begin();
    }

    private async void OnFetchSniList(object sender, object e)
    {
        if (_fetching) return;
        _fetching = true;
        SetFetching(true);
        try
        {
            var entries = await _vm.FetchSniListAsync();
            SetFetching(false);
            if (entries.Count == 0)
            {
                StatusText.Text = "sni.json is empty";
                return;
            }

            await ShowSniDialogAsync(entries);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"fetch failed: {ex.Message}";
        }
        finally
        {
            _fetching = false;
            SetFetching(false);
        }
    }

    private void SetFetching(bool on)
    {
        FetchSniButton.IsEnabled = !on;
        FetchLabel.Text = on ? "fetching…" : "fetch from repo";
        FetchIcon.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        FetchSpinner.IsActive = on;
        FetchSpinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ShowSniDialogAsync(IReadOnlyList<SniListEntry> entries)
    {
        var rows = entries.Select(en => $"{en.Sni}   →   {en.Ip}:{en.Port}").ToList();
        var list = new ListView
        {
            ItemsSource = rows,
            SelectionMode = ListViewSelectionMode.None,
            MaxHeight = 360,
        };

        var freeSlots = Math.Max(0, ConfigPageViewModel.MaxProfiles - _vm.Count());
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{entries.Count} entries · {freeSlots} free slot(s) of {ConfigPageViewModel.MaxProfiles}",
            Style = (Style)Application.Current.Resources["TextCaption"],
        });
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            Title = "SNI list from repo",
            Content = panel,
            PrimaryButtonText = $"add all ({entries.Count})",
            CloseButtonText = "cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var (added, skipped) = _vm.AddFromEntries(entries);
        ReloadList();
        StatusText.Text = skipped > 0
            ? $"added {added}, skipped {skipped} (limit {ConfigPageViewModel.MaxProfiles})"
            : $"added {added} profile(s)";
    }

    private void OnAddProfile(object sender, object e)
    {
        if (!_vm.CanAdd)
        {
            StatusText.Text = $"limit reached — max {ConfigPageViewModel.MaxProfiles} profiles";
            return;
        }

        var draft = _vm.NewDraft();
        var id = _vm.Save(draft);
        ReloadList(id);
        StatusText.Text = $"added: {draft.Name}";
    }

    private void OnSave(object sender, object e)
    {
        if (_selected is null) return;

        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            StatusText.Text = "name cannot be empty";
            return;
        }
        if (_vm.NameIsTaken(name, _selected.Id))
        {
            StatusText.Text = $"name already in use: {name}";
            return;
        }
        if (!int.TryParse(ListenPort.Text, out var lp) || lp <= 0 || lp > 65535)
        {
            StatusText.Text = "invalid listen port";
            return;
        }
        if (!int.TryParse(ConnectPort.Text, out var cp) || cp <= 0 || cp > 65535)
        {
            StatusText.Text = "invalid connect port";
            return;
        }

        _selected.Name = name;
        _selected.ListenHost = ListenHost.Text.Trim();
        _selected.ListenPort = lp;
        _selected.ConnectIp = ConnectIp.Text.Trim();
        _selected.ConnectPort = cp;
        _selected.FakeSni = FakeSni.Text.Trim();

        try
        {
            var id = _vm.Save(_selected);
            _selected.Id = id;
            ReloadList(id);
            StatusText.Text = $"saved: {name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"save failed: {ex.Message}";
        }
    }

    private void OnSetActive(object sender, object e)
    {
        if (_selected is null || _selected.Id == 0 || _selected.IsActive) return;
        _vm.SetActive(_selected.Id);

        if (ProfileList.ItemsSource is IReadOnlyList<SpoofProfile> items)
        {
            foreach (var item in items) item.IsActive = item.Id == _selected.Id;
        }

        LoadEditor(_selected);
        if (ProfileList.ContainerFromItem(_selected) is FrameworkElement container
            && FindByTag<Border>(container, "card") is Border card)
        {
            PopCard(card);
        }
        StatusText.Text = $"active: {_selected.Name}";
    }

    private void OnRevert(object sender, object e) => ReloadList(_selected?.Id);

    private void OnDelete(object sender, object e)
    {
        if (_selected is null || _selected.Id == 0) return;
        if (_vm.Count() <= 1)
        {
            StatusText.Text = "keep at least one profile";
            return;
        }

        var name = _selected.Name;
        _vm.Delete(_selected.Id);
        _selected = null;
        ReloadList();
        StatusText.Text = $"deleted: {name}";
    }
}
