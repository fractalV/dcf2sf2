using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DcfToSf2.Diagnostics;
using Microsoft.Win32;

namespace DcfToSf2.Ui;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush InfoBrush = new(Colors.Black);
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0, 128, 0));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(180, 120, 0));
    private static readonly SolidColorBrush ErrorBrush = new(Colors.Firebrick);

    public MainWindow()
    {
        InitializeComponent();
        LogBox.Document = new FlowDocument();
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
            IgnoreFields = ConversionOptions.ParseIgnoreFields(IgnoreFieldsBox.Text),
        };

        ConvertButton.IsEnabled = false;
        ClearLog();
        AppendLog($"Конвертация: {inputFile.FullName}", ConversionLogLevel.Info);

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
