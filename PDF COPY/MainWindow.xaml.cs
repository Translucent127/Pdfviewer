using System;
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
            InitializeWebViewEngine();

            SwitchTab(isSkuTab: true);

            _hookID = SetHook(Proc);
            Closing += (s, e) =>
            {
                SaveWindowSettings();
                SaveAppData();
                UnhookWindowsHookEx(_hookID);
            };
        }

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

        #region Engine Initialization & External Navigation Blocking
        private async void InitializeWebViewEngine()
        {
            await PdfWebViewer.EnsureCoreWebView2Async(null);

            PdfWebViewer.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            PdfWebViewer.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

            if (!string.IsNullOrEmpty(_initialPdfPath) && File.Exists(_initialPdfPath))
            {
                OpenPdfFile(_initialPdfPath);
            }
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        }

        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }
        #endregion

        #region Open & Reset PDF Features
        private void OpenPdfFile(string filePath)
        {
            TxtFileName.Text = Path.GetFileName(filePath);
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            PdfWebViewer.CoreWebView2.Navigate(new Uri(filePath).AbsoluteUri);
        }

        private void BtnOpenPdf_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Select PDF File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                OpenPdfFile(openFileDialog.FileName);
            }
        }

        private void BtnResetPdf_Click(object sender, RoutedEventArgs e)
        {
            TxtFileName.Text = string.Empty;
            PlaceholderPanel.Visibility = Visibility.Visible;
            _lastCopiedText = string.Empty;

            if (PdfWebViewer.CoreWebView2 != null)
            {
                PdfWebViewer.CoreWebView2.Navigate("about:blank");
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

                // 1. Simpan Daftar SKU Summary
                string skuJson = JsonSerializer.Serialize(SkuList, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SkuDataPath, skuJson, Encoding.UTF8);

                // 2. Simpan Teks Log Notepad
                File.WriteAllText(NotesDataPath, TxtScratchpad.Text, Encoding.UTF8);
            }
            catch { }
        }

        private void LoadAppData()
        {
            try
            {
                // 1. Muat Daftar SKU Summary
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

                // 2. Muat Teks Log Notepad
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
            if (ChkAutoCopy.IsChecked != true || PdfWebViewer.Source == null) return;
            if (!IsMouseOverWebViewer()) return;

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

        private bool IsMouseOverWebViewer()
        {
            try
            {
                if (GetCursorPos(out POINT p))
                {
                    Point relativePoint = PdfWebViewer.PointFromScreen(new Point(p.X, p.Y));
                    return relativePoint.X >= 0 && relativePoint.X <= PdfWebViewer.ActualWidth &&
                           relativePoint.Y >= 0 && relativePoint.Y <= PdfWebViewer.ActualHeight;
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