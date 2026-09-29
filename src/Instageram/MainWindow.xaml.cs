using Instageram.Services;
using System.Diagnostics;
using System.IO;


using System.Reflection;
using System.Linq;

using Microsoft.Data.Sqlite;



using System.Data;



using System.Globalization;



using System.IO.Compression;



using System.Text;



using System.Text.Json;



using System.Text.RegularExpressions;



using System.Windows;




namespace Instageram;

public partial class MainWindow : Window
{
    // ===== Manual Instagram Workflow =====
    private readonly OrganicFollowEngine _manualEngine = new();
    private OrganicCandidate? _manualCurrent;
    private int _manualCompleted;
    private long _activeCampaignId;

    public MainWindow()
    {
        InitializeComponent();

        PathText.Text = "Portable Root:" + Environment.NewLine + PortablePaths.Root;

        // Phase 2: the country list comes from data\countries.json instead of
        // a hard-coded array inside this file (see Core\CountryCatalog.cs).
        CountryList.ItemsSource = CountryCatalog.DisplayNames;

        // Phase 3: fill the Settings page from config\settings.json.
        LoadSettingsIntoUi();

        // Phase 3: fill the statistics page from the database.
        LoadStatisticsIntoUi();

        RefreshDashboard();
    }

    private void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (InstagramValidator.TryGetUsername(UrlBox.Text, out var username, out var message))
        {
            ResultText.Text = "✓ URL معتبر است." + Environment.NewLine +
                              "Username: @" + username + Environment.NewLine + Environment.NewLine +
                              "برای داده آنلاین در نسخه‌های آینده فقط باید از API رسمی و مجاز استفاده شود.";
            StatusText.Text = "Instagram URL validated.";
        }
        else
        {
            ResultText.Text = "✗ " + message;
            StatusText.Text = "Invalid Instagram URL.";
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e) => CreateCampaign(false);

    private void Run_Click(object sender, RoutedEventArgs e) => CreateCampaign(true);

