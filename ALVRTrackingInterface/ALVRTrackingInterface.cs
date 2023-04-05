﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using VRCFaceTracking;
using VRCFaceTracking.Params;

namespace ALVRTrackingInterface
{
    public class ALVRTrackingInterface : ExtTrackingModule
    {
        public IPAddress localAddr;
        public int PORT = 13191;

        private TcpClient client;
        private NetworkStream stream;
        private bool connected = false;
        private bool eyeActive;
        private bool lipActive;

        private byte[] rawExpressions = new byte[Marshal.SizeOf<VRCFTPacket>()];

        public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

        public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
        {
            // Using these to determine if we should be sending eye updates and/or lip updates to VRCFT.
            // Will allow other modules such as SRanipal to overlap should we want that (in the case of using the Facial Tracker in place of the Quest's lip tracker).
            eyeActive = eyeAvailable;
            lipActive = expressionAvailable;

            string configPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "questProIP.txt");
            Logger.Error(configPath);
            if (!File.Exists(configPath))
            {
                Logger.Msg("Failed to find config JSON! A questProIP.txt file has been generated, please configure your Quest Pro's IP address into this text file.");
                File.WriteAllText(configPath, "192.168.254.254");
                return (false, false);
            }

            string text = File.ReadAllText(configPath).Trim();

            if (!IPAddress.TryParse(text, out localAddr))
            {
                Logger.Error("The IP provided in questProIP.txt is not valid. Please check the file and try again.");
                return (false, false);
            }

            // Initialize server first before continuing.
            if (!ConnectToTCP())
                return (false, false);

            return (eyeActive, lipActive);
        }

        private bool ConnectToTCP()
        {
            try
            {
                client = new TcpClient();
                Logger.Msg($"Trying to establish a Quest Pro connection at {localAddr}:{PORT}...");

                client.Connect(localAddr, PORT);
                Logger.Msg("Connected to Quest Pro!");

                stream = client.GetStream();
                connected = true;

                return true;
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
            }

            return false;
        }


        public override Action GetUpdateThreadFunc()
        {
            return () =>
            {
                while (true)
                {
                    Update();
                    //Thread.Sleep(10); // blocked by IO
                }
            };
        }

        private void Update()
        {
            try
            {
                // Attempt reconnection if needed
                if (!connected || stream == null)
                {
                    ConnectToTCP();
                }

                if (stream == null)
                {
                    Logger.Warning("Can't read from network stream just yet! Trying again soon...");
                    return;
                }

                if (!stream.CanRead)
                {
                    Logger.Warning("Can't read from network stream just yet! Trying again soon...");
                    return;
                }

                int offset = 0;
                int readBytes;
                do
                {
                    readBytes = stream.Read(rawExpressions, offset, rawExpressions.Length - offset);
                    offset += readBytes;
                }
                while (readBytes > 0 && offset < rawExpressions.Length);

                if (offset < rawExpressions.Length && connected)
                {
                    // TODO Reconnect to the server if we lose connection
                    Logger.Warning("End of stream! Reconnecting...");
                    Thread.Sleep(1000);
                    connected = false;
                    try
                    {
                        stream.Close();
                    }
                    catch (SocketException e)
                    {
                        Logger.Error(e.Message);
                        Thread.Sleep(1000);
                    }
                }

                var newPacket = VRCFTPacket.ReadPacket(rawExpressions);

                if (eyeActive)
                {
                    UpdateEyeData(ref UnifiedTracking.Data.Eye, ref newPacket);
                    UpdateEyeExpressions(ref UnifiedTracking.Data.Shapes, ref newPacket);
                }
                if (lipActive)
                    UpdateMouthExpressions(ref UnifiedTracking.Data.Shapes, ref newPacket);
            }
            catch (SocketException e)
            {
                Logger.Error(e.Message);
                Thread.Sleep(1000);
            }
        }

        private void UpdateEyeData(ref UnifiedEyeData eye, ref VRCFTPacket packet)
        {
            switch (packet.eyeTrackerType)
            {
                case VRFCFTEyeType.FBEyeTrackingSocial:
                    UpdateEyeDataFB(ref eye, ref packet);
                    break;
            }
        }

        private void UpdateEyeExpressions(ref UnifiedExpressionShape[] unifiedExpressions, ref VRCFTPacket packet)
        {
            switch (packet.expressionType)
            {
                case VRFCFTExpressionType.FB:
                    UpdateEyeExpressionsFB(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan);
                    break;
            }
        }

        private void UpdateMouthExpressions(ref UnifiedExpressionShape[] unifiedExpressions, ref VRCFTPacket packet)
        {
            switch (packet.expressionType)
            {
                case VRFCFTExpressionType.FB:
                    UpdateEyeExpressionsFB(ref UnifiedTracking.Data.Shapes, packet.ExpressionWeightSpan);
                    break;
            }
        }

        // Preprocess our expressions per the Meta Documentation
        private void UpdateEyeDataFB(ref UnifiedEyeData eye, ref VRCFTPacket packet)
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

        public override void Teardown()
        {
            Logger.Msg("Tearing down ALVR client server...");
            try
            {
                stream.Close();
            }
            catch (SocketException e)
            {
                Logger.Error(e.Message);
                Thread.Sleep(1000);
            }

            Logger.Msg("ALVR module successfully disposed resources!");
        }
    }
}