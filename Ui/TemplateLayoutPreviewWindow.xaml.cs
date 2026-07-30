using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DcfToSf2.Layout;
using DcfToSf2.Layout.Templates;

namespace DcfToSf2.Ui;

internal partial class TemplateLayoutPreviewWindow : Window
{
    private const double DefaultScale = 0.9;
    private const double MinScale = 0.35;
    private const double MaxScale = 2.7;
    private const double ZoomStep = 1.15;
    private double _scale = DefaultScale;
    private double Scale => _scale;
    private const double MinSlotWidth = 16;
    private const int GridStep = 4;

    private static readonly Brush SelectionStroke = new SolidColorBrush(Color.FromRgb(30, 100, 220));
    private static readonly Brush MarqueeFill = new SolidColorBrush(Color.FromArgb(40, 30, 100, 220));
    private static readonly Brush MarqueeStroke = new SolidColorBrush(Color.FromRgb(30, 100, 220));

    private static int Snap(int value) =>
        (int)(Math.Round(value / (double)GridStep) * GridStep);

    private static int SnapAtLeast(int value, int min) =>
        Math.Max(Snap(min), Snap(value));

    private static readonly Brush[] GroupBrushes =
    [
        new SolidColorBrush(Color.FromRgb(70, 130, 180)),
        new SolidColorBrush(Color.FromRgb(60, 140, 90)),
        new SolidColorBrush(Color.FromRgb(180, 120, 40)),
        new SolidColorBrush(Color.FromRgb(120, 80, 160)),
        new SolidColorBrush(Color.FromRgb(160, 70, 70)),
        new SolidColorBrush(Color.FromRgb(90, 90, 90)),
    ];

    private readonly HashSet<string> _ignoreFields;
    private readonly UiSettings _settings;
    private readonly Dictionary<string, Brush> _groupBrush = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TemplateSlot>> _working = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedIds = new(StringComparer.OrdinalIgnoreCase);

    private enum InteractionMode
    {
        None,
        Marquee,
        DragGroup,
        Resize,
    }

    private InteractionMode _mode;
    private Border? _activeBorder;
    private TemplateSlot? _dragSlot;
    private Rectangle? _marquee;
    private Point _dragStart;
    private readonly Dictionary<string, (int X, int Y)> _dragOrigins = new(StringComparer.OrdinalIgnoreCase);
    private int _origW;
    private bool _dirty;
    private bool _textMode;
    private string _lastRenderedText = string.Empty;
    private bool _modeSwitchGuard;

    private const string TemplateDragFormat = "DcfToSf2.TemplateName";
    private Point _listDragStart;
    private IFieldGroupTemplate? _listDragTemplate;
    private IFieldGroupTemplate? _pendingListSelect;
    private bool _listDragStarted;

    public TemplateLayoutPreviewWindow(IEnumerable<string> ignoreFields, UiSettings? settings = null)
    {
        InitializeComponent();
        // Fresh load so user templates created earlier in this session appear in the list.
        _settings = UiSettings.Load();
        _ = settings;

        _ignoreFields = ignoreFields
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        PreviewCanvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
        PreviewCanvas.MouseMove += Canvas_MouseMove;
        PreviewCanvas.MouseLeftButtonUp += Canvas_MouseLeftButtonUp;
        PreviewCanvas.KeyDown += Canvas_KeyDown;
        PreviewCanvas.Focusable = true;
        UpdateZoomLabel();

        RefreshTemplatesList();
    }

