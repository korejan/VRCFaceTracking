using System;
using System.Runtime.InteropServices;
using VRCFaceTracking;

namespace ALXRTrackingInterface
{
    #region ALVR C-Types, Ignore These
    [StructLayout(LayoutKind.Sequential)]
    public struct TrackingQuat
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TrackingVector3
    {
        public float x;
        public float y;
        public float z;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TrackingVector2
    {
        public float x;
        public float y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TrackingInfo
    {
        public ulong targetTimestampNs;
        public TrackingQuat HeadPose_Pose_Orientation;
        public TrackingVector3 HeadPose_Pose_Position;
        public byte mounted;
        public const uint MAX_CONTROLLERS = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Controller
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool enabled;
            [MarshalAs(UnmanagedType.U1)]
            public bool isHand;
            public ulong buttons;
            public TrackingVector2 trackpadPosition;
            public float triggerValue;
            public float gripValue;
            public TrackingQuat orientation;
            public TrackingVector3 position;
            public TrackingVector3 angularVelocity;
            public TrackingVector3 linearVelocity;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 19)]
            public TrackingQuat[] boneRotations;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 19)]
            public TrackingVector3[] bonePositionsBase;
            public TrackingQuat boneRootOrientation;
            public TrackingVector3 boneRootPosition;
            public uint handFingerConfidences;
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public Controller[] controller;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TimeSync
    {
        public uint type;
        public uint mode;
        public ulong sequence;
        public ulong serverTime;
        public ulong clientTime;
        public ulong packetsLostTotal;
        public ulong packetsLostInSecond;
        public uint averageTotalLatency;
        public uint averageSendLatency;
        public uint averageTransportLatency;
        public ulong averageDecodeLatency;
        public uint idleTime;
        public uint fecFailure;
        public ulong fecFailureInSecond;
        public ulong fecFailureTotal;
        public float fps;
        public uint serverTotalLatency;
        public ulong trackingRecvFrameIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EyeFov
    {
        public float left;
        public float right;
        public float top;
        public float bottom;
    }
    #endregion

    #region LibALXR C-types
    public enum ALXRGraphicsApi
    {
        Auto,
        Vulkan2,
        Vulkan,
        D3D12,
        D3D11,
        OpenGLES,
        OpenGL,
        ApiCount = OpenGL
    }

    public enum ALXRDecoderType
    {
        D311VA,
        NVDEC,
        CUVID,
        VAAPI,
        CPU
    }

    public enum ALXRTrackingSpace
    {
        LocalRefSpace,
        StageRefSpace,
        ViewRefSpace
    }

    public enum ALXRCodecType
    {
        H264_CODEC,
        HEVC_CODEC
    }

    // replicates https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_FB_color_space
    public enum ALXRColorSpace
    {
        Unmanaged = 0,
        Rec2020 = 1,
        Rec709 = 2,
        RiftCV1 = 3,
        RiftS = 4,
        Quest = 5,
        P3 = 6,
        AdobeRgb = 7,
        Default = Quest,
        MaxEnum = 0x7fffffff
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct ALXRSystemProperties
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string systemName;
        public float currentRefreshRate;
        public IntPtr refreshRates;
        public uint refreshRatesCount;
        public uint recommendedEyeWidth;
        public uint recommendedEyeHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ALXREyeInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public EyeFov[] eyeFov;
        public float ipd;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ALXRVersion
    {
        public uint major;
        public uint minor;
        public uint patch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ALXRRustCtx
    {
        public delegate void InputSendDelegate(ref TrackingInfo data);
        public delegate void ViewsConfigSendDelegate(ref ALXREyeInfo eyeInfo);
        public delegate ulong PathStringToHashDelegate(string path);
        public delegate void TimeSyncSendDelegate(ref TimeSync data);
        public delegate void VideoErrorReportSendDelegate();
        public delegate void BatterySendDelegate(ulong device_path, float gauge_value, bool is_plugged);
        public delegate void SetWaitingNextIDRDelegate(bool waiting);
        public delegate void RequestIDRDelegate();

        public InputSendDelegate inputSend;
        public ViewsConfigSendDelegate viewsConfigSend;
        public PathStringToHashDelegate pathStringToHash;
        public TimeSyncSendDelegate timeSyncSend;
        public VideoErrorReportSendDelegate videoErrorReportSend;
        public BatterySendDelegate batterySend;
        public SetWaitingNextIDRDelegate setWaitingNextIDR;
        public RequestIDRDelegate requestIDR;

        public ALXRVersion firmwareVersion;
        public ALXRGraphicsApi graphicsApi;
        public ALXRDecoderType decoderType;
        public ALXRColorSpace displayColorSpace;

        public ALXRFacialExpressionType facialTracking;
        public ALXREyeTrackingType eyeTracking;

        [MarshalAs(UnmanagedType.U1)]
        public bool verbose;
        [MarshalAs(UnmanagedType.U1)]
        public bool disableLinearizeSrgb;
        [MarshalAs(UnmanagedType.U1)]
        public bool noSuggestedBindings;
        [MarshalAs(UnmanagedType.U1)]
        public bool noServerFramerateLock;
        [MarshalAs(UnmanagedType.U1)]
        public bool noFrameSkip;
        [MarshalAs(UnmanagedType.U1)]
        public bool disableLocalDimming;
        [MarshalAs(UnmanagedType.U1)]
        public bool headlessSession;
        [MarshalAs(UnmanagedType.U1)]
        public bool noFTServer;
        [MarshalAs(UnmanagedType.U1)]
        public bool noPassthrough;
        [MarshalAs(UnmanagedType.U1)]
        public bool noHandTracking;

#if XR_USE_PLATFORM_ANDROID
        public IntPtr applicationVM;
        public IntPtr applicationActivity;
#endif
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ALXRGuardianData
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool shouldSync;
        public float areaWidth;
        public float areaHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ALXRRenderConfig
    {
        public uint eyeWidth;
        public uint eyeHeight;
        public float refreshRate;
        public float foveationCenterSizeX;
        public float foveationCenterSizeY;
        public float foveationCenterShiftX;
        public float foveationCenterShiftY;
        public float foveationEdgeRatioX;
        public float foveationEdgeRatioY;
        [MarshalAs(UnmanagedType.U1)]
        public bool enableFoveation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ALXRDecoderConfig
    {
        public ALXRCodecType codecType;
        [MarshalAs(UnmanagedType.U1)]
        public bool enableFEC;
        [MarshalAs(UnmanagedType.U1)]
        public bool realtimePriority;
        public uint cpuThreadCount; // only used for software decoding.
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ALXRStreamConfig
    {
        public ALXRTrackingSpace trackingSpaceType;
        public ALXRRenderConfig renderConfig;
        public ALXRDecoderConfig decoderConfig;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ALXRProcessFrameResult
    {
        public ALXRFacialEyePacket* newPacket;
        public bool exitRenderLoop;
        public bool requestRestart;
    }

    public enum ALXRLogOptions : uint
    {
        None = 0,
        Timestamp = (1u << 0),
        LevelTag = (1u << 1)
    }

    public enum ALXRLogLevel : uint
    {
        Verbose,
        Info,
        Warning,
        Error
    }

    public delegate void ALXRLogOutputFn(ALXRLogLevel level, string output, uint len);
    #endregion

    #region LibALXR Functions
    public static class LibALXR
    {
        public const string DllName = "alxr_engine.dll";

        public const CallingConvention ALXRCallingConvention = CallingConvention.Cdecl;

        public static bool AddDllSearchPath()
        {
            return SetDllDirectory(Utils.CustomLibsDirectory + "\\ModuleLibs\\libalxr\\");
        }

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern bool alxr_init(ref ALXRRustCtx ctx, out ALXRSystemProperties systemProperties);

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_destroy();

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_request_exit_session();

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_process_frame([In, Out] ref bool exitRenderLoop, [In, Out] ref bool requestRestart);

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_process_frame2([In, Out] ref ALXRProcessFrameResult result);

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern bool alxr_is_session_running();

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_set_stream_config(ALXRStreamConfig config);

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern ALXRGuardianData alxr_get_guardian_data();

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_on_receive(IntPtr packet, uint packetSize);

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_on_tracking_update(bool clientsidePrediction);

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_on_haptics_feedback(ulong path, float duration_s, float frequency, float amplitude);

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_on_server_disconnect();

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_on_pause();

        [DllImport(DllName, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_on_resume();
                
        [DllImport(DllName, CharSet = CharSet.Auto, SetLastError = true, CallingConvention = ALXRCallingConvention)]
        public static extern void alxr_set_log_custom_output(ALXRLogOptions options, ALXRLogOutputFn outputFn);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

    }
    #endregion
}