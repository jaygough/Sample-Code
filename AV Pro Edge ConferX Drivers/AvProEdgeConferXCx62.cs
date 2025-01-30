using System.Net;
using Crestron.SimplSharpPro;

namespace ExampleSourceCode.AvProEdgeConferXDriver
{
    //Manual for this driver implementation is found at: https://avproglobal.egnyte.com/dl/MLwjjODK9q
    
    /// <summary><para>
    /// Class for controlling / monitoring the AVPro Edge ConferX AC-CX62-AUHD matrix switcher.
    /// </para></summary>
    public class AvProEdgeConferXCx62 : AvProEdgeConferXBase
    {
        /// <summary><para>
        /// Property to get the current HDBT output video mode.
        /// </para></summary>
        public HdbtVideoMode CurrentOutputVideoMode { get; private set; }
        
        /// <summary><para>Construct a new AV Pro Edge ConferX AC-CX62-AUHD matrix switcher that communicates via TCP.</para></summary>
        /// <param name="ipAddress"><see cref="IPAddress"/> of the switcher.</param>
        /// <param name="port">Optional port parameter. Defaults to 23 (telnet port).</param>
        public AvProEdgeConferXCx62(IPAddress ipAddress, ushort port = 23) : base(ipAddress, port, 6, 2) { }

        /// <summary><para>Construct a new AV Pro Edge ConferX AC-CX62-AUHD matrix switcher that communicates via serial.</para></summary>
        /// <param name="comPort">Crestron <see cref="ComPort"/> that is connected to the switcher.</param>
        public AvProEdgeConferXCx62(ComPort comPort) : base(comPort, 6, 2) { }

        /// <summary><para>Method to set the video mode of the output.</para></summary>
        /// <param name="mode">The new video mode.</param>
        public void SetOutputVideoMode(HdbtVideoMode mode) => SendToDevice("SET OUT1 VIDEO" + mode);

        //Enable device specific functionality.
        protected override void SetupSwitcher()
        {
            SupportsFanAutoFunction = true;
            SupportsFanSpeedFunction = true;
            SupportsTemperatureFunction = true;
        }
    }
}