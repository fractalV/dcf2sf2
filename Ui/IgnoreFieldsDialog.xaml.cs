using System.Windows;
using System.Windows.Controls;
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

        _blocks = BuildNodes(parsed, _manualChecked);
        FieldsTree.ItemsSource = _blocks;

        var patterns = SelectedFields
            .Where(DcfParser.IsIgnorePattern)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        PatternsBox.Text = string.Join(", ", patterns);

        _ready = true;
        SyncTreeWithPatterns();
        UpdateSummary();
    }

    private static List<IgnoreBlockNode> BuildNodes(DcfParseResult parsed, HashSet<string> manualChecked)
    {
        var groups = parsed
            .Fields.Where(f => !IsBlockOpenMarker(f) && !DcfParser.IsBuiltInServiceField(f.FieldElement.ElementName))
            .GroupBy(f => string.IsNullOrWhiteSpace(f.Path) ? Consts.DefaultBlockName : f.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var blocks = new List<IgnoreBlockNode>();
        foreach (var group in groups)
        {
            string title =
                group.Select(f => f.RusNameBlock).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                ?? IgnoreBlockNode.LeafName(group.Key);

            var fields = group
                .Select(f =>
                {
                    string name = f.FieldElement.ElementName;
                    string label = ExtractComment(f.FieldElement.ElementValue);
                    if (string.IsNullOrWhiteSpace(label))
                        label = DcfParser.GetLastToken(name);

                    return new IgnoreFieldNode(name, label, manualChecked.Contains(name));
                })
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fields.Count == 0)
                continue;

            blocks.Add(new IgnoreBlockNode(group.Key, title, fields));
        }

        return blocks;
    }

    private static bool IsBlockOpenMarker(Data f) =>
        string.IsNullOrEmpty(f.FieldElement.ElementValue) && !string.IsNullOrEmpty(f.FieldElement.ElementName);

    private static string ExtractComment(string elementValue)
    {
        int pipe = elementValue.IndexOf('|');
        if (pipe < 0)
            return string.Empty;

        string c = elementValue[(pipe + 1)..].Trim().TrimStart('*', '!').Trim();
        foreach (var key in new[] { "NAMESPACE=", "PATTERN=", "SEQ=", "CHOICE=", "PRNT=", "MAX=", "OPT=" })
        {
            int i = c.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
                c = c[..i].Trim();
        }

        return c;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var field in _blocks.SelectMany(b => b.Fields))
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
            .SelectMany(b => b.Fields)
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
                foreach (var field in block.Fields)
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
            .SelectMany(b => b.Fields)
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

            // Pattern matches stay checked even if user tries to uncheck
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
                field.Parent?.RefreshCheckState();
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
            foreach (var field in block.Fields)
            {
                if (value)
                    _manualChecked.Add(field.Name);
                else if (!field.IsPatternMatch)
                    _manualChecked.Remove(field.Name);

                field.IsChecked = value || field.IsPatternMatch;
            }

            block.RefreshCheckState();
        }
        finally
        {
            _suppressBlockEvents = false;
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (!_ready)
            return;

        int fieldCount = _blocks.SelectMany(b => b.Fields).Count(f => f.IsChecked);
        int patternMatched = _blocks.SelectMany(b => b.Fields).Count(f => f.IsPatternMatch);
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
