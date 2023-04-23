using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using VRCFaceTracking;

namespace ALXRTrackingInterface.UI
{
    public enum RunMethod
    {
        None,
        LibALXR, // Local
        ALXRClient
    };

    public struct ALXRClientConfig
    {
        [JsonConverter(typeof(IPAddressJsonConverter))]
        public IPAddress ClientIpAddress;
    }

    public struct LibALXRConfig
    {
        [JsonInclude]
        public bool VerboseLogs;
        [JsonInclude]
        public bool EnableHandleTracking;
        [JsonInclude]
        public bool HeadlessSession;
        [JsonInclude]
        public ALXRGraphicsApi GraphicsApi;
        [JsonInclude]
        public ALXREyeTrackingType EyeTrackingExt;
        [JsonInclude]
        public ALXRFacialExpressionType FacialTrackingExt;
    }

    public sealed class RunConfig
    {
        public static readonly IPAddress LocalHost = new IPAddress(new byte[] { 127, 0, 0, 1 });

        [JsonInclude]
        public RunMethod RunMethod = RunMethod.LibALXR;

        [JsonInclude]
        public ALXRClientConfig ALXRClientConfig = new ALXRClientConfig
        {
            ClientIpAddress = LocalHost
        };

        [JsonInclude]
        public LibALXRConfig LibALXRConfig = new LibALXRConfig
        {
            VerboseLogs = false,
            EnableHandleTracking = false,
            HeadlessSession = true,
            GraphicsApi = ALXRGraphicsApi.Auto,
            EyeTrackingExt = ALXREyeTrackingType.Auto,
            FacialTrackingExt = ALXRFacialExpressionType.Auto
        };

