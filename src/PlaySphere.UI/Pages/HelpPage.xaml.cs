using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PlaySphere.Application.Diagnostics;
using Windows.ApplicationModel.DataTransfer;

namespace PlaySphere.UI.Pages;

/// <summary>
/// Help → Report a problem. Packs local diagnostic logs and opens the default mail client.
/// Nothing is uploaded; the tester reviews and sends the email themselves.
/// </summary>
public sealed partial class HelpPage : Page
{
    private readonly IDiagnosticReportService? _reports = PlaySphereServices.GetOptional<IDiagnosticReportService>();
    private readonly DiagnosticReportOptions? _options = PlaySphereServices.GetOptional<DiagnosticReportOptions>();
    private string _dataDirectory = DiagnosticReportLocations.DefaultDataDirectory();
    private string? _lastZipPath;
    private bool _busy;

    public HelpPage()
    {
        InitializeComponent();
    }

    private string SupportEmail =>
        string.IsNullOrWhiteSpace(_options?.SupportEmail)
            ? DiagnosticReportDefaults.SupportEmail
            : _options.SupportEmail.Trim();

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        SupportEmailText.Text =
            $"Opens an email to {SupportEmail}. Nothing leaves this PC until you press Send in your mail app.";
        var warning = DiagnosticReportText.PlaceholderWarning(SupportEmail);
        if (warning is null)
        {
            PlaceholderWarningText.Visibility = Visibility.Collapsed;
        }
        else
        {
            PlaceholderWarningText.Text = warning;
            PlaceholderWarningText.Visibility = Visibility.Visible;
        }

        if (_reports is null)
        {
            LogListText.Text = "Diagnostic reports are unavailable in this session. You can still open the log folder.";
            SendReportButton.IsEnabled = false;
            return;
        }

