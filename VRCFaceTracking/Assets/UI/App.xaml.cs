using System;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using VRCFaceTracking.CommandLine;

namespace VRCFaceTracking.Assets.UI
{
    public partial class App
    {
        public App()
        {
            try
            {
                BindingOperations.EnableCollectionSynchronization(Logger.ConsoleOutput, Logger.ConsoleLock);
                ParseArgs();
            }
            catch ( Exception e ) 
            {
                string filePath = Utils.PersistentDataDirectory + "/ExceptionLog.txt";

                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    writer.WriteLine("Date : " + DateTime.Now.ToString() + "\n");

                    while (e != null)
                    {
                        writer.WriteLine
                        (
                            e.GetType().FullName + "\n" + 
                            "Message : " + e.Message + "\n" + 
                            "StackTrace : " + e.StackTrace
                        );

                        e = e.InnerException;
                    }

                    writer.Close();
                }

                Logger.Error(e.Message);
                Logger.Msg("Please restart VRCFaceTracking to reinitialize the application.");
            }
        }

        private static void ParseArgs()
        {
            var args = Environment.GetCommandLineArgs();
            
            var ip = new Option<string>("--osc", 
                () => "9000:127.0.0.1:9001",
                "IP address to send tracking data to.");
            
            var disableEye = new Option<bool>("--disable-eye", 
                () => false,
                "Disable eye tracking.");
            
            var disableExpression = new Option<bool>("--disable-expression", 
                () => false,
                "Disable expression tracking.");

            // Parse using our ArgumentsBinder class
            var rootCommand = new RootCommand
            {
                ip,
                disableEye,
                disableExpression
            };

            rootCommand.SetHandler(MainStandalone.Initialize, new ArgumentsBinder(ip, disableEye, disableExpression));
            
            // We want to ignore any unknown arguments
            rootCommand.TreatUnmatchedTokensAsErrors = false;
            
            rootCommand.Invoke(args);
        }

        ~App() => MainStandalone.Teardown();

        private void App_OnExit(object sender, ExitEventArgs e) => MainStandalone.Teardown();
    }
}