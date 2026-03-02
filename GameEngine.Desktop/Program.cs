using System;
using System.Diagnostics;
using Photino.NET;

namespace GameEngine.Desktop
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // 1. Launch the hidden C# Backend Web Server
            var apiProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = @"Api\GameEngine.Api.exe",
                    WorkingDirectory = "Api",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    EnvironmentVariables = { ["ASPNETCORE_ENVIRONMENT"] = "Production" }
                }
            };
            
            try 
            {
                apiProcess.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to start background game engine: " + ex.Message);
            }

            // 2. Launch the Native OS Window pointing to the Backend Web Server
            string windowTitle = "Double Deck Cancellation Hearts";
            var window = new PhotinoWindow()
                .SetTitle(windowTitle)
                .SetUseOsDefaultSize(false)
                .SetSize(1280, 800)
                .SetMaximized(true)
                .Center()
                .SetResizable(true)
                .Load($"http://localhost:5243/?t={DateTime.Now.Ticks}");

            // 3. Register a Web Message receiver to listen to commands from the JavaScript UI
            window.RegisterWebMessageReceivedHandler((object sender, string message) =>
            {
                if (message == "quit")
                {
                    window.Close();
                }
            });

            // 4. Keep the API running while the Window is open, then kill it when we exit.
            window.RegisterWindowClosingHandler((object sender, EventArgs e) =>
            {
                if (!apiProcess.HasExited)
                {
                    apiProcess.Kill();
                }
                return false; 
            });

            // Block thread to keep Window alive
            window.WaitForClose();
        }
    }
}
