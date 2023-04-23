using ALXRTrackingInterface.UI;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using VRCFaceTracking;
using VRCFaceTracking.Params;

namespace ALXRTrackingInterface
{
    using RunAction = Action<System.Threading.CancellationToken>;

    public sealed class ALXRTrackingInterface : ExtTrackingModule
    {
        public const int PORT = 13191;

        private bool eyeActive;
        private bool lipActive;

        private CancellationTokenSource runTokenSource = new CancellationTokenSource();
        private ManualResetEvent exitEvent = new ManualResetEvent(true);

        private ConfigControl configControl;

        private static readonly string[] ALXRLogTag = new string[] { "[LibALXR]", "[ALXR-client]" };

        
        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
        {
            // Using these to determine if we should be sending eye updates and/or lip updates to VRCFT.
            // Will allow other modules such as SRanipal to overlap should we want that (in the case of using the Facial Tracker in place of the Quest's lip tracker).
            eyeActive = eyeAvailable;
            lipActive = expressionAvailable;

            LoadConfigControl();

            return (eyeActive, lipActive);
        }

        private void LoadConfigControl()
        {
            var configUITask = Application.Current.Dispatcher.InvokeAsync(new Func<RunConfig>(() =>
            {
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow == null)
                {
                    Logger.Error("Failed to find main application window!");
                    return null;
                }

                var tabControl = UI.UIHelper.FindTabControl(mainWindow);
                if (tabControl == null)
                {
                    Logger.Error("Failed to find main application tab control!");
                    return null;
                }

                configControl = new ConfigControl();
                configControl.LoadConfig();

                configControl.ConnectToClientClick += ConfigControl_ConnectToClientClick;
                configControl.RunALXRClientClick += ConfigControl_RunALXRClientClick;
                configControl.StopRunTaskClick += ConfigControl_StopRunTaskClick;

                var newTabItem = new TabItem();
                if (tabControl.Items.Count > 0)
                {
                    var mainTab = tabControl.Items[0] as TabItem;
                    if (mainTab != null) {
                        Debug.Assert((mainTab.Header as string).ToLower() == "main");
                        newTabItem.Style = new Style(typeof(TabItem), mainTab.Style);
                        newTabItem.Foreground = mainTab.Foreground;
                    }
                }
                newTabItem.Header = "ALXR Module Config";
                newTabItem.Content = configControl;
                tabControl.Items.Add(newTabItem);

                return configControl.RunConfig;
            }));
            configUITask.Wait();
            var runConfig = configUITask.Result;
            SetRunAction(GetRunAction(runConfig));
        }

