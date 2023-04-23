using System.Runtime.InteropServices;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ALVRTrackingInterface
{
    public enum ALXRFacialExpressionType : byte
    {
        None = 0, // Not Support or Disabled
        FB,
        HTC,
        Pico,
        Auto,
        TypeCount
    };
    public enum ALXREyeTrackingType : byte
    {
        None = 0, // Not Support or Disabled
        FBEyeTrackingSocial,
        ExtEyeGazeInteraction,
        Auto,
        TypeCount
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct XrVector3
    {
        public float x, y, z;
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct XrQuaternion
    {
        public float x, y, z, w;
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct XrPosef
    {
        public XrQuaternion orientation;
        public XrVector3 position;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ALXRFacialEyePacket
    {
        public const int MaxEyeCount = 2;
        public const int MaxExpressionCount = 63;

        public ALXRFacialExpressionType expressionType;
        public ALXREyeTrackingType eyeTrackerType;
        public byte isEyeFollowingBlendshapesValid;
        public unsafe fixed byte isEyeGazePoseValid[MaxEyeCount];
        public unsafe fixed float expressionWeights[MaxExpressionCount];

        //public unsafe fixed XrPosef eyeGazePoses[2];
        public XrPosef eyeGazePose0;
        public XrPosef eyeGazePose1;

        public ReadOnlySpan<float> ExpressionWeightSpan
        {
            get
            {
                unsafe
                {
                    fixed (float* p = expressionWeights)
                    {
                        return new ReadOnlySpan<float>(p, MaxExpressionCount);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ALXRFacialEyePacket ReadPacket(byte[] array)
        {
            var newPacket = new ALXRFacialEyePacket();
            ReadPacket(array, ref newPacket);
            return newPacket;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadPacket(byte[] array, ref ALXRFacialEyePacket newPacket)
        {
            Debug.Assert(array.Length >= Marshal.SizeOf<ALXRFacialEyePacket>());
            ReadMemoryMarshal(array, 0, array.Length, out newPacket);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadMemoryMarshal<T>(byte[] array, int offset, int size, out T result) where T : struct
        {
            result = MemoryMarshal.Cast<byte, T>(array.AsSpan(offset, size))[0];
        }
    };
}