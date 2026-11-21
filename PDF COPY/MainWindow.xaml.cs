using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace PDF_COPY
{
    public class SkuItem : INotifyPropertyChanged
    {
        private int _quantity;
        private bool _isHighlighted;

        public string Name { get; set; } = string.Empty;

        public int Quantity
        {
            get => _quantity;
            set
            {
                int validValue = Math.Max(0, value);
                if (_quantity != validValue)
                {
                    _quantity = validValue;
                    OnPropertyChanged(nameof(Quantity));
                }
            }
        }

        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    OnPropertyChanged(nameof(IsHighlighted));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class WindowSettings
    {
        public double Left { get; set; } = 100;
        public double Top { get; set; } = 100;
        public double Width { get; set; } = 1240;
        public double Height { get; set; } = 750;
        public bool IsMaximized { get; set; } = false;
    }

    public partial class MainWindow : Window
    {
        private DispatcherTimer? _toastTimer;
        private DispatcherTimer? _skuToastTimer;
        private string _lastCopiedText = string.Empty;
        private string _lastSelectedLogText = string.Empty;
        private string? _initialPdfPath;
        private bool _isDataLoaded = false;

        public ObservableCollection<SkuItem> SkuList { get; set; } = new ObservableCollection<SkuItem>();

        private static string AppFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PDF_COPY"
        );

        private static string ConfigPath => Path.Combine(AppFolder, "window_settings.json");
        private static string SkuDataPath => Path.Combine(AppFolder, "sku_summary.json");
        private static string NotesDataPath => Path.Combine(AppFolder, "scratchpad.txt");

        private static readonly LowLevelMouseProc Proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private static MainWindow? _instance;

        public MainWindow()
        {
            InitializeComponent();
            _instance = this;

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && File.Exists(args[1]))
            {
                _initialPdfPath = args[1];
            }

            LstSkuItems.ItemsSource = SkuList;

            LoadWindowSettings();
            LoadAppData();
            InitializeToastTimer();

            SwitchTab(isSkuTab: true);

            _hookID = SetHook(Proc);
            Closing += (s, e) =>
            {
                SaveWindowSettings();
                SaveAppData();
                UnhookWindowsHookEx(_hookID);
            };

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_initialPdfPath) && File.Exists(_initialPdfPath))
            {
                OpenPdfInNewTab(_initialPdfPath);
            }
        }

        #region Title Bar Control Actions
        private void BtnWinMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnWinMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        private void BtnWinClose_Click(object sender, RoutedEventArgs e) => Close();
        #endregion

        #region 3-Dot Overflow Menu Handler
        private void BtnMoreMenu_Click(object sender, RoutedEventArgs e)
        {
            MoreMenuPopup.IsOpen = !MoreMenuPopup.IsOpen;
        }

        private void MenuItemAutoCopy_Click(object sender, MouseButtonEventArgs e)
        {
            ChkAutoCopy.IsChecked = !ChkAutoCopy.IsChecked;
        }

        private void MenuItemSkuSummary_Click(object sender, MouseButtonEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            if (PanelSkuRecap.Visibility == Visibility.Visible && GridSkuContent.Visibility == Visibility.Visible)
            {
                PanelSkuRecap.Visibility = Visibility.Collapsed;
            }
            else
            {
                PanelSkuRecap.Visibility = Visibility.Visible;
                SwitchTab(isSkuTab: true);
            }
        }

        private void MenuItemLog_Click(object sender, MouseButtonEventArgs e)
        {
            MoreMenuPopup.IsOpen = false;
            if (PanelSkuRecap.Visibility == Visibility.Visible && GridNotesContent.Visibility == Visibility.Visible)
            {
                PanelSkuRecap.Visibility = Visibility.Collapsed;
            }
            else
            {
                PanelSkuRecap.Visibility = Visibility.Visible;
                SwitchTab(isSkuTab: false);
            }
        }
        #endregion

        #region Multi-Tab PDF System (Edge-Style Responsive Tabs)
        public void OpenPdfInNewTab(string filePath)
        {
            string fileName = Path.GetFileName(filePath);

            // Jika PDF sudah ada di salah satu tab, aktifkan tab tersebut
            foreach (TabItem existingTab in PdfTabControl.Items)
            {
                if (existingTab.Tag is Tuple<string, WebView2> tuple && tuple.Item1.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    PdfTabControl.SelectedItem = existingTab;
                    return;
                }
            }

            WebView2 webViewer = new WebView2();

            // Format Header Tab Menggunakan Grid Responsive
            Grid headerGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0)
            };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock txtIcon = new TextBlock
            {
                Text = "📄",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 6, 0)
            };
            Grid.SetColumn(txtIcon, 0);

            TextBlock txtTitle = new TextBlock
            {
                Text = fileName,
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0E0E0")),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txtTitle, 1);

            Button btnClose = new Button
            {
                Content = "✕",
                Width = 20,
                Height = 20,
                Margin = new Thickness(4, 0, 2, 0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#999999")),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Normal,
                FontSize = 10,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            };
            Grid.SetColumn(btnClose, 2);

            headerGrid.Children.Add(txtIcon);
            headerGrid.Children.Add(txtTitle);
            headerGrid.Children.Add(btnClose);

            TabItem tabItem = new TabItem
            {
                Header = headerGrid,
                Tag = new Tuple<string, WebView2>(filePath, webViewer),
                Style = (Style)FindResource("EdgeTabItemStyle")
            };

            btnClose.Click += (s, e) => ClosePdfTab(tabItem);

            PdfTabControl.Items.Add(tabItem);
            PdfTabControl.SelectedItem = tabItem;

            InitializeTabWebView(webViewer, filePath);
        }

        private async void InitializeTabWebView(WebView2 webViewer, string filePath)
        {
            await webViewer.EnsureCoreWebView2Async(null);

            webViewer.CoreWebView2.NavigationStarting += (s, e) =>
            {
                if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                }
            };
            webViewer.CoreWebView2.NewWindowRequested += (s, e) => e.Handled = true;

            webViewer.CoreWebView2.Navigate(new Uri(filePath).AbsoluteUri);
        }

        private void ClosePdfTab(TabItem tabItem)
        {
            if (tabItem.Tag is Tuple<string, WebView2> tuple)
            {
                tuple.Item2.Dispose();
            }

            PdfTabControl.Items.Remove(tabItem);

            if (PdfTabControl.Items.Count == 0)
            {
                PdfTabContentPresenter.Content = null;
                PlaceholderPanel.Visibility = Visibility.Visible;
                TxtAddressBar.Text = "file:///select-a-pdf-file";
                _lastCopiedText = string.Empty;
            }
        }

        private void PdfTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PdfTabControl.SelectedItem is TabItem selectedTab && selectedTab.Tag is Tuple<string, WebView2> tuple)
            {
                TxtAddressBar.Text = new Uri(tuple.Item1).AbsoluteUri;
                PdfTabContentPresenter.Content = tuple.Item2;
                PlaceholderPanel.Visibility = Visibility.Collapsed;
            }
            else if (PdfTabControl.Items.Count == 0)
            {
                PdfTabContentPresenter.Content = null;
                PlaceholderPanel.Visibility = Visibility.Visible;
                TxtAddressBar.Text = "file:///select-a-pdf-file";
            }
        }

        private WebView2? GetActiveWebViewer()
        {
            if (PdfTabControl.SelectedItem is TabItem selectedTab && selectedTab.Tag is Tuple<string, WebView2> tuple)
            {
                return tuple.Item2;
            }
            return null;
        }
        #endregion

        #region Side Panel Tab Switcher
        private void SwitchTab(bool isSkuTab)
        {
            if (isSkuTab)
            {
                GridSkuContent.Visibility = Visibility.Visible;
                GridNotesContent.Visibility = Visibility.Collapsed;
                BtnTabSku.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0066FF"));
                BtnTabSku.Foreground = Brushes.White;
                BtnTabNotes.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
                BtnTabNotes.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0"));
            }
            else
            {
                GridSkuContent.Visibility = Visibility.Collapsed;
                GridNotesContent.Visibility = Visibility.Visible;
                BtnTabNotes.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0066FF"));
                BtnTabNotes.Foreground = Brushes.White;
                BtnTabSku.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
                BtnTabSku.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0"));
            }
        }

        private void BtnTabSku_Click(object sender, RoutedEventArgs e) => SwitchTab(isSkuTab: true);
        private void BtnTabNotes_Click(object sender, RoutedEventArgs e) => SwitchTab(isSkuTab: false);
        #endregion

        #region Open & Reset PDF Features
        private void BtnOpenPdf_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Select PDF File",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string file in openFileDialog.FileNames)
                {
                    OpenPdfInNewTab(file);
                }
            }
        }

        private void BtnResetPdf_Click(object sender, RoutedEventArgs e)
        {
            while (PdfTabControl.Items.Count > 0)
            {
                if (PdfTabControl.Items[0] is TabItem tab)
                {
                    ClosePdfTab(tab);
                }
            }
        }
        #endregion

        #region Show / Hide Side Panel
        private void BtnToggleSku_Click(object sender, RoutedEventArgs e)
        {
            PanelSkuRecap.Visibility = PanelSkuRecap.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
        #endregion

        #region SKU Summary Management Features
        private async void AddNewSku()
        {
            string name = TxtSkuInput.Text.Trim().ToUpper();
            if (string.IsNullOrEmpty(name)) return;

            var existing = SkuList.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                SkuList.Remove(existing);
                SkuList.Insert(0, existing);

                TxtSkuInput.Clear();
                TxtSkuInput.Focus();

                for (int i = 0; i < 3; i++)
                {
                    existing.IsHighlighted = true;
                    await Task.Delay(150);
                    existing.IsHighlighted = false;
                    await Task.Delay(120);
                }
            }
            else
            {
                SkuList.Insert(0, new SkuItem { Name = name, Quantity = 1 });
                TxtSkuInput.Clear();
                TxtSkuInput.Focus();
            }

            SaveAppData();
        }

        private void BtnAddSku_Click(object sender, RoutedEventArgs e) => AddNewSku();

        private void TxtSkuInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddNewSku();
        }

        private void BtnIncrement_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SkuItem item)
            {
                item.Quantity++;
                SaveAppData();
            }
        }

        private void BtnDecrement_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SkuItem item && item.Quantity > 0)
            {
                item.Quantity--;
                SaveAppData();
            }
        }

        private void BtnDeleteSku_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SkuItem item)
            {
                SkuList.Remove(item);
                SaveAppData();
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void BtnExportSku_Click(object sender, RoutedEventArgs e)
        {
            var activeSkus = SkuList.Where(x => x.Quantity > 0).OrderByDescending(x => x.Quantity).ThenBy(x => x.Name).ToList();
            if (!activeSkus.Any())
            {
                InfoExportOverlay.Visibility = Visibility.Visible;
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (var sku in activeSkus) sb.AppendLine($"{sku.Name.ToUpper()} x{sku.Quantity}");

            Clipboard.SetText(sb.ToString().TrimEnd());
            ShowSkuToast($"{activeSkus.Count} SKU summary copied!");
        }

        private void BtnInfoExportOk_Click(object sender, RoutedEventArgs e) => InfoExportOverlay.Visibility = Visibility.Collapsed;

        private void BtnResetQty_Click(object sender, RoutedEventArgs e)
        {
            if (!SkuList.Any()) return;
            foreach (var item in SkuList) item.Quantity = 0;
            SaveAppData();
            ShowSkuToast("All SKU quantities set to 0!");
        }

        private void BtnClearAllSku_Click(object sender, RoutedEventArgs e)
        {
            if (!SkuList.Any()) return;
            ConfirmClearOverlay.Visibility = Visibility.Visible;
        }

        private void BtnConfirmClearYes_Click(object sender, RoutedEventArgs e)
        {
            SkuList.Clear();
            ConfirmClearOverlay.Visibility = Visibility.Collapsed;
            SaveAppData();
            ShowSkuToast("All SKUs cleared!");
        }

        private void BtnConfirmClearNo_Click(object sender, RoutedEventArgs e) => ConfirmClearOverlay.Visibility = Visibility.Collapsed;
        #endregion

        #region Log (Notepad) Features with Auto-Copy & F5 Key
        private void TxtScratchpad_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                string timestamp = DateTime.Now.ToString("H:mm dd/MM/yyyy");

                int selectionStart = TxtScratchpad.SelectionStart;
                int selectionLength = TxtScratchpad.SelectionLength;

                if (selectionLength > 0)
                {
                    TxtScratchpad.Text = TxtScratchpad.Text.Remove(selectionStart, selectionLength).Insert(selectionStart, timestamp);
                }
                else
                {
                    TxtScratchpad.Text = TxtScratchpad.Text.Insert(selectionStart, timestamp);
                }

                TxtScratchpad.CaretIndex = selectionStart + timestamp.Length;
            }
        }

        private void TxtScratchpad_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isDataLoaded) SaveAppData();
        }

        private void TxtScratchpad_SelectionChanged(object sender, RoutedEventArgs e)
        {
            string selectedText = TxtScratchpad.SelectedText.Trim();
            if (!string.IsNullOrEmpty(selectedText) && selectedText != _lastSelectedLogText)
            {
                _lastSelectedLogText = selectedText;
                Clipboard.SetText(selectedText);
            }
            else if (string.IsNullOrEmpty(selectedText))
            {
                _lastSelectedLogText = string.Empty;
            }
        }
        #endregion

        #region Data Persistence (Auto Save & Load)
        private void SaveAppData()
        {
            try
            {
                if (!Directory.Exists(AppFolder)) Directory.CreateDirectory(AppFolder);

                string skuJson = JsonSerializer.Serialize(SkuList, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SkuDataPath, skuJson, Encoding.UTF8);

                File.WriteAllText(NotesDataPath, TxtScratchpad.Text, Encoding.UTF8);
            }
            catch { }
        }

        private void LoadAppData()
        {
            try
            {
                if (File.Exists(SkuDataPath))
                {
                    string skuJson = File.ReadAllText(SkuDataPath, Encoding.UTF8);
                    var items = JsonSerializer.Deserialize<ObservableCollection<SkuItem>>(skuJson);
                    if (items != null)
                    {
                        SkuList.Clear();
                        foreach (var item in items) SkuList.Add(item);
                    }
                }

                if (File.Exists(NotesDataPath))
                {
                    TxtScratchpad.Text = File.ReadAllText(NotesDataPath, Encoding.UTF8);
                }
            }
            catch { }
            finally
            {
                _isDataLoaded = true;
            }
        }
        #endregion

        #region Save & Load Window Settings
        private void LoadWindowSettings()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var settings = JsonSerializer.Deserialize<WindowSettings>(json);

                    if (settings != null)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        Width = settings.Width > 300 ? settings.Width : 1240;
                        Height = settings.Height > 200 ? settings.Height : 750;

                        if (settings.Left >= SystemParameters.VirtualScreenLeft &&
                            settings.Top >= SystemParameters.VirtualScreenTop &&
                            settings.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                            settings.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
                        {
                            Left = settings.Left;
                            Top = settings.Top;
                        }

                        if (settings.IsMaximized) WindowState = WindowState.Maximized;
                    }
                }
            }
            catch { }
        }

        private void SaveWindowSettings()
        {
            try
            {
                var settings = new WindowSettings();
                if (WindowState == WindowState.Maximized)
                {
                    settings.Left = RestoreBounds.Left;
                    settings.Top = RestoreBounds.Top;
                    settings.Width = RestoreBounds.Width;
                    settings.Height = RestoreBounds.Height;
                    settings.IsMaximized = true;
                }
                else
                {
                    settings.Left = Left;
                    settings.Top = Top;
                    settings.Width = Width;
                    settings.Height = Height;
                    settings.IsMaximized = false;
                }

                if (!Directory.Exists(AppFolder)) Directory.CreateDirectory(AppFolder);
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
        #endregion

        #region Auto Copy & Mouse Hook
        private async Task OnMouseUpDetectedAsync()
        {
            WebView2? activeWebViewer = GetActiveWebViewer();
            if (ChkAutoCopy.IsChecked != true || activeWebViewer == null || activeWebViewer.Source == null) return;
            if (!IsMouseOverActiveWebViewer(activeWebViewer)) return;

            await Task.Delay(80);
            SimulateCtrlC();
            await Task.Delay(60);

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();
                if (!string.IsNullOrEmpty(text) && text != _lastCopiedText)
                {
                    _lastCopiedText = text;
                    ShowToast(text);
                }
            }
        }

        private bool IsMouseOverActiveWebViewer(WebView2 activeWebViewer)
        {
            try
            {
                if (GetCursorPos(out POINT p))
                {
                    Point relativePoint = activeWebViewer.PointFromScreen(new Point(p.X, p.Y));
                    return relativePoint.X >= 0 && relativePoint.X <= activeWebViewer.ActualWidth &&
                           relativePoint.Y >= 0 && relativePoint.Y <= activeWebViewer.ActualHeight;
                }
            }
            catch { }
            return false;
        }

        private static void SimulateCtrlC()
        {
            const byte VK_CONTROL = 0x11;
            const byte VK_C = 0x43;
            const uint KEYEVENTF_KEYUP = 0x0002;

            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using Process curProcess = Process.GetCurrentProcess();
            ProcessModule? curModule = curProcess.MainModule;
            return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule?.ModuleName), 0);
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONUP)
            {
                if (_instance != null && _instance.IsActive)
                {
                    _instance.Dispatcher.InvokeAsync(async () =>
                    {
                        await _instance.OnMouseUpDetectedAsync();
                    });
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONUP = 0x0202;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        #endregion

        #region Toast Notification System
        private void InitializeToastTimer()
        {
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _toastTimer.Tick += (s, e) => { ToastNotification.Visibility = Visibility.Collapsed; _toastTimer?.Stop(); };

            _skuToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _skuToastTimer.Tick += (s, e) => { SkuToastNotification.Visibility = Visibility.Collapsed; _skuToastTimer?.Stop(); };
        }

        private void ShowToast(string text)
        {
            TxtToastContent.Text = text;
            ToastNotification.Visibility = Visibility.Visible;
            _toastTimer?.Stop();
            _toastTimer?.Start();
        }

        private void ShowSkuToast(string text)
        {
            TxtSkuToastContent.Text = text;
            SkuToastNotification.Visibility = Visibility.Visible;
            _skuToastTimer?.Stop();
            _skuToastTimer?.Start();
        }
        #endregion
    }
}