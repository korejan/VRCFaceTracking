using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ALXRTrackingInterface
{
    public enum XrEyeExpressionHTC
    {
        LeftBlink = 0,
        LeftWide = 1,
        RightBlink = 2,
        RightWide = 3,
        LeftSqueeze = 4,
        RightSqueeze = 5,
        LeftDown = 6,
        RightDown = 7,
        LeftOut = 8,
        RightIn = 9,
        LeftIn = 10,
        RightOut = 11,
        LeftUp = 12,
        RightUp = 13,
    }

    public enum XrLipExpressionHTC
    {
        //None = -1,
        JawRight = 0, // +JawX
        JawLeft = 1, // -JawX
        JawForward = 2,
        JawOpen = 3,
        MouthApeShape = 4,
        MouthUpperRight = 5, // +MouthUpper
        MouthUpperLeft = 6, // -MouthUpper
        MouthLowerRight = 7, // +MouthLower
        MouthLowerLeft = 8, // -MouthLower
        MouthUpperOverturn = 9,
        MouthLowerOverturn = 10,
        MouthPout = 11,
        MouthSmileRight = 12, // +SmileSadRight
        MouthSmileLeft = 13, // +SmileSadLeft
        MouthSadRight = 14, // -SmileSadRight
        MouthSadLeft = 15, // -SmileSadLeft
        CheekPuffRight = 16,
        CheekPuffLeft = 17,
        CheekSuck = 18,
        MouthUpperUpRight = 19,
        MouthUpperUpLeft = 20,
        MouthLowerDownRight = 21,
        MouthLowerDownLeft = 22,
        MouthUpperInside = 23,
        MouthLowerInside = 24,
        MouthLowerOverlay = 25,
        TongueLongStep1 = 26,
        TongueLongStep2 = 32,
        TongueDown = 30, // -TongueY
        TongueUp = 29, // +TongueY
        TongueRight = 28, // +TongueX
        TongueLeft = 27, // -TongueX
        TongueRoll = 31,
        TongueUpLeftMorph = 34,
        TongueUpRightMorph = 33,
        TongueDownLeftMorph = 36,
        TongueDownRightMorph = 35,
        //Max = 37,
    }
}
