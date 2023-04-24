using System.IO;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace ALXRTrackingInterface
{
    public enum RunMethod
    {
        None,
        LibALXR, // Local
        ALXRClient
    };

    public struct ALXRClientConfig
    {
        [JsonConverter(typeof(UI.IPAddressJsonConverter))]
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
}