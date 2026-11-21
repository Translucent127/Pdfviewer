using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PDF_COPY
{
    public partial class App : Application
    {
        private const string MutexName = "PDF_COPY_SingleInstance_Mutex";
        private const string PipeName = "PDF_COPY_SingleInstance_Pipe";
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // Jika aplikasi sudah berjalan, kirim path PDF ke instance pertama lalu tutup instance ini
                if (e.Args.Length > 0 && File.Exists(e.Args[0]))
                {
                    SendArgsToFirstInstance(e.Args[0]);
                }
                Shutdown();
                return;
            }

            // Instance pertama: Jalankan listener Named Pipe di latar belakang
            Task.Run(ListenForOtherInstances);

            base.OnStartup(e);
        }

        private static void SendArgsToFirstInstance(string filePath)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1000);
                using var writer = new StreamWriter(client);
                writer.WriteLine(filePath);
                writer.Flush();
            }
            catch { }
        }

        private void ListenForOtherInstances()
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    string? filePath = reader.ReadLine();

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (MainWindow is MainWindow mainWin)
                            {
                                mainWin.OpenPdfInNewTab(filePath);
                                if (mainWin.WindowState == WindowState.Minimized)
                                {
                                    mainWin.WindowState = WindowState.Normal;
                                }
                                mainWin.Activate();
                            }
                        });
                    }
                }
                catch { }
            }
        }
    }
}