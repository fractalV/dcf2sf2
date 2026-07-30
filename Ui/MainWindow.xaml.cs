using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DcfToSf2.Diagnostics;
using DcfToSf2.Parsing;
using Microsoft.Win32;

namespace DcfToSf2.Ui;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush InfoBrush = new(Colors.Black);
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0, 128, 0));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(180, 120, 0));
    private static readonly SolidColorBrush ErrorBrush = new(Colors.Firebrick);

    private readonly UiSettings _settings;
    private HashSet<string> _ignoreFields = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadingUi;

    public MainWindow()
    {
        InitializeComponent();
        LogBox.Document = new FlowDocument();

        _settings = UiSettings.Load();
        _loadingUi = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(_settings.LastInputFile))
                InputFileBox.Text = _settings.LastInputFile;
            if (!string.IsNullOrWhiteSpace(_settings.LastOutputDir))
                OutputDirBox.Text = _settings.LastOutputDir;
            TwoColumnBox.IsChecked = _settings.TwoColumn;
            ForceListBox.IsChecked = _settings.ForceList;

            _ignoreFields = _settings.GetIgnoreFieldsFor(InputFileBox.Text);
            RefreshIgnoreFieldsSummary();
        }
        finally
        {
            _loadingUi = false;
        }

        InputFileBox.TextChanged += InputFileBox_TextChanged;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e) => PersistSettings();

    private void InputFileBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingUi)
            return;

        _ignoreFields = _settings.GetIgnoreFieldsFor(InputFileBox.Text);
        RefreshIgnoreFieldsSummary();
    }

    private void PersistSettings()
    {
        _settings.LastInputFile = string.IsNullOrWhiteSpace(InputFileBox.Text)
            ? null
            : InputFileBox.Text.Trim();
        _settings.LastOutputDir = string.IsNullOrWhiteSpace(OutputDirBox.Text)
            ? null
            : OutputDirBox.Text.Trim();
        _settings.TwoColumn = TwoColumnBox.IsChecked == true;
        _settings.ForceList = ForceListBox.IsChecked == true;
        _settings.SetIgnoreFieldsFor(InputFileBox.Text, _ignoreFields);
        _settings.Save();
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "DCF files (*.dcf)|*.dcf|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(InputFileBox.Text))
        {
            try
            {
                var existing = new FileInfo(InputFileBox.Text.Trim());
                if (existing.Directory?.Exists == true)
                    dialog.InitialDirectory = existing.Directory.FullName;
            }
            catch
            {
                /* ignore invalid path */
            }
        }

        if (dialog.ShowDialog(this) == true)
            InputFileBox.Text = dialog.FileName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Каталог вывода",
        };

        if (!string.IsNullOrWhiteSpace(OutputDirBox.Text) && Directory.Exists(OutputDirBox.Text.Trim()))
            dialog.InitialDirectory = OutputDirBox.Text.Trim();
        else if (!string.IsNullOrWhiteSpace(InputFileBox.Text))
        {
            try
            {
                var dir = new FileInfo(InputFileBox.Text.Trim()).Directory;
                if (dir?.Exists == true)
                    dialog.InitialDirectory = dir.FullName;
            }
            catch
            {
                /* ignore */
            }
        }

        if (dialog.ShowDialog(this) == true)
            OutputDirBox.Text = dialog.FolderName;
    }

    private void SelectIgnoreFields_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseCurrentDcf(out var parsed, out _))
            return;

        if (parsed.Fields.Count == 0)
        {
            MessageBox.Show(
                this,
                "В DCF не найдено полей.",
                "Игнорируемые поля",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        var dialog = new IgnoreFieldsDialog(parsed, _ignoreFields) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _ignoreFields = dialog.SelectedFields;
            RefreshIgnoreFieldsSummary();
            PersistSettings();
        }
    }

    private void ViewIgnoredFields_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseCurrentDcf(out var parsed, out _))
            return;

        if (_ignoreFields.Count == 0)
        {
            MessageBox.Show(
                this,
                "Правила игнорирования не заданы.",
                "Игнорируемые поля",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        var view = new IgnoredFieldsViewWindow(parsed, _ignoreFields) { Owner = this };
        view.ShowDialog();
    }

    private bool TryParseCurrentDcf(out DcfParseResult parsed, out FileInfo inputFile)
    {
        parsed = null!;
        inputFile = null!;

        string inputPath = InputFileBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            MessageBox.Show(
                this,
                "Сначала укажите входной DCF-файл.",
                "Игнорируемые поля",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return false;
        }

        try
        {
            inputFile = new FileInfo(inputPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Некорректный путь к файлу: {ex.Message}",
                "Игнорируемые поля",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return false;
        }

        if (!inputFile.Exists)
        {
            MessageBox.Show(
                this,
                $"Файл не найден:\n{inputFile.FullName}",
                "Игнорируемые поля",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return false;
        }

        try
        {
            parsed = DcfParser.Parse(inputFile);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Не удалось прочитать DCF:\n{ex.Message}",
                "Игнорируемые поля",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            return false;
        }
    }

    private void RefreshIgnoreFieldsSummary()
    {
        IgnoreFieldsBox.Text =
            _ignoreFields.Count == 0
                ? string.Empty
                : string.Join(", ", _ignoreFields.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        string inputPath = InputFileBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            AppendLog("Укажите входной DCF-файл.", ConversionLogLevel.Error);
            return;
        }

        FileInfo inputFile;
        try
        {
            inputFile = new FileInfo(inputPath);
        }
        catch (Exception ex)
        {
            AppendLog($"Некорректный путь к файлу: {ex.Message}", ConversionLogLevel.Error);
            return;
        }

        DirectoryInfo outputDir;
        string outText = OutputDirBox.Text.Trim();
        try
        {
            if (string.IsNullOrWhiteSpace(outText))
            {
                outputDir =
                    inputFile.Directory
                    ?? new DirectoryInfo(Environment.CurrentDirectory);
            }
            else
            {
                outputDir = new DirectoryInfo(outText);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Некорректный каталог вывода: {ex.Message}", ConversionLogLevel.Error);
            return;
        }

        var options = new ConversionOptions
        {
            InputFile = inputFile,
            OutputDirectory = outputDir,
            TwoColumn = TwoColumnBox.IsChecked == true,
            ForceList = ForceListBox.IsChecked == true,
            IgnoreFields = new HashSet<string>(_ignoreFields, StringComparer.OrdinalIgnoreCase),
        };

        ConvertButton.IsEnabled = false;
        ClearLog();
        AppendLog($"Конвертация: {inputFile.FullName}", ConversionLogLevel.Info);
        if (_ignoreFields.Count > 0)
            AppendLog($"Игнорируются поля: {IgnoreFieldsBox.Text}", ConversionLogLevel.Info);

        var log = new CallbackConversionLog((message, level) =>
            Dispatcher.Invoke(() => AppendLog(message, level))
        );

        try
        {
            int code = await Task.Run(() =>
                MainOperations.MainOperations.ConvertFile(options, log, writePreview: false)
            );

            if (code == 0)
                AppendLog("Готово.", ConversionLogLevel.Success);
            else
                AppendLog($"Завершено с кодом {code}.", ConversionLogLevel.Error);

            PersistSettings();
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString(), ConversionLogLevel.Error);
        }
        finally
        {
            ConvertButton.IsEnabled = true;
        }
    }

    private void ClearLog()
    {
        LogBox.Document = new FlowDocument();
    }

    private void AppendLog(string message, ConversionLogLevel level)
    {
        var brush = level switch
        {
            ConversionLogLevel.Success => SuccessBrush,
            ConversionLogLevel.Warning => WarningBrush,
            ConversionLogLevel.Error => ErrorBrush,
            _ => InfoBrush,
        };

        var paragraph = new Paragraph(new Run(message) { Foreground = brush })
        {
            Margin = new Thickness(0),
        };

        LogBox.Document.Blocks.Add(paragraph);
        LogBox.ScrollToEnd();
    }
}
