using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Text.RegularExpressions;

namespace VRCFaceTracking.CommandLine
{
    public class Arguments
    {
        public string IP { get; set; }
        public int SendPort { get; set; }
        public int ReceivePort { get; set; }
        public bool EnableEye { get; set; }
        public bool EnableExpression { get; set; }
    }

    public class ArgumentsBinder : BinderBase<Arguments>
    {
        private readonly Option<string> _ipAndPorts;
        private readonly Option<bool> _enableEye;
        private readonly Option<bool> _enableExpression;

        public ArgumentsBinder(Option<string> ipAndPorts, Option<bool> enableEye, Option<bool> enableExpression)
        {
            _ipAndPorts = ipAndPorts;
            _enableEye = enableEye;
            _enableExpression = enableExpression;
        }

        protected override Arguments GetBoundValue(BindingContext bindingContext)
        {
            var split = bindingContext.ParseResult.GetValueForOption(_ipAndPorts).Split(':');
            var ip = split[1];

            if (!new Regex("^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$").IsMatch(ip) 
                || !int.TryParse(split[0], out var sendPort)
                || !int.TryParse(split[2], out var receivePort))
                throw new ArgumentException("Invalid IP Address or Port");

            return new()
            {

                IP = ip,
                SendPort = sendPort,
                ReceivePort = receivePort,
                EnableEye = !bindingContext.ParseResult.GetValueForOption(_enableEye),
                EnableExpression = !bindingContext.ParseResult.GetValueForOption(_enableExpression)
            };
        }
    }
}