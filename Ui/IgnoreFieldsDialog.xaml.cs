using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DcfToSf2.Layout.Templates;
using DcfToSf2.Parsing;

namespace DcfToSf2.Ui;

internal partial class IgnoreFieldsDialog : Window
{
    private readonly DcfParseResult _parsed;
    private readonly List<IgnoreBlockNode> _blocks;
    private readonly HashSet<string> _manualChecked;
    private bool _suppressBlockEvents;
    private bool _ready;

    public HashSet<string> SelectedFields { get; private set; }

    public IgnoreFieldsDialog(DcfParseResult parsed, IEnumerable<string>? previouslySelected)
    {
        InitializeComponent();

        _parsed = parsed;

        SelectedFields = previouslySelected is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(previouslySelected, StringComparer.OrdinalIgnoreCase);

        _manualChecked = SelectedFields
            .Where(s => !DcfParser.IsIgnorePattern(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _blocks = IgnoreFieldsTreeBuilder.Build(parsed, _manualChecked);
        FieldsTree.ItemsSource = _blocks;

        var patterns = SelectedFields
            .Where(DcfParser.IsIgnorePattern)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        PatternsBox.Text = string.Join(", ", patterns);

        _ready = true;
        SyncTreeWithPatterns();
        UpdateSummary();
    }

    private void TreeNode_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject d)
            return;
        var item = FindAncestor<TreeViewItem>(d);
        if (item is not null)
            item.IsSelected = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private IgnoreBlockNode? ResolveSelectedBlock()
    {
        return FieldsTree.SelectedItem switch
        {
            IgnoreBlockNode block => block,
            IgnoreFieldNode field => field.Parent,
            _ => null,
        };
    }

    private void CreateTemplate_Click(object sender, RoutedEventArgs e)
    {
        var block = ResolveSelectedBlock();
        if (block is null)
        {
            MessageBox.Show(
                this,
                "Выделите ветку (блок) в дереве документа.",
                "Создать шаблон",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        if (block.AllFields().Any() == false)
        {
            MessageBox.Show(
                this,
                "В выбранной ветке нет полей.",
                "Создать шаблон",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        string defaultName = block.IsSection
            ? block.SectionKey!
            : IgnoreBlockNode.LeafName(block.Path);
        if (!TryPromptTemplateName(defaultName, out string templateName))
            return;

        var byName = _parsed.Fields
            .Where(f =>
                string.Equals(
                    string.IsNullOrWhiteSpace(f.Path) ? Consts.DefaultBlockName : f.Path,
                    block.Path,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .GroupBy(f => f.FieldElement.ElementName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var fieldTuples = block.AllFields()
            .Select(f =>
            {
                byName.TryGetValue(f.Name, out var data);
                string value = data?.FieldElement.ElementValue ?? "C50 |";
                return (f.Name, f.Label, value);
            })
            .ToList();

        var def = UserDefinedFieldGroupTemplate.CreateFromBranch(
            block.Path,
            block.Title,
            fieldTuples,
            templateName
        );
        if (def.Fields.Count == 0)
        {
            MessageBox.Show(
                this,
                "Не удалось собрать поля для шаблона.",
                "Создать шаблон",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        var created = UserTemplateStore.Add(def);
        // Persist without Load() — Load would wipe the just-added template from memory.
        UiSettings.PersistStores();

        MessageBox.Show(
            this,
            $"Шаблон «{created.Name}» создан из ветки «{block.Path}» ({def.Fields.Count} полей).\n"
                + "Откройте «Шаблоны…» в главном окне, чтобы отредактировать раскладку.",
            "Создать шаблон",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private bool TryPromptTemplateName(string defaultName, out string name)
    {
        name = defaultName;
        var dlg = new Window
        {
            Title = "Имя шаблона",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
        };
        var panel = new StackPanel { Margin = new Thickness(12), Width = 360 };
        panel.Children.Add(
            new TextBlock
            {
                Text = "Имя нового шаблона раскладки:",
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
            }
        );
        var box = new TextBox
        {
            Text = defaultName,
            Margin = new Thickness(0, 0, 0, 12),
            MinWidth = 320,
        };
        panel.Children.Add(box);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            Width = 88,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 4, 12, 4),
        };
        var cancel = new Button
        {
            Content = "Отмена",
            IsCancel = true,
            Width = 88,
            Padding = new Thickness(12, 4, 12, 4),
        };
        bool accepted = false;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                MessageBox.Show(
                    dlg,
                    "Укажите имя шаблона.",
                    "Имя шаблона",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            accepted = true;
            dlg.DialogResult = true;
        };
        cancel.Click += (_, _) => dlg.DialogResult = false;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dlg.Content = panel;
        box.SelectAll();
        box.Focus();
        if (dlg.ShowDialog() != true || !accepted)
            return false;

        name = box.Text.Trim();
        return true;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var field in _blocks.SelectMany(b => b.AllFields()))
            _manualChecked.Add(field.Name);
        SyncTreeWithPatterns();
        UpdateSummary();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _manualChecked.Clear();
        SyncTreeWithPatterns();
        UpdateSummary();
    }

    private void AddSuffixPatterns_Click(object sender, RoutedEventArgs e)
    {
        var suffixes = _blocks
            .SelectMany(b => b.AllFields())
            .Where(f => f.IsChecked)
            .Select(f => DcfParser.GetLastToken(f.Name))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => "*" + s)
            .ToList();

        if (suffixes.Count == 0)
        {
            MessageBox.Show(
                this,
                "Отметьте хотя бы одно поле — для его суффикса будет добавлен шаблон *Суффикс.",
                "Шаблоны",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        var existing = ParsePatternsBox();
        foreach (var pattern in suffixes)
            existing.Add(pattern);

        PatternsBox.Text = string.Join(", ", existing.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }

    private void PatternsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready)
            return;

        SyncTreeWithPatterns();
        UpdateSummary();
    }

    private void SyncTreeWithPatterns()
    {
        var patterns = ParsePatternsBox().Where(DcfParser.IsIgnorePattern).ToList();

        _suppressBlockEvents = true;
        try
        {
            foreach (var block in _blocks)
            {
                foreach (var field in block.AllFields())
                {
                    bool byPattern = patterns.Any(p => DcfParser.MatchesIgnorePattern(p, field.Name));
                    field.IsPatternMatch = byPattern;
                    field.IsChecked = byPattern || _manualChecked.Contains(field.Name);
                }

                block.RefreshCheckState();
            }
        }
        finally
        {
            _suppressBlockEvents = false;
        }
    }

    private HashSet<string> ParsePatternsBox()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            var part in PatternsBox.Text.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (!string.IsNullOrWhiteSpace(part))
                set.Add(part);
        }

        return set;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedFields = CollectCurrentIgnoreSet();
        DialogResult = true;
    }

    private void ViewList_Click(object sender, RoutedEventArgs e)
    {
        var current = CollectCurrentIgnoreSet();
        if (current.Count == 0)
        {
            MessageBox.Show(
                this,
                "Ничего не выбрано — список пуст.",
                "Игнорируемые поля",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        var view = new IgnoredFieldsViewWindow(_parsed, current) { Owner = this };
        view.ShowDialog();
    }

    private HashSet<string> CollectCurrentIgnoreSet()
    {
        var patterns = ParsePatternsBox().Where(DcfParser.IsIgnorePattern).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fields = _blocks
            .SelectMany(b => b.AllFields())
            .Where(f => f.IsChecked)
            .Select(f => f.Name)
            .Where(name => !patterns.Any(p => DcfParser.MatchesIgnorePattern(p, name)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
            result.Add(pattern);
        return result;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void FieldCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressBlockEvents)
            return;

        if (sender is CheckBox { DataContext: IgnoreFieldNode field })
        {
            if (field.IsChecked)
                _manualChecked.Add(field.Name);
            else
                _manualChecked.Remove(field.Name);

            if (!field.IsChecked && field.IsPatternMatch)
            {
                _suppressBlockEvents = true;
                try
                {
                    field.IsChecked = true;
                }
                finally
                {
                    _suppressBlockEvents = false;
                }
            }

            _suppressBlockEvents = true;
            try
            {
                RefreshBlockAndAncestors(field.Parent);
            }
            finally
            {
                _suppressBlockEvents = false;
            }
        }

        UpdateSummary();
    }

    private void BlockCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressBlockEvents)
            return;

        if (sender is not CheckBox { DataContext: IgnoreBlockNode block } box)
            return;

        bool value = box.IsChecked == true;

        _suppressBlockEvents = true;
        try
        {
            foreach (var field in block.AllFields())
            {
                if (value)
                    _manualChecked.Add(field.Name);
                else if (!field.IsPatternMatch)
                    _manualChecked.Remove(field.Name);

                field.IsChecked = value || field.IsPatternMatch;
            }

            block.RefreshCheckState();
            RefreshBlockAndAncestors(block.ParentBlock);
        }
        finally
        {
            _suppressBlockEvents = false;
        }

        UpdateSummary();
    }

    private static void RefreshBlockAndAncestors(IgnoreBlockNode? block)
    {
        while (block is not null)
        {
            block.RefreshCheckState();
            block = block.ParentBlock;
        }
    }

    private void UpdateSummary()
    {
        if (!_ready)
            return;

        int fieldCount = _blocks.SelectMany(b => b.AllFields()).Count(f => f.IsChecked);
        int patternMatched = _blocks.SelectMany(b => b.AllFields()).Count(f => f.IsPatternMatch);
        int patternCount = ParsePatternsBox().Count(DcfParser.IsIgnorePattern);

        if (fieldCount == 0 && patternCount == 0)
            SummaryText.Text = "Ничего не выбрано";
        else if (patternCount == 0)
            SummaryText.Text = $"Выбрано полей: {fieldCount}";
        else
            SummaryText.Text =
                $"Шаблонов: {patternCount} (совпало полей: {patternMatched}), всего отмечено: {fieldCount}";
    }
}