        RefreshFileList();
        if (!DiagnosticReportDefaults.IsPlausibleSupportEmail(SupportEmail))
        {
            SendReportButton.IsEnabled = false;
            StatusText.Text = "The support email address is not configured. Set DiagnosticReportDefaults.SupportEmail and rebuild. The log folder is still available below.";
        }
    }

    private void IncludeLibraryCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        RefreshFileList();
    }

    private void RefreshFileList()
    {
        if (_reports is null)
        {
            return;
        }

        try
        {
            var inventory = _reports.Inspect(null);
            _dataDirectory = inventory.DataDirectory;
            LogListText.Text = DiagnosticReportText.FormatFileList(
                inventory,
                IncludeLibraryCheckBox.IsChecked == true);
        }
        catch (Exception ex)
        {
            LogListText.Text = "Couldn't read the log folder. " + ex.Message;
        }
    }

    private async void SendReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _reports is null)
        {
            return;
        }

        if (!DiagnosticReportDefaults.IsPlausibleSupportEmail(SupportEmail))
        {
            StatusText.Text = "The support email address is not configured. Set DiagnosticReportDefaults.SupportEmail and rebuild. The log folder is still available below.";
            return;
        }

        DiagnosticReportInventory inventory;
        try
        {
            inventory = _reports.Inspect(null);
            _dataDirectory = inventory.DataDirectory;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't read the log folder. " + ex.Message + Environment.NewLine + _dataDirectory;
            return;
        }

        var includeLibrary = IncludeLibraryCheckBox.IsChecked == true;
        var confirmation = DiagnosticReportText.BuildConfirmation(inventory, includeLibrary, SupportEmail);
        var confirmed = await ShowDialogAsync(
            "Send diagnostic report?",
            confirmation,
            primary: "Create report",
            secondary: null,
            close: "Cancel");
        if (confirmed != ContentDialogResult.Primary)
        {
            return;
        }

        _busy = true;
        SendReportButton.IsEnabled = false;
        SendReportButton.Content = "Creating report…";
        try
        {
            var result = await _reports.CreatePackAsync(new DiagnosticPackRequest
            {
                Description = DescriptionBox.Text,
                Contact = ContactBox.Text,
                IncludeLibraryDatabase = includeLibrary,
                AppVersion = ReadAppVersion(),
                OsDescription = Environment.OSVersion.VersionString,
            });

            if (!result.Success || string.IsNullOrWhiteSpace(result.ZipPath))
            {
                var message = (result.ErrorMessage
                    ?? "Couldn't create the diagnostic zip. You can still open the log folder and attach the files yourself.")
                    + Environment.NewLine + _dataDirectory;
                StatusText.Text = message;
                var choice = await ShowDialogAsync(
                    "Couldn't create the report",
                    message,
                    primary: "Open log folder",
                    secondary: "Copy folder path",
                    close: "Close");
                ApplyFolderChoice(choice);
                return;
            }

            _lastZipPath = result.ZipPath;
            ShowZipButton.Visibility = Visibility.Visible;
            CopyZipPathButton.Visibility = Visibility.Visible;
            RevealZip(result.ZipPath);

            var mailto = DiagnosticMailto.TryCreateUri(
                SupportEmail,
                result.ZipPath,
                DescriptionBox.Text,
                ContactBox.Text,
                result.IncludedEntries,
                includeLibrary);
            if (mailto is null || !TryOpenMailto(mailto))
            {
                var message =
                    "The zip is ready, but Windows couldn't open an email app. Attach the zip yourself — it has not been sent."
                    + Environment.NewLine + result.ZipPath;
                StatusText.Text = message;
                var choice = await ShowDialogAsync(
                    "Report is ready",
                    message,
                    primary: "Show the zip",
                    secondary: "Copy zip path",
                    close: "Close");
                ApplyZipChoice(choice, result.ZipPath);
                return;
            }

            StatusText.Text =
                "Email opened. Attach the zip if your mail app did not, then press Send. Nothing has been uploaded."
                + Environment.NewLine + result.ZipPath;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Report cancelled. Nothing was sent.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't create the report. " + ex.Message + Environment.NewLine + _dataDirectory;
            var choice = await ShowDialogAsync(
                "Couldn't create the report",
                StatusText.Text,
                primary: "Open log folder",
                secondary: "Copy folder path",
                close: "Close");
            ApplyFolderChoice(choice);
        }
        finally
        {
            _busy = false;
            SendReportButton.Content = "Report a problem";
            SendReportButton.IsEnabled = _reports is not null
                && DiagnosticReportDefaults.IsPlausibleSupportEmail(SupportEmail);
        }
    }

    private void ApplyFolderChoice(ContentDialogResult? choice)
    {
        if (choice == ContentDialogResult.Primary)
        {
            OpenLogFolder();
        }
        else if (choice == ContentDialogResult.Secondary)
        {
            CopyText(_dataDirectory, "Log folder path copied.");
        }
    }

    private void ApplyZipChoice(ContentDialogResult? choice, string zipPath)
    {
        if (choice == ContentDialogResult.Primary)
        {
            RevealZip(zipPath);
        }
        else if (choice == ContentDialogResult.Secondary)
        {
            CopyText(zipPath, "Zip path copied.");
        }
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e) => OpenLogFolder();

    private void CopyLogFolderButton_Click(object sender, RoutedEventArgs e) =>
        CopyText(_dataDirectory, "Log folder path copied.");

    private void ShowZipButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastZipPath))
        {
            RevealZip(_lastZipPath);
        }
    }

    private void CopyZipPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastZipPath))
        {
            CopyText(_lastZipPath, "Zip path copied.");
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't create the log folder. " + ex.Message + Environment.NewLine + _dataDirectory;
            return;
        }

        if (!ExplorerHelper.OpenFolder(_dataDirectory))
        {
            StatusText.Text = "Couldn't open File Explorer. The log folder is:" + Environment.NewLine + _dataDirectory;
        }
    }

    private static void RevealZip(string zipPath) => ExplorerHelper.OpenContainingFolder(zipPath);

    private void CopyText(string text, string successMessage)
    {
        if (TryCopyText(text))
        {
            StatusText.Text = successMessage + Environment.NewLine + text;
            return;
        }

        StatusText.Text = "Couldn't copy to the clipboard. The path is:" + Environment.NewLine + text;
    }

    private static bool TryCopyText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryOpenMailto(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<ContentDialogResult?> ShowDialogAsync(
        string title,
        string message,
        string primary,
        string? secondary,
        string close)
    {
        if (XamlRoot is null)
        {
            StatusText.Text = message;
            return null;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            },
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (!string.IsNullOrEmpty(secondary))
        {
            dialog.SecondaryButtonText = secondary;
        }

        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception)
        {
            StatusText.Text = message;
            return null;
        }
    }

    private static string ReadAppVersion()
    {
        try
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (Exception)
        {
            return typeof(HelpPage).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
    }
}