        public string ToJsonString() =>
            JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = true
            });

        public void WriteJsonFile(string filename) =>
            WriteJsonFile(this, filename);

        public static void WriteJsonFile(RunConfig config, string filename)
        {
            if (config == null)
                return;
            var jsonStr = config.ToJsonString();
            File.WriteAllText(filename, jsonStr);
        }

        public static RunConfig ReadJsonFile(string filename)
        {
            var jsonStr = File.ReadAllText(filename);
            return JsonSerializer.Deserialize<RunConfig>(jsonStr, new JsonSerializerOptions
            {
                IncludeFields = true
            });
        }
    }

    /// <summary>
    /// Interaction logic for ConfigControl.xaml
    /// </summary>
    public partial class ConfigControl : UserControl
    {
        public ConfigControl()
        {
            InitializeComponent();
            ClientIPAddress = RunConfig.LocalHost;
        }

        public event RoutedEventHandler ConnectToClientClick;
        public event RoutedEventHandler RunALXRClientClick;
        public event RoutedEventHandler StopRunTaskClick;

        public string ConfigFilename { get; set; } = 
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "ALXRConfig.json");

        public RunConfig RunConfig
        {
            get
            {
                var runConfig = new RunConfig();
                runConfig.RunMethod = this.OnStartup;
                runConfig.ALXRClientConfig.ClientIpAddress = ClientIPAddress;
                runConfig.LibALXRConfig = this.LibALXRConfig;
                return runConfig;
            }
            set
            {
                if (value == null)
                    return;
                OnStartup = value.RunMethod;
                ClientIPAddress = value.ALXRClientConfig.ClientIpAddress;
                LibALXRConfig = value.LibALXRConfig;
            }
        }

        public IPAddress ClientIPAddress
        {
            get => ClientIPAddressControl.IPAddress;
            set => ClientIPAddressControl.IPAddress = value;
        }

        public RunMethod OnStartup
        {
            get
            {
                if (ALXRClientRadioButton.IsChecked == true)
                    return RunMethod.ALXRClient;
                if (LibALXRRadioButton.IsChecked == true)
                    return RunMethod.LibALXR;
                Debug.Assert(NoRunOnStartupRadioButton.IsChecked == true);
                return RunMethod.None;

            }
            set
            {
                NoRunOnStartupRadioButton.IsChecked = false;
                ALXRClientRadioButton.IsChecked = false;
                LibALXRRadioButton.IsChecked = false;
                switch (value)
                {
                    case RunMethod.LibALXR:
                        LibALXRRadioButton.IsChecked = true;
                        break;
                    case RunMethod.ALXRClient:
                        ALXRClientRadioButton.IsChecked = true;
                        break;
                    case RunMethod.None:
                    default:
                        NoRunOnStartupRadioButton.IsChecked = true;
                        break;
                }
            }
        }

        public LibALXRConfig LibALXRConfig
        {
            get => new LibALXRConfig
            {
                VerboseLogs = this.VerboseLogs,
                EnableHandleTracking = this.EnableHandleTracking,
                HeadlessSession = this.HeadlessSession,
                GraphicsApi = this.GraphicsApi,
                FacialTrackingExt = this.FacialTrackingExt,
                EyeTrackingExt = this.EyeTrackingExt,
            };
            
            set
            {
                VerboseLogs = value.VerboseLogs;
                EnableHandleTracking = value.EnableHandleTracking;
                HeadlessSession = value.HeadlessSession;
                GraphicsApi = value.GraphicsApi;
                FacialTrackingExt = value.FacialTrackingExt;
                EyeTrackingExt = value.EyeTrackingExt;
            }
        }

        public ALXRClientConfig ALXRClientConfig
        {
            get => new ALXRClientConfig
            {
                ClientIpAddress = this.ClientIPAddress
            };
            set => this.ClientIPAddress = value.ClientIpAddress;
        }

        public bool VerboseLogs
        {
            get => VerboseLogsCheckBox.IsChecked == true;
            set => VerboseLogsCheckBox.IsChecked = value;
        }

        public bool EnableHandleTracking
        {
            get => HandTrackingCheckBox.IsChecked == true;
            set => HandTrackingCheckBox.IsChecked = value;
        }

        public bool HeadlessSession
        {
            get => HeadlessCheckBox.IsChecked == true;
            set => HeadlessCheckBox.IsChecked = value;
        }

        public ALXRGraphicsApi GraphicsApi
        {
            get
            {
                if (GraphicsApiComboBox.SelectedIndex < 0)
                    return ALXRGraphicsApi.Auto;
                return (ALXRGraphicsApi)GraphicsApiComboBox.SelectedIndex;
            }
            set => GraphicsApiComboBox.SelectedIndex = (int)value;
        }

        public ALXRFacialExpressionType FacialTrackingExt
        {
            get
            {
                if (FacialTrackingComboBox.SelectedIndex < 0)
                    return ALXRFacialExpressionType.Auto;
                return (ALXRFacialExpressionType)FacialTrackingComboBox.SelectedIndex;
            }
            set => FacialTrackingComboBox.SelectedIndex = (int)value;
        }

        public ALXREyeTrackingType EyeTrackingExt
        {
            get
            {
                if (EyeTrackingComboBox.SelectedIndex < 0)
                    return ALXREyeTrackingType.Auto;
                return (ALXREyeTrackingType)EyeTrackingComboBox.SelectedIndex;
            }
            set => EyeTrackingComboBox.SelectedIndex = (int)value;
        }

        public string ActiveRuntime
        {
            get { return (string)ActiveRuntimeLabel.Content; }
            set
            {
                if (string.IsNullOrEmpty(value))
                    value = "Unknown";
                ActiveRuntimeLabel.Content = value;
            }
        }

        public void SaveConfig()
        {
            try
            {
                var configFile = ConfigFilename;
                Logger.Msg($"Writing alxr-config: {configFile}");

                var config = this.RunConfig;
                config.WriteJsonFile(configFile);
                
                Logger.Msg($"alxr-config successfully written.");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to write alxr-config, reason: {ex.Message}");
            }
        }

        public void LoadConfig()
        {
            try
            {
                var configFile = ConfigFilename;
                Logger.Msg($"Attempting to load config file: {configFile}");
                if (!File.Exists(configFile))
                {
                    Logger.Warning($"Failed to find alxr-config json, file doest not exist.");
                    return;
                }
                this.RunConfig = RunConfig.ReadJsonFile(configFile);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read alxr-config, reason: {ex.Message}");
            }
        }

        private void ConnectToClientButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConnectToClientClick != null)
            {
                ConnectToClientClick(sender, e);
            }
        }

        private void resetButton_Click(object sender, RoutedEventArgs e)
        {
            ClientIPAddressControl.Reset();
        }

        private void RunLibALXRButton_Click(object sender, RoutedEventArgs e)
        {
            if (RunALXRClientClick != null)
            {
                RunALXRClientClick(sender, e);
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (StopRunTaskClick != null)
            {
                StopRunTaskClick(sender, e);
            }
        }

        private void ResetButton_Click_1(object sender, RoutedEventArgs e)
        {
            RunConfig = new RunConfig();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
        }
    }
}