    private void CreateCampaign(bool startNow)
    {
        try
        {
            if (!InstagramValidator.TryGetUsername(UrlBox.Text, out var username, out var error))
            {
                MessageBox.Show(error, "Invalid Instagram URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var name = NameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("نام کشور را وارد کنید.", "Country Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!long.TryParse(TargetBox.Text.Replace(",", "").Trim(), out var target) || target <= 0)
            {
                MessageBox.Show("هدف Number باید عددی بزرگ‌تر از صفر باشد.", "Invalid هدف", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!long.TryParse(CurrentBox.Text.Replace(",", "").Trim(), out var current) || current < 0)
            {
                MessageBox.Show("Current Result باید صفر یا عدد مثبت باشد.", "Invalid Current Result", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selected = CountryList.SelectedItems.Cast<string>().ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show("حداقل یک کشور یا بازار را انتخاب کنید.", "Country Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string profileUrl = "https://www.instagram.com/" + username.Trim('/') + "/";

if(string.IsNullOrWhiteSpace(profileUrl))
{
    profileUrl = "https://www.instagram.com/" + username.Trim() + "/";
}

_activeCampaignId = DatabaseService.AddCampaign(name, username, profileUrl, target, current, string.Join(" | ", selected), startNow);

            if(_activeCampaignId > 0)
            {
                CentralBrain.Instance.SetActiveCampaign(_activeCampaignId);
                RefreshDashboard();
            }

CentralBrain.Instance.SetActiveCampaign(_activeCampaignId);
             

            if (startNow)
            {
                StartManualWorkflow_Click(this, new RoutedEventArgs());
            }

            MessageBox.Show("کمپین با موفقیت ایجاد شد.", "INSTAGERAM", MessageBoxButton.OK, MessageBoxImage.Information);

            UrlBox.Clear();
            NameBox.Clear();
            TargetBox.Text = "1000";
            CurrentBox.Text = "0";
            CountryList.UnselectAll();

            RefreshDashboard();
            StatusText.Text = "Campaign created successfully.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("CreateCampaign", ex.ToString());
            MessageBox.Show(ErrorMessages.ForUser(ex), "Campaign Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    
    // ===== Manual Instagram Workflow =====
    private void StartManualWorkflow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // اگر CreateCampaign همین الان کمپین ساخته، همان ID را استفاده کن.
            // فقط اگر ID نداریم، آخرین کمپین دیتابیس را بخوان.
            long campaignId = DatabaseService.GetLatestCampaignId();

            _activeCampaignId = campaignId;

            if(campaignId <= 0)
            {
                MessageBox.Show("No campaign in database");
                return;
            }

            CentralBrain.Instance.SetActiveCampaign(campaignId);

            UrlBox.Text = DatabaseService.GetCampaignUrl(campaignId);
            StatusText.Text = "Campaign loaded from database";

            if(campaignId <= 0)
            {
                MessageBox.Show(
                    "هیچ کمپینی در دیتابیس وجود ندارد. ابتدا از بخش «ایجاد کمپین» یک کمپین ذخیره کنید.",
                    "Campaign",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _activeCampaignId = campaignId;
            CentralBrain.Instance.SetActiveCampaign(campaignId);

            string url = DatabaseService.GetCampaignUrl(campaignId);

            if(string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(
                    "کمپین پیدا شد ولی URL اینستاگرام آن خالی است.",
                    "Campaign URL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var username = url.TrimEnd('/').Split('/').Last();

            _manualEngine.Stop();

            var candidate = new OrganicCandidate
            {
                Username = username,
                ProfileUrl = url,
                Country = "Manual",
                Status = OrganicCandidateStatus.Discovered
            };

            _manualEngine.EnqueueDiscovered(new[] { candidate });
            _manualEngine.Start();

            _manualCompleted = 0;

            ManualCurrentText.Text = "@" + username;
            ManualQueueText.Text = "Queue: 1 | Completed: 0";
            ManualStatusText.Text = "کمپین آماده است.";

            OpenCurrentProfile();
        }
        catch(Exception ex)
        {
            ManualStatusText.Text = "خطا در شروع ارسال دستی";

            MessageBox.Show(
                ex.ToString(),
                "Manual Workflow Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

private void ShowNextManualCandidate()
    {
        _manualCurrent = _manualEngine.GetNext();

        if(_manualCurrent == null)
        {
            ManualCurrentText.Text = "صف تکمیل شد.";
            ManualQueueText.Text =
                $"Queue: 0 | Completed: {_manualCompleted}";
            ManualStatusText.Text = "فرآیند تکمیل شد.";
            return;
        }

        ManualCurrentText.Text = "@" + _manualCurrent.Username;

        ManualQueueText.Text =
            $"Queue: {_manualEngine.QueueCount} | Completed: {_manualCompleted}";

        ManualStatusText.Text = "پیج فعلی آماده است.";

        OpenCurrentProfile();
    }

    private void OpenCurrentProfile_Click(object sender, RoutedEventArgs e)
{
    OpenCurrentProfile();
}
private void OpenCurrentProfile()
{
    try
    {
        string url = UrlBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("ابتدا URL اینستاگرام را وارد کنید.", "باز کردن پیج");
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://www.instagram.com/" + url.Trim('/') + "/";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("www.instagram.com", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("URL اینستاگرام معتبر نیست.", "باز کردن پیج");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true
        });
    }
    catch (Exception ex)
    {
        MessageBox.Show(ErrorMessages.ForUser(ex), "خطا در باز کردن پیج");
    }
}
private void CompleteCurrent_Click(object sender, RoutedEventArgs e)
    {
        if(_manualCurrent == null) return;

        _manualEngine.MarkCompleted(_manualCurrent);

        if(_activeCampaignId > 0)
        {
            ActivityTracker.Register(
                _activeCampaignId,
                "ManualActivity",
                "Candidate completed");
            
            DatabaseService.RegisterCampaignActivity(_activeCampaignId);
        }

        if(_activeCampaignId <= 0)
            CentralBrain.Instance.SyncCampaign();
            _activeCampaignId = CentralBrain.Instance.ActiveCampaignId;

            if(_activeCampaignId <= 0)
            {
                _activeCampaignId = DatabaseService.GetLatestCampaignId();
                CentralBrain.Instance.SetActiveCampaign(_activeCampaignId);
            }
            _manualCompleted++;

            if(_activeCampaignId > 0)
            {
                CentralBrain.Instance.Complete();
                RefreshDashboard();
            }


        ShowNextManualCandidate();
    }

    private void RejectCurrent_Click(object sender, RoutedEventArgs e)
    {
        if(_manualCurrent == null) return;

        _manualEngine.MarkRejected(_manualCurrent);

        ShowNextManualCandidate();
    }

    private void NextCurrent_Click(object sender, RoutedEventArgs e)
    {
        ShowNextManualCandidate();
    }

    /// <summary>
    /// Phase 3: fills the manual work list from config\instagram_users.txt.
    /// Nothing is sent anywhere - this only reads the list the user maintains
    /// and queues it, so every action on Instagram stays manual.
    /// </summary>
    private async void ImportUsers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var provider = InstagramDiscoveryProviderFactory.Create();

            var candidates = await provider.DiscoverAsync(10000);

            var file = (provider as ImportedInstagramDiscoveryProvider)?.FilePath
                       ?? PortablePaths.UsersFile;

            if (candidates.Count == 0)
            {
                ManualStatusText.Text = Localization.T("import_empty");

                MessageBox.Show(
                    Localization.T("import_empty") + Environment.NewLine + Environment.NewLine + file,
                    "INSTAGERAM",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var queue = candidates.Select(c => new OrganicCandidate
            {
                Username = c.Username,
                ProfileUrl = c.ProfileUrl,
                Country = string.IsNullOrWhiteSpace(c.Country) ? "Imported" : c.Country,
                Status = OrganicCandidateStatus.Discovered
            });

            _manualEngine.EnqueueDiscovered(queue);

            var message = Localization.Format("import_done", candidates.Count);

            ManualQueueText.Text =
                $"Queue: {_manualEngine.QueueCount} | Completed: {_manualCompleted}";
            ManualStatusText.Text = message;
            StatusText.Text = message;

            AppLogger.Info("Import", $"{candidates.Count} profiles loaded from {file}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Import", ex.ToString());
            MessageBox.Show(ErrorMessages.ForUser(ex), "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
private void Export_Click(object sender, RoutedEventArgs e) => RunExport(DatabaseService.ExportCsv, "report_csv_done");

    /// <summary>Phase 3: Excel report.</summary>
    private void ExportXlsx_Click(object sender, RoutedEventArgs e) => RunExport(DatabaseService.ExportXlsx, "report_xlsx_done");

    /// <summary>Phase 3: printable HTML report (browser Print -> PDF).</summary>
    private void ExportHtml_Click(object sender, RoutedEventArgs e) => RunExport(DatabaseService.ExportHtml, "report_html_done");

    private void RunExport(Func<string> export, string doneKey)
    {
        try
        {
            var path = export();

            ActionText.Text = Localization.T(doneKey) + Environment.NewLine + path;
            StatusText.Text = Localization.T(doneKey);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Export", ex.ToString());
            ActionText.Text = Localization.T("report_failed");
            MessageBox.Show(ErrorMessages.ForUser(ex), "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = BackupService.Create();
            ActionText.Text = "✓ Backup ZIP ساخته شد:" + Environment.NewLine + path;
            StatusText.Text = "Backup created.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Backup", ex.ToString());
            MessageBox.Show(ErrorMessages.ForUser(ex), "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Phase 3: restore a backup ZIP chosen by the user.</summary>
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "انتخاب فایل پشتیبان",
                Filter = "INSTAGERAM Backup (*.zip)|*.zip|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(PortablePaths.Backups)
                    ? PortablePaths.Backups
                    : PortablePaths.Root
            };

            if (dialog.ShowDialog() != true)
                return;

            var confirm = MessageBox.Show(
                "داده‌های فعلی با محتوای این پشتیبان جایگزین می‌شود." + Environment.NewLine +
                "پیش از جایگزینی، یک نسخه امنیتی خودکار گرفته می‌شود." + Environment.NewLine + Environment.NewLine +
                "ادامه می‌دهید؟",
                "بازیابی پشتیبان",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            var result = BackupService.Restore(dialog.FileName);

            ActionText.Text = (result.Success ? "✓ " : "✗ ") + result.Message;
            StatusText.Text = result.Success ? "Backup restored." : "Restore failed.";

            if (result.Success)
                RefreshDashboard();
            else
                MessageBox.Show(result.Message, "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            AppLogger.Error("RestoreUI", ex.ToString());
            MessageBox.Show(ErrorMessages.ForUser(ex), "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ================== Phase 3: Settings page ==================

    /// <summary>Guards the live theme preview while the UI is being filled.</summary>
    private bool _loadingSettings;

    /// <summary>Fills the Settings tab from the currently loaded settings.</summary>
    private void LoadSettingsIntoUi()
    {
        try
        {
            _loadingSettings = true;

            ThemeBox.ItemsSource = new[] { "Light", "Dark" };
            ThemeBox.SelectedItem = AppSettings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                ? "Dark"
                : "Light";

            LanguageBox.ItemsSource = new[] { "fa", "en" };
            LanguageBox.SelectedItem = AppSettings.Language.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : "fa";

            AutoBackupBox.IsChecked = AppSettings.AutomaticBackup;
            IntegrityCheckBox.IsChecked = AppSettings.IntegrityCheckEnabled;

            SettingsInfoText.Text = "تنظیمات بارگذاری شد." + Environment.NewLine +
                                    "منبع: " + AppSettings.Source;

            SettingsPathsText.Text =
                "ریشه قابل حمل: " + PortablePaths.Root + Environment.NewLine +
                "دیتابیس: " + PortablePaths.Database + Environment.NewLine +
                "لاگ‌ها: " + PortablePaths.Logs + Environment.NewLine +
                "گزارش‌ها: " + PortablePaths.Exports + Environment.NewLine +
                "پشتیبان‌ها: " + PortablePaths.Backups + Environment.NewLine +
                Localization.T("settings_template") + ": " +
                (File.Exists(PortablePaths.SettingsTemplate)
                    ? PortablePaths.SettingsTemplate
                    : Localization.T("common_none"));

            SettingsVersionText.Text =
                Localization.T("settings_version") + ": " + AppInfo.ProductName + " " + AppInfo.Version +
                Environment.NewLine +
                AppInfo.Framework + " · schema v" + DatabaseService.SchemaVersion;
        }
        catch (Exception ex)
        {
            AppLogger.Error("SettingsUI", ex.ToString());
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppSettings.Set(
                LanguageBox.SelectedItem as string ?? "fa",
                ThemeBox.SelectedItem as string ?? "Light",
                AutoBackupBox.IsChecked == true,
                IntegrityCheckBox.IsChecked == true);

            var saved = AppSettings.Save();

            // Phase 3: the theme is applied to the live window immediately,
            // because every colour in the XAML is a DynamicResource.
            var themeApplied = ThemeService.Apply(AppSettings.Theme);

            // Phase 3: reloading the strings updates every {loc:Text ...}
            // binding in the live window, with no restart.
            Localization.Load(AppSettings.Language);

            SettingsInfoText.Text = saved
                ? "✓ تنظیمات ذخیره شد و در جدول app_settings ثبت گردید." + Environment.NewLine +
                  "تم اعمال‌شده: " + ThemeService.Current +
                  (themeApplied ? "" : " (اعمال تم ناموفق بود؛ جزئیات در logs\\application.log)") + Environment.NewLine +
                  "چندزبان‌سازی در مرحله بعدی همین فاز فعال می‌شود."
                : "✗ ذخیره تنظیمات ناموفق بود. جزئیات در logs\\application.log";

            StatusText.Text = saved ? "Settings saved." : "Settings save failed.";

            LoadSettingsIntoUi();
        }
        catch (Exception ex)
        {
            AppLogger.Error("SettingsUI", ex.ToString());
            MessageBox.Show(ErrorMessages.ForUser(ex), "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadSettings_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Load();
        LoadSettingsIntoUi();
        StatusText.Text = "Settings reloaded from file.";
    }

    /// <summary>Phase 3: live theme preview without saving yet.</summary>
    private void ThemeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingSettings)
            return;

        if (ThemeBox.SelectedItem is not string theme)
            return;

        ThemeService.Apply(theme);

        SettingsInfoText.Text = "پیش‌نمایش تم: " + ThemeService.Current + Environment.NewLine +
                                "برای ذخیره دائمی، دکمه «ذخیره تنظیمات» را بزنید.";
    }

    /// <summary>Phase 3: live language preview without saving yet.</summary>
    private void LanguageBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingSettings)
            return;

        if (LanguageBox.SelectedItem is not string language)
            return;

        Localization.Load(language);

        SettingsInfoText.Text = "پیش‌نمایش زبان: " + Localization.Language + Environment.NewLine +
                                "منبع: " + Localization.Source + Environment.NewLine +
                                "برای ذخیره دائمی، دکمه «ذخیره تنظیمات» را بزنید.";
    }

    // ================== Phase 3: statistics page ==================

    /// <summary>Guards the growth refresh while the statistics tab is filled.</summary>
    private bool _loadingStatistics;

    /// <summary>Fills the statistics tab from the database.</summary>
    private void LoadStatisticsIntoUi()
    {
        try
        {
            _loadingStatistics = true;

            var selected = (StatsCampaignBox.SelectedItem as DatabaseService.CampaignOption)?.Id ?? 0;

            var options = DatabaseService.GetCampaignOptions();
            StatsCampaignBox.ItemsSource = options;

            if (options.Count > 0)
            {
                StatsCampaignBox.SelectedItem =
                    options.FirstOrDefault(o => o.Id == selected) ?? options[0];
            }

            if (string.IsNullOrWhiteSpace(StatsDateBox.Text))
                StatsDateBox.Text = DateTime.Now.ToString("yyyy-MM-dd");

            StatisticsGrid.ItemsSource = DatabaseService.GetRecentStatistics(200).DefaultView;

            RefreshGrowthPanel();
        }
        catch (Exception ex)
        {
            AppLogger.Error("StatisticsUI", ex.ToString());
        }
        finally
        {
            _loadingStatistics = false;
        }
    }

    private void SaveStatistics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (StatsCampaignBox.SelectedItem is not DatabaseService.CampaignOption campaign)
            {
                StatsMessageText.Text = Localization.T("stats_invalid");
                return;
            }

            var dateText = StatsDateBox.Text.Trim();

            if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                StatsMessageText.Text = Localization.T("stats_invalid");
                return;
            }

            if (!long.TryParse(StatsFollowersBox.Text.Replace(",", "").Trim(), out var followers) || followers < 0)
            {
                StatsMessageText.Text = Localization.T("stats_invalid");
                return;
            }

            long.TryParse(StatsGrowthBox.Text.Replace(",", "").Trim(), out var growth);

            double.TryParse(
                StatsEngagementBox.Text.Replace("%", "").Replace(",", ".").Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var engagement);

            var id = DatabaseService.RecordStatistics(
                campaign.Id,
                campaign.ProjectId,
                parsed.ToString("yyyy-MM-dd"),
                followers,
                growth,
                engagement,
                "",
                StatsNotesBox.Text.Trim());

            var message = Localization.Format("stats_saved", id);

            StatsMessageText.Text = message;
            StatusText.Text = message;

            StatsNotesBox.Clear();

            LoadStatisticsIntoUi();
        }
        catch (Exception ex)
        {
            AppLogger.Error("StatisticsUI", ex.ToString());
            MessageBox.Show(ErrorMessages.ForUser(ex), "Statistics Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshStatistics_Click(object sender, RoutedEventArgs e)
    {
        LoadStatisticsIntoUi();

        StatsMessageText.Text = Localization.T("stats_refreshed");
        StatusText.Text = Localization.T("stats_refreshed");
    }

    /// <summary>Phase 3: redraws the chart and the head-line numbers.</summary>
    private void RefreshGrowthPanel()
    {
        try
        {
            var campaignId = (StatsCampaignBox.SelectedItem as DatabaseService.CampaignOption)?.Id ?? 0;

            var series = DatabaseService.GetFollowerSeries(campaignId, 180);

            GrowthChart.Series = series.Select(point => (double)point.Followers).ToList();
            GrowthChart.Labels = series.Select(point => point.Date).ToList();

            var summary = DatabaseService.GetGrowthSummary(campaignId);

            GrowthLatestText.Text = summary.Records == 0
                ? "—"
                : summary.LatestFollowers.ToString("N0");

            GrowthTotalText.Text = summary.Records == 0
                ? "—"
                : (summary.TotalGrowth > 0 ? "+" : "") + summary.TotalGrowth.ToString("N0");

            GrowthEngagementText.Text = summary.Records == 0
                ? "—"
                : summary.AverageEngagement.ToString("N2") + "%";

            GrowthRecordsText.Text = summary.Records.ToString("N0");

            ComparisonGrid.ItemsSource = DatabaseService.GetCampaignComparison().DefaultView;
        }
        catch (Exception ex)
        {
            AppLogger.Error("GrowthUI", ex.ToString());
        }
    }

    private void StatsCampaignBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingStatistics)
            return;

        RefreshGrowthPanel();
    }

    /// <summary>
    /// Phase 4: when the database had to be repaired at startup, the user is
    /// told plainly what happened and where the damaged file was put.
    /// </summary>
    private void ReportRecoveryIfAny()
    {
        try
        {
            if (DatabaseService.LastRecovery is not { Recovered: true } recovery)
                return;

            var message = Localization.T("recovery_performed") + Environment.NewLine + Environment.NewLine;

            if (recovery.Quarantined && !string.IsNullOrWhiteSpace(recovery.QuarantinePath))
            {
                message += Localization.T("recovery_quarantined") + Environment.NewLine +
                           recovery.QuarantinePath + Environment.NewLine + Environment.NewLine;
            }

            message += Localization.Format("recovery_salvaged", recovery.SalvagedRows, recovery.SalvagedTables)
                       + Environment.NewLine +
                       Localization.T("recovery_use_backup");

            StatusText.Text = Localization.T("recovery_performed");

            MessageBox.Show(message, "INSTAGERAM", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AppLogger.Error("RecoveryUI", ex.ToString());
        }
    }

    /// <summary>Maps settings.startup_mode to a tab index (default: Dashboard).</summary>
    private static int StartupTabIndex() =>
        AppSettings.StartupMode.Trim().ToLowerInvariant() switch
        {
            "newcampaign" or "campaign" => 1,
            "validation" or "security" => 2,
            "reports" or "backup" => 3,
            "settings" => 4,
            "statistics" or "stats" or "growth" => 5,
            _ => 0
        };

    
private void SaveCampaign_Click(object sender, RoutedEventArgs e)
{
    try
    {
        string name = NameBox.Text.Trim();
        string url = UrlBox.Text.Trim();

        if(string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Campaign name required");
            return;
        }

        long target = long.TryParse(TargetBox.Text,out var t) ? t : 0;
        long current = long.TryParse(CurrentBox.Text,out var c) ? c : 0;

        var countries = string.Join(" | ",
            CountryList.SelectedItems.Cast<object>());

        long id = DatabaseService.AddCampaign(
            name,
            name,
            url,
            target,
            current,
            countries,
            false
        );

        StatusText.Text = "کمپین ذخیره شد (شناسه: " + id + ")";

        RefreshDashboard();
    }
    catch(Exception ex)
    {
        AppLogger.Error("SaveCampaign", ex.ToString());

        MessageBox.Show(ErrorMessages.ForUser(ex), "Save Campaign Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
private void RefreshDashboard()
    {
        var summary = DatabaseService.GetSummary();

        ProjectsText.Text = summary.Projects.ToString("N0");
        CampaignsText.Text = summary.Campaigns.ToString("N0");
        TargetText.Text = summary.هدف.ToString("N0");
        ProgressText.Text = summary.Progress.ToString("N2") + "% (" + summary.Current.ToString("N0") + "/" + summary.Target.ToString("N0") + ")";

        var dashboardData = DatabaseService.GetCampaigns();
        CampaignGrid.ItemsSource = dashboardData.DefaultView;
        StatusText.Text = "Dashboard loaded from database";
    }
}

public partial class MainWindow
{
    // Phase 1 fix: this handler used to look for a control named
    // "CountryComboBox" that does not exist in MainWindow.xaml (the real
    // control is the CountryList ListBox), so it silently did nothing.
    // It now works with the real control: Iran is moved to the top and selected.
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CountryList.ItemsSource is not IEnumerable<string> countries)
                return;

            var ordered = countries
                .OrderByDescending(c => c.StartsWith("Iran", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (ordered.Count == 0)
                return;

            CountryList.ItemsSource = ordered;
            CountryList.SelectedIndex = 0;

            // The startup tab comes from settings.json ("startup_mode").
            // This also repairs the fact that selecting an item inside a tab
            // can pull that tab to the front during load.
            MainTabs.SelectedIndex = StartupTabIndex();
            MainTabs.UpdateLayout();

            ReportRecoveryIfAny();
        }
        catch (Exception ex)
        {
            AppLogger.Error("MainWindow_Loaded", ex.ToString());
        }
    }
}




















