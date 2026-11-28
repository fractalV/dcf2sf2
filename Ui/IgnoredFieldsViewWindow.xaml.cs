using System.Windows;
using DcfToSf2.Parsing;

namespace DcfToSf2.Ui;

internal partial class IgnoredFieldsViewWindow : Window
{
    public IgnoredFieldsViewWindow(DcfParseResult parsed, IEnumerable<string> ignoreFields)
    {
        InitializeComponent();

        var rules = ignoreFields
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        RulesBox.Text = rules.Count == 0 ? "(нет правил)" : string.Join(", ", rules);

        var rows = IgnoredFieldsResolver.Resolve(parsed, rules);
        FieldsGrid.ItemsSource = rows;
        SummaryText.Text =
            rows.Count == 0
                ? "Нет полей, попадающих под текущие правила игнорирования."
                : $"Всего игнорируемых полей в DCF: {rows.Count}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
