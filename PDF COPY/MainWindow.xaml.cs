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
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace EdgePdfReaderPro;

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
    public double Width { get; set; } = 1200;
    public double Height { get; set; } = 750;
    public bool IsMaximized { get; set; } = false;
}

public partial class MainWindow : Window
{
    private DispatcherTimer? _toastTimer;
    private string _lastCopiedText = string.Empty;
    private readonly string? _initialPdfPath;

    public ObservableCollection<SkuItem> SkuList { get; set; } = new();

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EdgePdfReaderPro",
        "window_settings.json"
    );

    private static readonly LowLevelMouseProc Proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;
    private static MainWindow? _instance;

    public MainWindow()
    {
        InitializeComponent();
        _instance = this;

        LstSkuItems.ItemsSource = SkuList;

        LoadWindowSettings();
        InitializeToastTimer();
        InitializeWebViewEngine();

        _hookID = SetHook(Proc);
        Closing += (s, e) =>
        {
            SaveWindowSettings();
            UnhookWindowsHookEx(_hookID);
        };
    }

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
        OpenFileDialog openFileDialog = new()
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

    #region Show / Hide SKU Summary Panel
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
        }
    }

    private void BtnDecrement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SkuItem item && item.Quantity > 0)
        {
            item.Quantity--;
        }
    }

    private void BtnDeleteSku_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SkuItem item)
        {
            SkuList.Remove(item);
        }
    }

    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        Regex regex = new Regex("[^0-9]+");
        e.Handled = regex.IsMatch(e.Text);
    }

    private void BtnExportSku_Click(object sender, RoutedEventArgs e)
    {
        var activeSkus = SkuList
            .Where(x => x.Quantity > 0)
            .OrderByDescending(x => x.Quantity)
            .ThenBy(x => x.Name)
            .ToList();

        if (!activeSkus.Any())
        {
            MessageBox.Show("There are no SKUs with a quantity greater than 0 to copy.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StringBuilder sb = new();
        foreach (var sku in activeSkus)
        {
            sb.AppendLine($"{sku.Name.ToUpper()} x{sku.Quantity}");
        }

        Clipboard.SetText(sb.ToString().TrimEnd());
        ShowToast($"{activeSkus.Count} SKU summary copied successfully!");
    }

    private void BtnResetQty_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in SkuList)
        {
            item.Quantity = 0;
        }
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
    }

    private void BtnConfirmClearNo_Click(object sender, RoutedEventArgs e)
    {
        ConfirmClearOverlay.Visibility = Visibility.Collapsed;
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
                    Width = settings.Width > 300 ? settings.Width : 1200;
                    Height = settings.Height > 200 ? settings.Height : 750;

                    if (settings.Left >= SystemParameters.VirtualScreenLeft &&
                        settings.Top >= SystemParameters.VirtualScreenTop &&
                        settings.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                        settings.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
                    {
                        Left = settings.Left;
                        Top = settings.Top;
                    }

                    if (settings.IsMaximized)
                    {
                        WindowState = WindowState.Maximized;
                    }
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

            string? dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
    #endregion

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

    #region Win32 P/Invoke & Mouse Hook
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
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    #endregion

    #region Toast Notification System
    private void InitializeToastTimer()
    {
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _toastTimer.Tick += (s, e) =>
        {
            ToastNotification.Visibility = Visibility.Collapsed;
            _toastTimer?.Stop();
        };
    }

    private void ShowToast(string text)
    {
        TxtToastContent.Text = text;
        ToastNotification.Visibility = Visibility.Visible;
        _toastTimer?.Stop();
        _toastTimer?.Start();
    }
    #endregion
}