    public void SelectTemplate(string templateName)
    {
        RefreshTemplatesList();
        var item = FieldGroupTemplateRegistry.Default.All.FirstOrDefault(t =>
            t.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase)
        );
        if (item is not null)
            TemplatesList.SelectedItem = item;
        else if (TemplatesList.Items.Count > 0)
            TemplatesList.SelectedIndex = 0;
    }

    private void RefreshTemplatesList()
    {
        TemplatesList.ItemsSource = null;
        TemplatesList.ItemsSource = FieldGroupTemplateRegistry.Default.All;
        if (TemplatesList.SelectedItem is null && TemplatesList.Items.Count > 0)
            TemplatesList.SelectedIndex = 0;
    }

    private IFieldGroupTemplate? SelectedTemplate => TemplatesList.SelectedItem as IFieldGroupTemplate;

    private void TemplatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_textMode && PreviewText.Visibility == Visibility.Visible && PreviewText.Text != _lastRenderedText)
            TryApplyText(showStatus: false);

        _selectedIds.Clear();
        Redraw();
    }

    private void TemplatesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _listDragStart = e.GetPosition(null);
        _listDragTemplate = FindTemplateFromListHit(e.OriginalSource as DependencyObject);
        _listDragStarted = false;
        _pendingListSelect = null;

        if (
            _listDragTemplate is not null
            && SelectedTemplate is { } current
            && !_listDragTemplate.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase)
        )
        {
            // Keep current template selected so canvas stays put while dragging another in.
            _pendingListSelect = _listDragTemplate;
            e.Handled = true;
        }
    }

    private void TemplatesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _listDragTemplate is null || _listDragStarted)
            return;

        var pos = e.GetPosition(null);
        if (
            Math.Abs(pos.X - _listDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _listDragStart.Y) < SystemParameters.MinimumVerticalDragDistance
        )
            return;

        _listDragStarted = true;
        _pendingListSelect = null;
        var data = new DataObject(TemplateDragFormat, _listDragTemplate.Name);
        DragDrop.DoDragDrop(TemplatesList, data, DragDropEffects.Copy);
        _listDragTemplate = null;
        _listDragStarted = false;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_pendingListSelect is not null && !_listDragStarted)
            TemplatesList.SelectedItem = _pendingListSelect;

        _pendingListSelect = null;
        _listDragTemplate = null;
        _listDragStarted = false;
        base.OnPreviewMouseLeftButtonUp(e);
    }

    private static IFieldGroupTemplate? FindTemplateFromListHit(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListBoxItem { DataContext: IFieldGroupTemplate t })
                return t;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void PreviewDropTarget_DragOver(object sender, DragEventArgs e)
    {
        if (
            e.Data.GetDataPresent(TemplateDragFormat)
            && e.Data.GetData(TemplateDragFormat) is string name
            && SelectedTemplate is { } current
            && !name.Equals(current.Name, StringComparison.OrdinalIgnoreCase)
            && FindTemplateByName(name) is not null
        )
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void PreviewDropTarget_Drop(object sender, DragEventArgs e)
    {
        if (
            SelectedTemplate is not { } current
            || e.Data.GetData(TemplateDragFormat) is not string name
            || name.Equals(current.Name, StringComparison.OrdinalIgnoreCase)
            || FindTemplateByName(name) is not { } source
        )
        {
            e.Handled = true;
            return;
        }

        var dropPos = e.GetPosition(PreviewCanvas);
        MergeTemplateAt(current, source, dropPos);
        e.Handled = true;
    }

    private IFieldGroupTemplate? FindTemplateByName(string name) =>
        FieldGroupTemplateRegistry.Default.All.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        );

    private void MergeTemplateAt(IFieldGroupTemplate current, IFieldGroupTemplate source, Point dropPos)
    {
        var target = GetWorkingSlots(current);
        var existingIds = new HashSet<string>(
            target.Where(s => !string.IsNullOrEmpty(s.Id)).Select(s => s.Id),
            StringComparer.OrdinalIgnoreCase
        );

        var sourceSlots = source.GetPreviewSlots(_ignoreFields).Select(s => s.Clone()).ToList();
        if (sourceSlots.Count == 0)
            return;

        int minX = sourceSlots.Min(s => s.X);
        int minY = sourceSlots.Min(s => s.Y);
        int dropX = Snap(Math.Max(0, (int)Math.Round(dropPos.X / Scale)));
        int dropY = Snap(Math.Max(0, (int)Math.Round(dropPos.Y / Scale)));
        int offsetX = dropX - minX;
        int offsetY = dropY - minY;

        string embedHint = NextEmbedHint(existingIds);
        _selectedIds.Clear();

        foreach (var slot in sourceSlots)
        {
            slot.X = Math.Max(0, Snap(slot.X + offsetX));
            slot.Y = Math.Max(0, Snap(slot.Y + offsetY));
            slot.IsEmbedded = true;
            slot.IsRemoved = false;

            if (string.IsNullOrEmpty(slot.Id))
                slot.Id = TemplateSlot.MakeId(slot.Kind, slot.Group, slot.Suffix, slot.StemHint);

            if (existingIds.Contains(slot.Id))
                AssignUniqueEmbeddedId(slot, embedHint, existingIds);

            existingIds.Add(slot.Id);
            target.Add(slot);
            _selectedIds.Add(slot.Id);
        }

        _dirty = true;
        Redraw();
        StatusText.Text = $"Добавлен шаблон «{source.Name}» · {_selectedIds.Count} слотов";
    }

    private static void AssignUniqueEmbeddedId(
        TemplateSlot slot,
        string embedHint,
        HashSet<string> existingIds
    )
    {
        string originalStem = slot.StemHint;
        for (int n = 0; n < 10_000; n++)
        {
            string hint =
                n == 0
                    ? (string.IsNullOrEmpty(originalStem) ? embedHint : $"{originalStem}:{embedHint}")
                    : (string.IsNullOrEmpty(originalStem) ? $"{embedHint}:{n}" : $"{originalStem}:{embedHint}:{n}");
            string id = TemplateSlot.MakeId(slot.Kind, slot.Group, slot.Suffix, hint);
            if (existingIds.Contains(id))
                continue;
            slot.StemHint = hint;
            slot.Id = id;
            return;
        }

        slot.StemHint = $"{embedHint}:{Guid.NewGuid():N}";
        slot.Id = TemplateSlot.MakeId(slot.Kind, slot.Group, slot.Suffix, slot.StemHint);
    }

    private static string NextEmbedHint(HashSet<string> existingIds)
    {
        for (int n = 1; n < 10_000; n++)
        {
            string hint = $"embed:{n}";
            bool used = existingIds.Any(id =>
                id.EndsWith($"@{hint}", StringComparison.OrdinalIgnoreCase)
                || id.Contains($"@{hint}:", StringComparison.OrdinalIgnoreCase)
            );
            if (!used)
                return hint;
        }

        return $"embed:{Guid.NewGuid():N}";
    }

    private List<TemplateSlot> GetWorkingSlots(IFieldGroupTemplate template)
    {
        if (_working.TryGetValue(template.Name, out var list))
            return list;

        list = template.GetPreviewSlots(_ignoreFields).Select(s => s.Clone()).ToList();
        _working[template.Name] = list;
        return list;
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _modeSwitchGuard)
            return;

        bool wantText = ModeText.IsChecked == true;
        if (wantText == _textMode)
            return;

        if (_textMode && !wantText)
        {
            if (!TryApplyText(showStatus: true))
            {
                _modeSwitchGuard = true;
                ModeText.IsChecked = true;
                _modeSwitchGuard = false;
                return;
            }
        }

        _textMode = wantText;
        ApplyModeVisibility();
        if (_textMode)
            RefreshTextFromSlots();
        else
            Redraw();
    }

    private void ApplyModeVisibility()
    {
        CanvasHost.Visibility = _textMode ? Visibility.Collapsed : Visibility.Visible;
        PreviewText.Visibility = _textMode ? Visibility.Visible : Visibility.Collapsed;
        ApplyTextButton.Visibility = _textMode ? Visibility.Visible : Visibility.Collapsed;
        ZoomOutButton.Visibility = _textMode ? Visibility.Collapsed : Visibility.Visible;
        ZoomInButton.Visibility = _textMode ? Visibility.Collapsed : Visibility.Visible;
        ZoomResetButton.Visibility = _textMode ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.IsEnabled = !_textMode;
        RestoreButton.IsEnabled = !_textMode;
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_scale / ZoomStep);

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_scale * ZoomStep);

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(DefaultScale);

    private void CanvasHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_textMode || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;

        SetZoom(e.Delta > 0 ? _scale * ZoomStep : _scale / ZoomStep);
        e.Handled = true;
    }

    private void SetZoom(double scale)
    {
        double next = Math.Clamp(scale, MinScale, MaxScale);
        if (Math.Abs(next - _scale) < 0.001)
        {
            UpdateZoomLabel();
            return;
        }

        _scale = next;
        UpdateZoomLabel();
        if (!_textMode)
            Redraw();
    }

    private void UpdateZoomLabel()
    {
        int pct = (int)Math.Round(_scale / DefaultScale * 100);
        ZoomResetButton.Content = $"{pct}%";
    }

    private void RefreshTextFromSlots()
    {
        if (SelectedTemplate is not { } template)
        {
            PreviewText.Text = string.Empty;
            _lastRenderedText = string.Empty;
            return;
        }

        var slots = GetWorkingSlots(template);
        _lastRenderedText = TemplatePreviewText.Render(slots);
        PreviewText.Text = _lastRenderedText;
        UpdateStatus(template, slots.Count);
    }

    private void ApplyText_Click(object sender, RoutedEventArgs e) => TryApplyText(showStatus: true);

    private bool TryApplyText(bool showStatus)
    {
        if (SelectedTemplate is not { } template)
            return true;

        string text = PreviewText.Text ?? string.Empty;
        if (text == _lastRenderedText)
            return true;

        var slots = GetWorkingSlots(template);
        var result = TemplatePreviewText.ApplyToSlots(text, slots);
        _dirty = true;
        _lastRenderedText = TemplatePreviewText.Render(slots);
        PreviewText.Text = _lastRenderedText;

        if (showStatus)
        {
            string extra =
                result.Unmatched > 0
                    ? $" · не сопоставлено {result.Unmatched}"
                        + (result.Messages.Count > 0 ? $": {result.Messages[0]}" : "")
                    : "";
            StatusText.Text = $"Текст применён · обновлено {result.Updated}{extra}";
        }

        return true;
    }

    private void Redraw()
    {
        if (_textMode)
        {
            RefreshTextFromSlots();
            return;
        }

        PreviewCanvas.Children.Clear();
        _activeBorder = null;
        _dragSlot = null;
        _marquee = null;
        _mode = InteractionMode.None;
        _dragOrigins.Clear();

        if (SelectedTemplate is not { } template)
            return;

        var slots = GetWorkingSlots(template);
        if (slots.Count == 0)
            return;

        double maxX = slots.Max(s => s.X + s.Width) * Scale + 40;
        double maxY = slots.Max(s => s.Y + s.Height) * Scale + 40;
        PreviewCanvas.Width = Math.Max(800, maxX);
        PreviewCanvas.Height = Math.Max(400, maxY);

        foreach (var slot in slots.OrderBy(s => s.Kind switch
                 {
                     TemplateSlotKind.Text => 0,
                     TemplateSlotKind.Pict => 2,
                     _ => 1,
                 }))
            AddSlotVisual(slot);

        UpdateStatus(template, slots.Count);
    }

    private void UpdateStatus(IFieldGroupTemplate template, int count)
    {
        var slots = GetWorkingSlots(template);
        int removed = slots.Count(s => s.IsRemoved);
        string sel = _selectedIds.Count > 0 ? $" · выбрано {_selectedIds.Count}" : "";
        string rem = removed > 0 ? $" · удалено {removed}" : "";
        StatusText.Text = _dirty
            ? $"Правки не сохранены · {template.Name} · {count} слотов{rem}{sel}"
            : $"{template.Name} · {count} слотов{rem}{sel}"
              + (TemplateLayoutStore.IsReplaceBase(template.Name)
                  ? " · база заменена"
                  : TemplateLayoutStore.HasOverrides(template.Name)
                      ? " · есть сохранённая раскладка"
                      : "");
    }

    private bool IsSelected(TemplateSlot slot) =>
        !string.IsNullOrEmpty(slot.Id) && _selectedIds.Contains(slot.Id);

    private void AddSlotVisual(TemplateSlot slot)
    {
        bool selected = IsSelected(slot);
        bool excluded = slot.IsExcluded;
        bool isPict = slot.Kind == TemplateSlotKind.Pict;
        var brush = slot.Kind switch
        {
            TemplateSlotKind.Text => Brushes.DimGray,
            TemplateSlotKind.Pict => Brushes.DarkSlateGray,
            _ => BrushFor(slot.Group),
        };
        var border = new Border
        {
            Width = Math.Max(8, slot.Width * Scale),
            Height = Math.Max(isPict ? 8 : 10, slot.Height * Scale),
            BorderBrush = excluded ? Brushes.Gray : selected ? SelectionStroke : brush,
            BorderThickness = new Thickness(selected ? 2.5 : isPict ? 1 : slot.Kind == TemplateSlotKind.Text ? 1 : 1.5),
            Background =
                excluded ? new SolidColorBrush(Color.FromArgb(40, 128, 128, 128))
                : selected ? new SolidColorBrush(Color.FromArgb(70, 30, 100, 220))
                : isPict ? new SolidColorBrush(Color.FromArgb(120, 40, 40, 40))
                : slot.Kind == TemplateSlotKind.Text
                    ? new SolidColorBrush(Color.FromArgb(55, 210, 210, 210))
                    : new SolidColorBrush(
                        Color.FromArgb(
                            50,
                            ((SolidColorBrush)brush).Color.R,
                            ((SolidColorBrush)brush).Color.G,
                            ((SolidColorBrush)brush).Color.B
                        )
                    ),
            Opacity = excluded ? 0.45 : 1,
            Cursor = slot.IsIgnored ? Cursors.Arrow : Cursors.SizeAll,
            Tag = slot,
            ToolTip =
                isPict
                    ? $"Pict: {slot.Label}\n{slot.X},{slot.Y} {slot.Width}×{slot.Height}"
                        + (slot.IsRemoved ? "\n(удалено из шаблона)" : "")
                    : slot.Kind == TemplateSlotKind.Text
                        ? $"Text: {slot.Label}\n{slot.X},{slot.Y} {slot.Width}×{slot.Height}"
                            + (slot.IsRemoved ? "\n(удалено из шаблона)" : "")
                        : $"{slot.Suffix}\n{slot.X},{slot.Y} {slot.Width}×{slot.Height}"
                            + (slot.IsRemoved ? "\n(удалено из шаблона)" : "")
                            + (slot.IsIgnored ? "\n(игнор. поле)" : ""),
        };

        var label = new TextBlock
        {
            Text =
                isPict
                    ? (excluded ? "(P)" : "P")
                    : slot.Kind == TemplateSlotKind.Text
                        ? (excluded ? $"({slot.Label})" : slot.Label)
                        : (excluded ? $"({slot.Suffix})" : slot.Suffix),
            FontSize = isPict ? 8 : 10,
            FontStyle = slot.Kind == TemplateSlotKind.Text ? FontStyles.Italic : FontStyles.Normal,
            Foreground = excluded ? Brushes.Gray : isPict ? Brushes.White : Brushes.Black,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(isPict ? 0 : 2, 1, isPict ? 0 : 8, 1),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = isPict ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };

        if (slot.IsIgnored)
        {
            border.Child = label;
        }
        else if (slot.IsRemoved)
        {
            border.Child = label;
            border.MouseLeftButtonDown += RemovedSlot_MouseLeftButtonDown;
        }
        else if (isPict)
        {
            border.Child = label;
            border.MouseLeftButtonDown += Slot_MouseLeftButtonDown;
        }
        else
        {
            var grid = new Grid();
            grid.Children.Add(label);
            var grip = new Border
            {
                Width = 7,
                Background = new SolidColorBrush(Color.FromArgb(140, 30, 30, 30)),
                Cursor = Cursors.SizeWE,
                HorizontalAlignment = HorizontalAlignment.Right,
                Tag = slot,
            };
            grid.Children.Add(grip);
            border.Child = grid;

            border.MouseLeftButtonDown += Slot_MouseLeftButtonDown;
            grip.MouseLeftButtonDown += Grip_MouseLeftButtonDown;
        }

        Canvas.SetLeft(border, slot.X * Scale);
        Canvas.SetTop(border, slot.Y * Scale);
        PreviewCanvas.Children.Add(border);

        if (excluded)
        {
            var line = new Line
            {
                X1 = slot.X * Scale,
                Y1 = slot.Y * Scale,
                X2 = (slot.X + slot.Width) * Scale,
                Y2 = (slot.Y + slot.Height) * Scale,
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            PreviewCanvas.Children.Add(line);
        }
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Empty canvas / background → marquee (or clear if click without drag)
        if (e.OriginalSource is not Canvas && e.Source is not Canvas)
            return;

        PreviewCanvas.Focus();
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!ctrl)
            _selectedIds.Clear();

        _mode = InteractionMode.Marquee;
        _dragStart = e.GetPosition(PreviewCanvas);
        _marquee = new Rectangle
        {
            Stroke = MarqueeStroke,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection([3, 2]),
            Fill = MarqueeFill,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_marquee, _dragStart.X);
        Canvas.SetTop(_marquee, _dragStart.Y);
        PreviewCanvas.Children.Add(_marquee);
        PreviewCanvas.CaptureMouse();
        e.Handled = true;
        RefreshSelectionVisuals();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_mode == InteractionMode.None || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(PreviewCanvas);

        if (_mode == InteractionMode.Marquee && _marquee is not null)
        {
            double x = Math.Min(pos.X, _dragStart.X);
            double y = Math.Min(pos.Y, _dragStart.Y);
            double w = Math.Abs(pos.X - _dragStart.X);
            double h = Math.Abs(pos.Y - _dragStart.Y);
            Canvas.SetLeft(_marquee, x);
            Canvas.SetTop(_marquee, y);
            _marquee.Width = w;
            _marquee.Height = h;
            e.Handled = true;
            return;
        }

        if (_mode == InteractionMode.DragGroup)
        {
            double dx = (pos.X - _dragStart.X) / Scale;
            double dy = (pos.Y - _dragStart.Y) / Scale;
            int snapDx = Snap((int)Math.Round(dx));
            int snapDy = Snap((int)Math.Round(dy));

            // Keep group within non-negative coords
            int minOrigX = _dragOrigins.Values.Min(o => o.X);
            int minOrigY = _dragOrigins.Values.Min(o => o.Y);
            if (minOrigX + snapDx < 0)
                snapDx = Snap(-minOrigX);
            if (minOrigY + snapDy < 0)
                snapDy = Snap(-minOrigY);

            foreach (var (id, origin) in _dragOrigins)
            {
                var slot = FindSlotById(id);
                var border = slot is null ? null : FindBorder(slot);
                if (slot is null || border is null)
                    continue;
                slot.X = origin.X + snapDx;
                slot.Y = origin.Y + snapDy;
                Canvas.SetLeft(border, slot.X * Scale);
                Canvas.SetTop(border, slot.Y * Scale);
            }

            _dirty = true;
            if (SelectedTemplate is { } t)
                UpdateStatus(t, GetWorkingSlots(t).Count);
            e.Handled = true;
            return;
        }

        if (_mode == InteractionMode.Resize && _dragSlot is not null && _activeBorder is not null)
        {
            double dx = (pos.X - _dragStart.X) / Scale;
            _dragSlot.Width = SnapAtLeast((int)Math.Round(_origW + dx), (int)MinSlotWidth);
            _activeBorder.Width = Math.Max(8, _dragSlot.Width * Scale);
            _dirty = true;
            if (SelectedTemplate is { } t)
                UpdateStatus(t, GetWorkingSlots(t).Count);
            e.Handled = true;
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == InteractionMode.Marquee)
            FinishMarquee(e.GetPosition(PreviewCanvas));
        else if (_mode is InteractionMode.DragGroup or InteractionMode.Resize)
            FinishMoveOrResize();

        if (PreviewCanvas.IsMouseCaptured)
            PreviewCanvas.ReleaseMouseCapture();
        _mode = InteractionMode.None;
        e.Handled = true;
    }

    private void FinishMarquee(Point end)
    {
        if (_marquee is not null)
        {
            PreviewCanvas.Children.Remove(_marquee);
            _marquee = null;
        }

        double x1 = Math.Min(_dragStart.X, end.X);
        double y1 = Math.Min(_dragStart.Y, end.Y);
        double x2 = Math.Max(_dragStart.X, end.X);
        double y2 = Math.Max(_dragStart.Y, end.Y);

        if (x2 - x1 < 3 && y2 - y1 < 3)
        {
            RefreshSelectionVisuals();
            return;
        }

        if (SelectedTemplate is not { } template)
            return;

        var rect = new Rect(x1, y1, x2 - x1, y2 - y1);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var hit = GetWorkingSlots(template)
            .Where(s =>
                !s.IsExcluded
                && !string.IsNullOrEmpty(s.Id)
                && rect.IntersectsWith(new Rect(s.X * Scale, s.Y * Scale, s.Width * Scale, s.Height * Scale))
            )
            .Select(s => s.Id)
            .ToList();

        if (!ctrl)
            _selectedIds.Clear();
        foreach (var id in hit)
            _selectedIds.Add(id);

        RefreshSelectionVisuals();
    }

    private void FinishMoveOrResize()
    {
        if (_activeBorder is not null && _dragSlot is not null)
            _activeBorder.ToolTip = SlotToolTip(_dragSlot);

        if (SelectedTemplate is { } template)
        {
            var slots = GetWorkingSlots(template);
            PreviewCanvas.Width = Math.Max(800, slots.Max(s => s.X + s.Width) * Scale + 40);
            PreviewCanvas.Height = Math.Max(400, slots.Max(s => s.Y + s.Height) * Scale + 40);
            foreach (var id in _selectedIds)
            {
                var slot = FindSlotById(id);
                var border = slot is null ? null : FindBorder(slot);
                if (slot is null || border is null)
                    continue;
                border.ToolTip = SlotToolTip(slot);
            }
        }

        _activeBorder = null;
        _dragSlot = null;
        _dragOrigins.Clear();
    }

    private static string SlotToolTip(TemplateSlot slot) =>
        slot.Kind switch
        {
            TemplateSlotKind.Text =>
                $"Text: {slot.Label}\n{slot.X},{slot.Y} {slot.Width}×{slot.Height}"
                    + (slot.IsRemoved ? "\n(удалено из шаблона)" : ""),
            TemplateSlotKind.Pict =>
                $"Pict: {slot.Label}\n{slot.X},{slot.Y} {slot.Width}×{slot.Height}"
                    + (slot.IsRemoved ? "\n(удалено из шаблона)" : ""),
            _ =>
                $"{slot.Suffix}\n{slot.X},{slot.Y} {slot.Width}×{slot.Height}"
                    + (slot.IsRemoved ? "\n(удалено из шаблона)" : "")
                    + (slot.IsIgnored ? "\n(игнор. поле)" : ""),
        };

    private void RefreshSelectionVisuals()
    {
        foreach (var border in PreviewCanvas.Children.OfType<Border>())
        {
            if (border.Tag is not TemplateSlot slot || slot.IsIgnored)
                continue;
            bool selected = IsSelected(slot);
            bool excluded = slot.IsExcluded;
            bool isPict = slot.Kind == TemplateSlotKind.Pict;
            var brush = slot.Kind switch
            {
                TemplateSlotKind.Text => Brushes.DimGray,
                TemplateSlotKind.Pict => Brushes.DarkSlateGray,
                _ => BrushFor(slot.Group),
            };
            border.BorderBrush = excluded ? Brushes.Gray : selected ? SelectionStroke : brush;
            border.BorderThickness = new Thickness(
                selected ? 2.5 : isPict || slot.Kind == TemplateSlotKind.Text ? 1 : 1.5
            );
            border.Background = excluded
                ? new SolidColorBrush(Color.FromArgb(40, 128, 128, 128))
                : selected
                    ? new SolidColorBrush(Color.FromArgb(70, 30, 100, 220))
                    : isPict
                        ? new SolidColorBrush(Color.FromArgb(120, 40, 40, 40))
                        : slot.Kind == TemplateSlotKind.Text
                            ? new SolidColorBrush(Color.FromArgb(55, 210, 210, 210))
                            : new SolidColorBrush(
                                Color.FromArgb(
                                    50,
                                    ((SolidColorBrush)brush).Color.R,
                                    ((SolidColorBrush)brush).Color.G,
                                    ((SolidColorBrush)brush).Color.B
                                )
                            );
        }

        if (SelectedTemplate is { } t)
            UpdateStatus(t, GetWorkingSlots(t).Count);
    }

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TemplateSlot slot })
            return;
        if (FindBorder(slot) is not { } border)
            return;

        EnsureSelectedForEdit(slot, addOnly: false);
        _mode = InteractionMode.Resize;
        _activeBorder = border;
        _dragSlot = slot;
        _dragStart = e.GetPosition(PreviewCanvas);
        _origW = slot.Width;
        PreviewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Slot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Cursor: var c } && Equals(c, Cursors.SizeWE))
            return;
        if (sender is not Border { Tag: TemplateSlot slot } || slot.IsIgnored)
            return;

        PreviewCanvas.Focus();
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl)
        {
            if (IsSelected(slot))
                _selectedIds.Remove(slot.Id);
            else
                _selectedIds.Add(slot.Id);
            RefreshSelectionVisuals();
            e.Handled = true;
            return;
        }

        if (!IsSelected(slot))
        {
            _selectedIds.Clear();
            _selectedIds.Add(slot.Id);
            RefreshSelectionVisuals();
        }

        // Drag whole selection
        _mode = InteractionMode.DragGroup;
        _dragSlot = slot;
        _activeBorder = sender as Border;
        _dragStart = e.GetPosition(PreviewCanvas);
        _dragOrigins.Clear();
        if (SelectedTemplate is { } template)
        {
            foreach (var s in GetWorkingSlots(template))
            {
                if (IsSelected(s))
                    _dragOrigins[s.Id] = (s.X, s.Y);
            }
        }

        PreviewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void EnsureSelectedForEdit(TemplateSlot slot, bool addOnly)
    {
        if (string.IsNullOrEmpty(slot.Id))
            return;
        if (!IsSelected(slot))
        {
            if (!addOnly)
                _selectedIds.Clear();
            _selectedIds.Add(slot.Id);
            RefreshSelectionVisuals();
        }
    }

    private void RemovedSlot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: TemplateSlot slot } || !slot.IsRemoved)
            return;

        PreviewCanvas.Focus();
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!ctrl)
            _selectedIds.Clear();
        if (IsSelected(slot))
            _selectedIds.Remove(slot.Id);
        else
            _selectedIds.Add(slot.Id);
        RefreshSelectionVisuals();
        e.Handled = true;
    }

    private void Canvas_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _selectedIds.Clear();
            RefreshSelectionVisuals();
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            DeleteSelectedSlots();
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && SelectedTemplate is { } t)
        {
            _selectedIds.Clear();
            foreach (var s in GetWorkingSlots(t).Where(s => !s.IsExcluded && !string.IsNullOrEmpty(s.Id)))
                _selectedIds.Add(s.Id);
            RefreshSelectionVisuals();
            e.Handled = true;
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelectedSlots();

    private void Restore_Click(object sender, RoutedEventArgs e) => RestoreSelectedSlots();

    private void DeleteSelectedSlots()
    {
        if (SelectedTemplate is not { } template || _selectedIds.Count == 0)
            return;

        var slots = GetWorkingSlots(template);
        bool any = false;
        foreach (var slot in slots.Where(s => IsSelected(s) && !s.IsIgnored))
        {
            if (!slot.IsRemoved)
            {
                slot.IsRemoved = true;
                any = true;
            }
        }

        if (!any)
            return;

        _dirty = true;
        _selectedIds.Clear();
        Redraw();
    }

    private void RestoreSelectedSlots()
    {
        if (SelectedTemplate is not { } template || _selectedIds.Count == 0)
            return;

        var slots = GetWorkingSlots(template);
        bool any = false;
        foreach (var slot in slots.Where(s => IsSelected(s) && s.IsRemoved))
        {
            slot.IsRemoved = false;
            any = true;
        }

        if (!any)
            return;

        _dirty = true;
        Redraw();
    }

    private TemplateSlot? FindSlotById(string id)
    {
        if (SelectedTemplate is not { } template)
            return null;
        return GetWorkingSlots(template).FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private Border? FindBorder(TemplateSlot slot) =>
        PreviewCanvas.Children.OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.Tag, slot));

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate is null)
            return;

        if (_textMode)
            TryApplyText(showStatus: false);

        if (!TryPersistWorking(askReplaceBase: true))
            return;

        _dirty = false;
        StatusText.Text =
            $"Сохранено ({_working.Count} шабл.)"
            + (TemplateLayoutStore.IsReplaceBase(SelectedTemplate.Name) ? " · база заменена" : "");
        Redraw();
    }

    /// <returns>false if user cancelled.</returns>
    private bool TryPersistWorking(bool askReplaceBase)
    {
        bool replaceBase = false;
        if (askReplaceBase)
        {
            var choice = MessageBox.Show(
                this,
                "Заменить базовый шаблон?\n\n"
                    + "Да — удалённые поля уберутся навсегда, текущая раскладка станет новой базой.\n"
                    + "Нет — сохранить как переопределение (удалённые можно вернуть).\n"
                    + "Отмена — не сохранять.",
                "Сохранение шаблонов",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question
            );
            if (choice == MessageBoxResult.Cancel)
                return false;
            replaceBase = choice == MessageBoxResult.Yes;
        }

        if (replaceBase)
        {
            foreach (var name in _working.Keys.ToList())
            {
                var list = _working[name];
                list.RemoveAll(s => s.IsRemoved);
            }
        }

        foreach (var (name, slots) in _working)
            TemplateLayoutStore.SaveTemplate(name, slots, replaceBase);

        _settings.Save();
        return true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate is not { } template)
            return;

        TemplateLayoutStore.ClearTemplate(template.Name);
        _working.Remove(template.Name);
        _selectedIds.Clear();
        _settings.Save();
        _dirty = false;
        Redraw();
        StatusText.Text = $"Сброшено: {template.Name}";
    }

    private Brush BrushFor(string group)
    {
        if (string.IsNullOrEmpty(group))
            group = "?";
        if (_groupBrush.TryGetValue(group, out var b))
            return b;
        b = GroupBrushes[_groupBrush.Count % GroupBrushes.Length];
        _groupBrush[group] = b;
        return b;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_textMode && PreviewText.Text != _lastRenderedText)
            TryApplyText(showStatus: false);

        if (_dirty)
        {
            var r = MessageBox.Show(
                this,
                "Есть несохранённые изменения раскладки. Сохранить?",
                "Шаблоны",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question
            );
            if (r == MessageBoxResult.Cancel)
                return;
            if (r == MessageBoxResult.Yes)
            {
                if (!TryPersistWorking(askReplaceBase: true))
                    return;
            }
        }

        Close();
    }
}