        private void SetActiveRuntimeLabel(string rtName)
        {
            var runtimeName = rtName.Clone() as string;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (configControl != null)
                {
                    configControl.ActiveRuntime = runtimeName;
                }
            }));
        }

        private RunAction GetRunAction(RunConfig runConfig)
        {
            if (runConfig == null)
                return null;
            switch (runConfig.RunMethod)
            {
                case RunMethod.ALXRClient:
                    var alxrClientConfig = runConfig.ALXRClientConfig;
                    return tok => RemoteRun(alxrClientConfig, tok);
                case RunMethod.LibALXR:
                    var libALXRConfig = runConfig.LibALXRConfig;
                    return tok => LocalRun(libALXRConfig, tok);
                default: return null;
            }
        }

        private void ConfigControl_StopRunTaskClick(object sender, RoutedEventArgs e) => CancelRunAction();

        private void ConfigControl_RunALXRClientClick(object sender, RoutedEventArgs e)
        {
            Debug.Assert(configControl != null);
            var libALXRConfig = configControl.LibALXRConfig;
            SetRunAction(tok => LocalRun(libALXRConfig, tok));
            SwitchToMainTab();
        }

        private void ConfigControl_ConnectToClientClick(object sender, RoutedEventArgs e)
        {
            Debug.Assert(configControl != null);
            var alxrClientConfig = configControl.ALXRClientConfig;
            SetRunAction(tok => RemoteRun(alxrClientConfig, tok));
            SwitchToMainTab();
        }

        private void SwitchToMainTab() => UIHelper.SwitchToMainTab(configControl);

        private bool SetRunAction(RunAction newAction)
        {
            if (newAction == null)
                return false;
            CancelRunAction();
            runTokenSource = new CancellationTokenSource();
            exitEvent.Reset();
            runQueue.Add(newAction);
            return true;
        }

        private void CancelRunAction()
        {
            runTokenSource.Cancel();
            if (!exitEvent.WaitOne(5000))
            {
                Logger.Warning("Waiting for current task to finish took to long.");
            }
        }

        #region LocalRun (LibALXR)
        private static ALXRRustCtx CreateALXRCtx(ref LibALXRConfig config)
        {
            return new ALXRRustCtx
            {
                inputSend = (ref TrackingInfo data) => { },
                viewsConfigSend = (ref ALXREyeInfo eyeInfo) => { },
                pathStringToHash = (path) => { return (ulong)path.GetHashCode(); },
                timeSyncSend = (ref TimeSync data) => { },
                videoErrorReportSend = () => { },
                batterySend = (a, b, c) => { },
                setWaitingNextIDR = a => { },
                requestIDR = () => { },
                graphicsApi = config.GraphicsApi,
                decoderType = ALXRDecoderType.D311VA,
                displayColorSpace = ALXRColorSpace.Default,
                facialTracking = config.FacialTrackingExt,
                eyeTracking = config.EyeTrackingExt,
                verbose = config.VerboseLogs,
                disableLinearizeSrgb = false,
                noSuggestedBindings = true,
                noServerFramerateLock = false,
                noFrameSkip = false,
                disableLocalDimming = true,
                headlessSession = config.HeadlessSession,
                noFTServer = true,
                noPassthrough = true,
                noHandTracking = true, //!config.EnableHandleTracking, temp disabled for future OSC supprot.
                firmwareVersion = new ALXRVersion
                {
                    // only relevant for android clients.
                    major = 0,
                    minor = 0,
                    patch = 0
                }
            };
        }

        private void LocalRun(LibALXRConfig config, CancellationToken cancellationToken)
        {
            if (!LibALXR.AddDllSearchPath())
            {
                Logger.Error($"{ALXRLogTag[0]} libalxr library path to search failed to be set.");
                return;
            }

            try
            {
                LibALXR.alxr_set_log_custom_output(ALXRLogOptions.None, (level, output, len) =>
                {
                    var fullMsg = $"{ALXRLogTag[0]} {output}";
                    switch (level)
                    {
                        case ALXRLogLevel.Info:
                            Logger.Msg(fullMsg);
                            break;
                        case ALXRLogLevel.Warning:
                            Logger.Warning(fullMsg);
                            break;
                        case ALXRLogLevel.Error:
                            Logger.Error(fullMsg);
                            break;
                    }
                });

                while (!cancellationToken.IsCancellationRequested)
                {
                    var ctx = CreateALXRCtx(ref config);
                    var sysProperties = new ALXRSystemProperties();
                    if (!LibALXR.alxr_init(ref ctx, out sysProperties))
                    {
                        break;
                    }

                    SetActiveRuntimeLabel(sysProperties.systemName);

                    var newPacket = new ALXRFacialEyePacket();
                    var processFrameResult = new ALXRProcessFrameResult
                    {
                        exitRenderLoop = false,
                        requestRestart = false,
                    };
                    unsafe
                    {
                        processFrameResult.newPacket = &newPacket;
                    }
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        processFrameResult.exitRenderLoop = false;
                        LibALXR.alxr_process_frame2(ref processFrameResult);
                        if (processFrameResult.exitRenderLoop)
                        {
                            break;
                        }
                        
                        unsafe
                        {
                            UpdateData(ref *processFrameResult.newPacket);
                        }

                        if (!LibALXR.alxr_is_session_running())
                        {
                            // Throttle loop since xrWaitFrame won't be called.
                            Thread.Sleep(250);
                        }
                    }

                    LibALXR.alxr_destroy();

                    if (!processFrameResult.requestRestart)
                    {
                        break;
                    }
                }
            }
            finally
            {
                LibALXR.alxr_destroy();
            }
        }
        #endregion

        #region RemoteRun (Any ALXR Client)
        private static async Task<TcpClient> ConnectToServerAsync(IPAddress localAddr, int port, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var client = new TcpClient();
                    client.NoDelay = true;
                    Logger.Msg($"{ALXRLogTag[1]} Attempting to establish a connection at {localAddr}:{port}...");
                    await client.ConnectAsync(localAddr, port).WithCancellation(cancellationToken);
                    Logger.Msg($"{ALXRLogTag[1]} Successfully connected to ALXR client: {localAddr}:{port}");
                    return client;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is ObjectDisposedException))
                {
                    Logger.Error($"{ALXRLogTag[1]} Error connecting to {localAddr}:{port}: {ex.Message}");
                }
                await Task.Delay(1000, cancellationToken);
            }
            return null;
        }

        private byte[] rawExprBuffer = new byte[Marshal.SizeOf<ALXRFacialEyePacket>()];
        private async Task<ALXRFacialEyePacket> ReadALXRFacialEyePacketAsync(NetworkStream stream, System.Threading.CancellationToken cancellationToken)
        {
            Debug.Assert(stream != null && stream.CanRead);
            
            int offset = 0;
            int readBytes = 0;
            do
            {
                readBytes = await stream.ReadAsync(rawExprBuffer, offset, rawExprBuffer.Length - offset, cancellationToken);
                offset += readBytes;
            }
            while (readBytes > 0 && offset < rawExprBuffer.Length &&
                    !cancellationToken.IsCancellationRequested);

            if (offset < rawExprBuffer.Length)
                throw new Exception("Failed read packet.");
            return ALXRFacialEyePacket.ReadPacket(rawExprBuffer);

        }

        private void RemoteRun(ALXRClientConfig config, CancellationToken cToken)
        {
            try
            {
                var clientAddress = config.ClientIpAddress;
                if (clientAddress == null)
                {
                    Logger.Error($"{ALXRLogTag[1]} client IP address is null or invalid.");
                    return;
                }

                Task.Run(async () =>
                {
                    while (!cToken.IsCancellationRequested)
                    {
                        try
                        {
                            using (var newTcpClient = await ConnectToServerAsync(clientAddress, PORT, cToken))
                            {
                                if (newTcpClient == null || !newTcpClient.Connected)
                                    throw new Exception($"Error connecting to {clientAddress}:{PORT}");

                                using (var stream = newTcpClient.GetStream())
                                {
                                    if (stream == null)
                                        throw new Exception($"Error connecting to {clientAddress}:{PORT}");

                                    while (!cToken.IsCancellationRequested && stream.CanRead)
                                    {
                                        var newPacket = await ReadALXRFacialEyePacketAsync(stream, cToken);
                                        UpdateData(ref newPacket);
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"{ALXRLogTag[1]} {e.Message}");
                            Logger.Warning($"{ALXRLogTag[1]} End of stream!");
                        }
                    }
                }, cToken).Wait();
            }
            catch (Exception e)
            {
                Logger.Error($"{ALXRLogTag[1]} {e.Message}");
            }
        }
        #endregion

        private readonly BlockingCollection<RunAction> runQueue = new BlockingCollection<RunAction>(1);
        private CancellationTokenSource runQueueTokenSource = new CancellationTokenSource();

        public override Action GetUpdateThreadFunc()
        {
            return () =>
            {
                try
                {
                    foreach (var RunFn in runQueue.GetConsumingEnumerable(runQueueTokenSource.Token))
                    {
                        try
                        {
                            Debug.Assert(RunFn != null);
                            RunFn(runTokenSource.Token);
                        }
                        finally
                        {
                            exitEvent.Set();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            };
        }

        #region UpdateData Functions

        private void UpdateData(ref ALXRFacialEyePacket newPacket)
        {
            if (eyeActive)
            {
                UpdateEyeData(ref UnifiedTracking.Data.Eye, ref newPacket);
                UpdateEyeExpressions(ref UnifiedTracking.Data.Shapes, ref newPacket);
            }
            if (lipActive)
                UpdateMouthExpressions(ref UnifiedTracking.Data.Shapes, ref newPacket);
        }

        private void UpdateEyeData(ref UnifiedEyeData eye, ref ALXRFacialEyePacket packet)
        {
            switch (packet.eyeTrackerType)
            {
            case ALXREyeTrackingType.FBEyeTrackingSocial:
                UpdateEyeDataFB(ref eye, ref packet);
                break;
            case ALXREyeTrackingType.ExtEyeGazeInteraction:
                UpdateEyeDataEyeGazeEXT(ref eye, ref packet);
                break;
            }
        }

        private void UpdateEyeExpressions(ref UnifiedExpressionShape[] unifiedExpressions, ref ALXRFacialEyePacket packet)
        {
            switch (packet.expressionType)
            {
            case ALXRFacialExpressionType.FB:
                UpdateEyeExpressionsFB(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan);
                break;
            case ALXRFacialExpressionType.HTC:
                UpdateEyeExpressionsHTC(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan);
                break;
            }
        }

        private void UpdateMouthExpressions(ref UnifiedExpressionShape[] unifiedExpressions, ref ALXRFacialEyePacket packet)
        {
            switch (packet.expressionType)
            {
            case ALXRFacialExpressionType.FB:
                UpdateEyeExpressionsFB(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan);
                break;
            case ALXRFacialExpressionType.HTC:
                UpdateMouthExpressionsHTC(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan.Slice(14)); //packet.expressionWeights.AsSpan(14));
                break;
            }
        }

        #region XR_EXT_eye_gaze_interaction Update Function
        private void UpdateEyeDataEyeGazeEXT(ref UnifiedEyeData eye, ref ALXRFacialEyePacket packet)
        {
            unsafe
            {
                Debug.Assert(packet.eyeTrackerType == ALXREyeTrackingType.ExtEyeGazeInteraction);

                #region Eye Data parsing
                eye.Left.Openness = 1.0f;
                eye.Right.Openness = 1.0f;
                #endregion

                #region Eye Data to UnifiedEye

                //Porting of eye tracking parameters
                eye.Left.Gaze = new Vector2(
                    packet.eyeGazePose0.position.x,
                    packet.eyeGazePose0.position.y
                );
                eye.Right.Gaze = new Vector2(
                    packet.eyeGazePose1.position.x,
                    packet.eyeGazePose1.position.y
                );

                // Eye dilation code, automated process maybe?
                eye.Left.PupilDiameter_MM = 5f;
                eye.Right.PupilDiameter_MM = 5f;

                // Force the normalization values of Dilation to fit avg. pupil values.
                eye._minDilation = 0;
                eye._maxDilation = 10;

                #endregion

            }
        }
        #endregion

        #region HTC Facial Update Functions
        private void UpdateEyeExpressionsHTC(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressionWeights)
        {
            unifiedExpressions[(int)UnifiedExpressions.EyeWideLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftWide];
            unifiedExpressions[(int)UnifiedExpressions.EyeWideRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightWide];;

            unifiedExpressions[(int)UnifiedExpressions.EyeSquintLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftSqueeze];;
            unifiedExpressions[(int)UnifiedExpressions.EyeSquintRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightSqueeze];;

            // Emulator expressions for Unified Expressions. These are essentially already baked into Legacy eye expressions (SRanipal)
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftWide];
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftWide];

            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightWide];;
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightWide];;

            unifiedExpressions[(int)UnifiedExpressions.BrowPinchLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftSqueeze];;
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererLeft].Weight = expressionWeights[(int)XrEyeExpressionHTC.LeftSqueeze];;

            unifiedExpressions[(int)UnifiedExpressions.BrowPinchRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightSqueeze];;
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererRight].Weight = expressionWeights[(int)XrEyeExpressionHTC.RightSqueeze];;
        }

        private void UpdateMouthExpressionsHTC(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressionWeights)
        {
            #region Direct Jaw

            unifiedExpressions[(int)UnifiedExpressions.JawOpen].Weight = expressionWeights[(int)XrLipExpressionHTC.JawOpen] + expressionWeights[(int)XrLipExpressionHTC.MouthApeShape];
            unifiedExpressions[(int)UnifiedExpressions.JawLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.JawLeft];
            unifiedExpressions[(int)UnifiedExpressions.JawRight].Weight = expressionWeights[(int)XrLipExpressionHTC.JawRight];
            unifiedExpressions[(int)UnifiedExpressions.JawForward].Weight = expressionWeights[(int)XrLipExpressionHTC.JawForward];
            unifiedExpressions[(int)UnifiedExpressions.MouthClosed].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthApeShape];

            #endregion

            #region Direct Mouth and Lip

            // These shapes have overturns subtracting from them, as we are expecting the new standard to have Upper Up / Lower Down baked into the funneller shapes below these.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperUpRight] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperUpRight] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperUpLeft] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperUpLeft] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];

            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerDownLeft] - expressionWeights[(int)XrLipExpressionHTC.MouthLowerOverturn];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerDownRight] - expressionWeights[(int)XrLipExpressionHTC.MouthLowerOverturn];

            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthPout];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthPout];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthPout];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthPout];

            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperOverturn];

            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperInside];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperInside];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerInside];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerInside];

            unifiedExpressions[(int)UnifiedExpressions.MouthUpperLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthUpperRight];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerRight];

            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileRight];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileRight];
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSadLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSadRight];

            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserUpper].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerOverlay] - expressionWeights[(int)XrLipExpressionHTC.MouthUpperInside];
            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserLower].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthLowerOverlay];

            #endregion

            #region Direct Cheek

            unifiedExpressions[(int)UnifiedExpressions.CheekPuffLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.CheekPuffLeft];
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffRight].Weight = expressionWeights[(int)XrLipExpressionHTC.CheekPuffRight];

            unifiedExpressions[(int)UnifiedExpressions.CheekSuckLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.CheekSuck];
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckRight].Weight = expressionWeights[(int)XrLipExpressionHTC.CheekSuck];

            #endregion

            #region Direct Tongue

            unifiedExpressions[(int)UnifiedExpressions.TongueOut].Weight = (expressionWeights[(int)XrLipExpressionHTC.TongueLongStep1] + expressionWeights[(int)XrLipExpressionHTC.TongueLongStep2]) / 2.0f;
            unifiedExpressions[(int)UnifiedExpressions.TongueUp].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueUp];
            unifiedExpressions[(int)UnifiedExpressions.TongueDown].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueDown];
            unifiedExpressions[(int)UnifiedExpressions.TongueLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueLeft];
            unifiedExpressions[(int)UnifiedExpressions.TongueRight].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueRight];
            unifiedExpressions[(int)UnifiedExpressions.TongueRoll].Weight = expressionWeights[(int)XrLipExpressionHTC.TongueRoll];

            #endregion

            // These shapes are not tracked at all by SRanipal, but instead are being treated as enhancements to driving the shapes above.

            #region Emulated Unified Mapping

            unifiedExpressions[(int)UnifiedExpressions.CheekSquintLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileLeft];
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileRight];

            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileLeft];
            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSmileRight];

            unifiedExpressions[(int)UnifiedExpressions.MouthStretchLeft].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSadRight];
            unifiedExpressions[(int)UnifiedExpressions.MouthStretchRight].Weight = expressionWeights[(int)XrLipExpressionHTC.MouthSadRight];

            #endregion
        }
        #endregion

        #region FB Eye & Facial Update Functions   
        // Preprocess our expressions per the Meta Documentation
        private void UpdateEyeDataFB(ref UnifiedEyeData eye, ref ALXRFacialEyePacket packet)
        {
            unsafe
            {
                #region Eye Data parsing

                eye.Left.Openness = 1.0f - (float)Math.Max(0, Math.Min(1, packet.expressionWeights[(int)FBExpression.Eyes_Closed_L] + // Use eye closed full range
                    packet.expressionWeights[(int)FBExpression.Eyes_Closed_L] * (2f * packet.expressionWeights[(int)FBExpression.Lid_Tightener_L] / Math.Pow(2f, 2f * packet.expressionWeights[(int)FBExpression.Lid_Tightener_L])))); // Add lid tighener as the eye closes to help winking.

                eye.Right.Openness = 1.0f - (float)Math.Max(0, Math.Min(1, packet.expressionWeights[(int)FBExpression.Eyes_Closed_R] + // Use eye closed full range
                    packet.expressionWeights[(int)FBExpression.Eyes_Closed_R] * (2f * packet.expressionWeights[(int)FBExpression.Lid_Tightener_R] / Math.Pow(2f, 2f * packet.expressionWeights[(int)FBExpression.Lid_Tightener_R])))); // Add lid tighener as the eye closes to help winking.

                #endregion

                #region Eye Gaze parsing

                // pitch = 47(left)-- > -47(right)
                // yaw = -55(down)-- > 43(up)
                // Eye look angle (degrees) limits calibrated to SRanipal eye tracking

                var q = packet.eyeGazePose0.orientation;

                double yaw = Math.Atan2(2.0 * (q.y * q.z + q.w * q.x), q.w * q.w - q.x * q.x - q.y * q.y + q.z * q.z);
                double pitch = Math.Asin(-2.0 * (q.x * q.z - q.w * q.y));

                var pitch_L = (180.0 / Math.PI) * pitch; // from radians
                var yaw_L = (180.0 / Math.PI) * yaw;

                q = packet.eyeGazePose1.orientation;

                yaw = Math.Atan2(2.0 * (q.y * q.z + q.w * q.x), q.w * q.w - q.x * q.x - q.y * q.y + q.z * q.z);
                pitch = Math.Asin(-2.0 * (q.x * q.z - q.w * q.y));

                var pitch_R = (180.0 / Math.PI) * pitch; // from radians
                var yaw_R = (180.0 / Math.PI) * yaw;

                float eyeLookUpLimit = 43;
                float eyeLookDownLimit = 55;
                float eyeLookOutLimit = 47;
                float eyeLookInLimit = 47;

                if (pitch_L > 0)
                {
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Left_L] = Math.Min(1, (float)(pitch_L / eyeLookOutLimit));
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Right_L] = 0;
                }
                else
                {
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Left_L] = 0;
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Right_L] = Math.Min(1, (float)((-pitch_L) / eyeLookInLimit));
                }
                if (yaw_L > 0)
                {
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Up_L] = Math.Min(1, (float)(yaw_L / eyeLookUpLimit));
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Down_L] = 0;
                }
                else
                {
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Up_L] = 0;
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Down_L] = Math.Min(1, (float)((-yaw_L) / eyeLookDownLimit));
                }

                if (pitch_R > 0)
                {
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Left_R] = Math.Min(1, (float)(pitch_R / eyeLookInLimit));
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Right_R] = 0;
                }
                else
                {
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Left_R] = 0;
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Right_R] = Math.Min(1, (float)((-pitch_R) / eyeLookOutLimit));
                }
                if (yaw_R > 0)
                {
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Up_R] = Math.Min(1, (float)(yaw_R / eyeLookUpLimit));
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Down_R] = 0;
                }
                else
                {
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Up_R] = 0;
                    packet.expressionWeights[(int)FBExpression.Eyes_Look_Down_R] = Math.Min(1, (float)((-yaw_R) / eyeLookDownLimit));
                }

                #endregion

                #region Eye Data to UnifiedEye

                //Porting of eye tracking parameters
                eye.Left.Gaze = MakeEye
                (
                    LookLeft: packet.expressionWeights[(int)FBExpression.Eyes_Look_Left_L],
                    LookRight: packet.expressionWeights[(int)FBExpression.Eyes_Look_Right_L],
                    LookUp: packet.expressionWeights[(int)FBExpression.Eyes_Look_Up_L],
                    LookDown: packet.expressionWeights[(int)FBExpression.Eyes_Look_Down_L]
                );

                eye.Right.Gaze = MakeEye
                (
                    LookLeft: packet.expressionWeights[(int)FBExpression.Eyes_Look_Left_R],
                    LookRight: packet.expressionWeights[(int)FBExpression.Eyes_Look_Right_R],
                    LookUp: packet.expressionWeights[(int)FBExpression.Eyes_Look_Up_R],
                    LookDown: packet.expressionWeights[(int)FBExpression.Eyes_Look_Down_R]
                );

                // Eye dilation code, automated process maybe?
                eye.Left.PupilDiameter_MM = 5f;
                eye.Right.PupilDiameter_MM = 5f;

                // Force the normalization values of Dilation to fit avg. pupil values.
                eye._minDilation = 0;
                eye._maxDilation = 10;

                #endregion

            }
        }

        private void UpdateEyeExpressionsFB(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressions)
        {
            #region Eye Expressions Set

            unifiedExpressions[(int)UnifiedExpressions.EyeWideLeft].Weight = expressions[(int)FBExpression.Upper_Lid_Raiser_L] ;
            unifiedExpressions[(int)UnifiedExpressions.EyeWideRight].Weight = expressions[(int)FBExpression.Upper_Lid_Raiser_R] ;

            unifiedExpressions[(int)UnifiedExpressions.EyeSquintLeft].Weight = expressions[(int)FBExpression.Lid_Tightener_L];
            unifiedExpressions[(int)UnifiedExpressions.EyeSquintRight].Weight = expressions[(int)FBExpression.Lid_Tightener_R];

            #endregion

            #region Brow Expressions Set

            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpLeft].Weight = expressions[(int)FBExpression.Inner_Brow_Raiser_L];
            unifiedExpressions[(int)UnifiedExpressions.BrowInnerUpRight].Weight = expressions[(int)FBExpression.Inner_Brow_Raiser_R];
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = expressions[(int)FBExpression.Outer_Brow_Raiser_L];
            unifiedExpressions[(int)UnifiedExpressions.BrowOuterUpRight].Weight = expressions[(int)FBExpression.Outer_Brow_Raiser_R];

            unifiedExpressions[(int)UnifiedExpressions.BrowLowererLeft].Weight = expressions[(int)FBExpression.Brow_Lowerer_L];
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchLeft].Weight = expressions[(int)FBExpression.Brow_Lowerer_L];
            unifiedExpressions[(int)UnifiedExpressions.BrowLowererRight].Weight = expressions[(int)FBExpression.Brow_Lowerer_R];
            unifiedExpressions[(int)UnifiedExpressions.BrowPinchRight].Weight = expressions[(int)FBExpression.Brow_Lowerer_R];

            #endregion
        }

        // Thank you @adjerry on the VRCFT discord for these conversions! https://docs.google.com/spreadsheets/d/118jo960co3Mgw8eREFVBsaJ7z0GtKNr52IB4Bz99VTA/edit#gid=0
        private void UpdateMouthExpressionsFB(ref UnifiedExpressionShape[] unifiedExpressions, ReadOnlySpan<float> expressions)
        {
            #region Jaw Expression Set                        
            unifiedExpressions[(int)UnifiedExpressions.JawOpen].Weight = expressions[(int)FBExpression.Jaw_Drop];           
            unifiedExpressions[(int)UnifiedExpressions.JawLeft].Weight = expressions[(int)FBExpression.Jaw_Sideways_Left];
            unifiedExpressions[(int)UnifiedExpressions.JawRight].Weight = expressions[(int)FBExpression.Jaw_Sideways_Right];
            unifiedExpressions[(int)UnifiedExpressions.JawForward].Weight = expressions[(int)FBExpression.Jaw_Thrust];
            #endregion

            #region Mouth Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.MouthClosed].Weight = expressions[(int)FBExpression.Lips_Toward];

            unifiedExpressions[(int)UnifiedExpressions.MouthUpperLeft].Weight = expressions[(int)FBExpression.Mouth_Left];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerLeft].Weight = expressions[(int)FBExpression.Mouth_Left];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperRight].Weight = expressions[(int)FBExpression.Mouth_Right];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerRight].Weight = expressions[(int)FBExpression.Mouth_Right];

            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullLeft].Weight = expressions[(int)FBExpression.Lip_Corner_Puller_L];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantLeft].Weight = expressions[(int)FBExpression.Lip_Corner_Puller_L];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerPullRight].Weight = expressions[(int)FBExpression.Lip_Corner_Puller_R];
            unifiedExpressions[(int)UnifiedExpressions.MouthCornerSlantRight].Weight = expressions[(int)FBExpression.Lip_Corner_Puller_R];
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownLeft].Weight = expressions[(int)FBExpression.Lip_Corner_Depressor_L];
            unifiedExpressions[(int)UnifiedExpressions.MouthFrownRight].Weight = expressions[(int)FBExpression.Lip_Corner_Depressor_R];

            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownLeft].Weight = expressions[(int)FBExpression.Lower_Lip_Depressor_L];
            unifiedExpressions[(int)UnifiedExpressions.MouthLowerDownRight].Weight = expressions[(int)FBExpression.Lower_Lip_Depressor_R];
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpLeft].Weight = Math.Max(0.0f, expressions[(int)FBExpression.Upper_Lip_Raiser_L] - expressions[(int)FBExpression.Nose_Wrinkler_L]); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenLeft].Weight = Math.Max(0.0f, expressions[(int)FBExpression.Upper_Lip_Raiser_L] - expressions[(int)FBExpression.Nose_Wrinkler_L]); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperUpRight].Weight = Math.Max(0.0f, expressions[(int)FBExpression.Upper_Lip_Raiser_R] - expressions[(int)FBExpression.Nose_Wrinkler_L]); // Workaround for wierd tracking quirk.
            unifiedExpressions[(int)UnifiedExpressions.MouthUpperDeepenRight].Weight = Math.Max(0.0f, expressions[(int)FBExpression.Upper_Lip_Raiser_R] - expressions[(int)FBExpression.Nose_Wrinkler_L]); // Workaround for wierd tracking quirk.

            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserUpper].Weight = expressions[(int)FBExpression.Chin_Raiser_T];
            unifiedExpressions[(int)UnifiedExpressions.MouthRaiserLower].Weight = expressions[(int)FBExpression.Chin_Raiser_B];

            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleLeft].Weight = expressions[(int)FBExpression.Dimpler_L];
            unifiedExpressions[(int)UnifiedExpressions.MouthDimpleRight].Weight = expressions[(int)FBExpression.Dimpler_R];

            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerLeft].Weight = expressions[(int)FBExpression.Lip_Tightener_L];
            unifiedExpressions[(int)UnifiedExpressions.MouthTightenerRight].Weight = expressions[(int)FBExpression.Lip_Tightener_R];

            unifiedExpressions[(int)UnifiedExpressions.MouthPressLeft].Weight = expressions[(int)FBExpression.Lip_Pressor_L];
            unifiedExpressions[(int)UnifiedExpressions.MouthPressRight].Weight = expressions[(int)FBExpression.Lip_Pressor_R];

            unifiedExpressions[(int)UnifiedExpressions.MouthStretchLeft].Weight = expressions[(int)FBExpression.Lip_Stretcher_L];
            unifiedExpressions[(int)UnifiedExpressions.MouthStretchRight].Weight = expressions[(int)FBExpression.Lip_Stretcher_R];
            #endregion

            #region Lip Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperRight].Weight = expressions[(int)FBExpression.Lip_Pucker_R];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerRight].Weight = expressions[(int)FBExpression.Lip_Pucker_R];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerUpperLeft].Weight = expressions[(int)FBExpression.Lip_Pucker_L];
            unifiedExpressions[(int)UnifiedExpressions.LipPuckerLowerLeft].Weight = expressions[(int)FBExpression.Lip_Pucker_L];

            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperLeft].Weight = expressions[(int)FBExpression.Lip_Funneler_LT];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelUpperRight].Weight = expressions[(int)FBExpression.Lip_Funneler_RT];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerLeft].Weight = expressions[(int)FBExpression.Lip_Funneler_LB];
            unifiedExpressions[(int)UnifiedExpressions.LipFunnelLowerRight].Weight = expressions[(int)FBExpression.Lip_Funneler_RB];

            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperLeft].Weight = expressions[(int)FBExpression.Lip_Suck_LT];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckUpperRight].Weight = expressions[(int)FBExpression.Lip_Suck_RT];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerLeft].Weight = expressions[(int)FBExpression.Lip_Suck_LB];
            unifiedExpressions[(int)UnifiedExpressions.LipSuckLowerRight].Weight = expressions[(int)FBExpression.Lip_Suck_RB];
            #endregion

            #region Cheek Expression Set   
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffLeft].Weight = expressions[(int)FBExpression.Cheek_Puff_L];
            unifiedExpressions[(int)UnifiedExpressions.CheekPuffRight].Weight = expressions[(int)FBExpression.Cheek_Puff_R];
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckLeft].Weight = expressions[(int)FBExpression.Cheek_Suck_L];
            unifiedExpressions[(int)UnifiedExpressions.CheekSuckRight].Weight = expressions[(int)FBExpression.Cheek_Suck_R];
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintLeft].Weight = expressions[(int)FBExpression.Cheek_Raiser_L];
            unifiedExpressions[(int)UnifiedExpressions.CheekSquintRight].Weight = expressions[(int)FBExpression.Cheek_Raiser_R];
            #endregion

            #region Nose Expression Set             
            unifiedExpressions[(int)UnifiedExpressions.NoseSneerLeft].Weight = expressions[(int)FBExpression.Nose_Wrinkler_L];
            unifiedExpressions[(int)UnifiedExpressions.NoseSneerRight].Weight = expressions[(int)FBExpression.Nose_Wrinkler_R];
            #endregion

            #region Tongue Expression Set   
            //Future placeholder
            #endregion
        }

        private Vector2 MakeEye(float LookLeft, float LookRight, float LookUp, float LookDown) =>
            new Vector2(LookRight - LookLeft, LookUp - LookDown);
        #endregion
        
        #endregion

        public override void Teardown()
        {
            Logger.Msg("Tearing down ALVR client server...");
            try
            {
                CancelRunAction();
                runQueueTokenSource.Cancel();
                runQueue.CompleteAdding();

            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
                Thread.Sleep(1000);
            }
            finally
            {
                if (configControl != null)
                    configControl.SaveConfig();
            }
            Logger.Msg("ALVR module successfully disposed resources!");
        }
    }
}