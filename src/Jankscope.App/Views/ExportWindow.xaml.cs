using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Jankscope.App.Services;
using Jankscope.Core.Reports;
using Jankscope.Frames.Incidents;

namespace Jankscope.App.Views;

/// <summary>
/// The review step of an export (spec section 16: preview before export). It builds the redacted document, shows every line of it, says what was replaced,
/// and writes only that exact text. If anything personal survives redaction the export is blocked and nothing can be saved.
/// </summary>
public partial class ExportWindow : Window
{
    private readonly AppHost _host;
    private readonly Func<bool, ExportPreview> _previewFactory;
    private readonly string _defaultFileName;
    private readonly string _fileFilter;
    private readonly string _dialogTitle;
    private ExportPreview? _preview;
    private int _generation;

    public ExportWindow(AppHost host, IncidentMark mark)
        : this(
            host,
            redact => host.Controller.PrepareExport(mark, redact),
            $"jankscope-{mark.Id}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json",
            "Jankscope incident (*.json)|*.json",
            "Save the incident report")
    {
    }

    public ExportWindow(
        AppHost host,
        Func<bool, ExportPreview> previewFactory,
        string defaultFileName,
        string fileFilter = "Jankscope report (*.json)|*.json",
        string dialogTitle = "Save the report")
    {
        _host = host;
        _previewFactory = previewFactory;
        _defaultFileName = defaultFileName;
        _fileFilter = fileFilter;
        _dialogTitle = dialogTitle;
        InitializeComponent();
        Loaded += (_, _) => _ = BuildAsync();
    }

    private void OnRedactChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            _ = BuildAsync();
        }
    }

    private async Task BuildAsync()
    {
        var generation = ++_generation;
        var redact = RedactCheck.IsChecked == true;
        SaveButton.IsEnabled = false;
        SaveResult.Text = string.Empty;
        Warning.Visibility = Visibility.Collapsed;
        Summary.Text = "Preparing the preview...";
        PreviewList.ItemsSource = null;
        _preview = null;

        try
        {
            var preview = await Task.Run(() => _previewFactory(redact));
            if (generation != _generation)
            {
                return; // the person changed the option while this was running
            }

            _preview = preview;
            // The text has Windows line endings; a trailing carriage return would draw as an extra blank line. Only the display is trimmed: the saved bytes are the preview's own.
            PreviewList.ItemsSource = preview.Text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            Summary.Text = Describe(preview);
            if (!preview.RedactionApplied)
            {
                Warning.Text = "Redaction is OFF. This file may contain your Windows user name, your PC name, network details and anything you typed. Share it only with someone you trust.";
                Warning.Foreground = (Brush)FindResource("DangerBrush");
                Warning.Visibility = Visibility.Visible;
            }

            SaveButton.IsEnabled = true;
        }
        catch (ExportBlockedException ex)
        {
            Summary.Text = ex.Message + " Nothing can be saved. This is a bug in the redaction; the details of what survived are deliberately not shown.";
            Summary.Foreground = (Brush)FindResource("DangerBrush");
        }
        catch (InvalidOperationException ex)
        {
            Summary.Text = ex.Message;
        }
    }

    private static string Describe(ExportPreview preview)
    {
        var size = preview.Bytes.Length >= 1_000_000
            ? (preview.Bytes.Length / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : Math.Max(1, preview.Bytes.Length / 1000).ToString(CultureInfo.InvariantCulture) + " KB";
        var replaced = preview.Counts.Where(c => c.Value > 0).Select(c => $"{CategoryName(c.Key)}: {c.Value.ToString(CultureInfo.InvariantCulture)}").ToList();
        var redaction = !preview.RedactionApplied
            ? "No personal information was removed."
            : replaced.Count == 0
                ? "Nothing known needed to be removed. Only what Jankscope knows about is removed (this PC's user name and PC name, addresses): read the text below, especially your note."
                : "Replaced with a placeholder: " + string.Join(", ", replaced) + ". Only what Jankscope knows about is removed: read the text below, especially your note.";
        return $"{size}, SHA-256 {preview.Sha256[..16]}... {redaction}";
    }

    private static string CategoryName(RedactionCategory category) => category switch
    {
        RedactionCategory.UserName => "Windows user name",
        RedactionCategory.HostName => "PC name",
        RedactionCategory.WifiSsid => "Wi-Fi name",
        RedactionCategory.Bssid => "Wi-Fi access point",
        RedactionCategory.MacAddress => "network card address",
        RedactionCategory.IpAddress => "IP address",
        RedactionCategory.DeviceSerial => "device serial number",
        RedactionCategory.CommandLine => "command-line arguments",
        RedactionCategory.EmailAddress => "e-mail address",
        RedactionCategory.OrganizationName => "organization name",
        _ => category.ToString(),
    };

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_preview is not { } preview)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = _dialogTitle,
            Filter = _fileFilter,
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = _defaultFileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ExportPipeline.WriteAtomic(preview, dialog.FileName, overwrite: true);
            SaveResult.Text = "Saved. The file is exactly the text above (SHA-256 " + preview.Sha256[..16] + "...).";
            SaveResult.Foreground = (Brush)FindResource("OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SaveResult.Text = "The file could not be saved (" + ex.GetType().Name + "). Choose another place.";
            SaveResult.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
