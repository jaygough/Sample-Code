using System;
using System.Collections.Generic;
using System.Net;
using Crestron.SimplSharpPro;

namespace ExampleSourceCode.AvProEdgeConferXDriver
{
    //Manual for this driver implementation is found at: https://avproglobal.egnyte.com/dl/IGVKL3PVpd
    
    ///<summary><para>Class for controlling / monitoring the AVPro Edge ConferX AC-CX84-AUHD matrix switcher.</para></summary>
    public class AvProEdgeConferXCx84 : AvProEdgeConferXBase
    { 
        //Internal storage for the current HDMI output modes
        private readonly Dictionary<HdmiOutput, HdmiVideoMode> _hdmiVideoModes = new()
        {
            { HdmiOutput.Hdmi2, HdmiVideoMode.Bypass },
            { HdmiOutput.Hdmi4, HdmiVideoMode.Bypass }
        };

        //Internal storage for the current HDBT output modes
        private readonly Dictionary<HdbtOutput, HdbtVideoMode> _hdbtVideoModes = new()
        {
            { HdbtOutput.Hdbt1, HdbtVideoMode.IctMode },
            { HdbtOutput.Hdbt3, HdbtVideoMode.IctMode }
        };

        #region Get Commands
        
        public void GetLcdOnTimer() => SendToDevice("GET LCD ON T");
        
        /// <summary><para>Returns the current output mode of the selected HDMI output.</para></summary>
        /// <param name="output">The selected output to query.</param>
        /// <returns>The mode of the selected output.</returns>
        /// <exception cref="ArgumentException">Thrown when the 'all' output is passed as an argument.</exception>
        public HdmiVideoMode GetHdmiVideoMode(HdmiOutput output)
        {
            if (output == HdmiOutput.All)
                throw new ArgumentException("The 'All' output is not supported for this operation.");
            return _hdmiVideoModes[output];
        }
        

        /// <summary><para>Returns the current output mode of the selected HDBT output.</para></summary>
        /// <param name="output">The selected output to query.</param>
        /// <returns>The mode of the selected output.</returns>
        /// <exception cref="ArgumentException">Thrown when the 'all' output is passed as an argument.</exception>
        public HdbtVideoMode GetHdbtVideoMode(HdbtOutput output)
        {
            if (output == HdbtOutput.All)
                throw new ArgumentException("The 'All' output is not supported for this operation.");
            return _hdbtVideoModes[output];
        }
        
        #endregion

        /// <summary><para>Construct a new AV Pro Edge ConferX AC-CX-84 matrix switcher that communicates via TCP.</para></summary>
        /// <param name="ipAddress"><see cref="IPAddress"/> of the switcher.</param>
        /// <param name="port">Optional port parameter. Defaults to 23 (telnet port).</param>
        public AvProEdgeConferXCx84(IPAddress ipAddress, ushort port = 23) : base(ipAddress, port, 8, 4) { }

        /// <summary><para>Construct a new AV Pro Edge ConferX AC-CX-84 matrix switcher that communicates via serial.</para></summary>
        /// <param name="comPort">Crestron <see cref="ComPort"/> that is connected to the switcher.</param>
        public AvProEdgeConferXCx84(ComPort comPort) : base(comPort, 8, 4) { }
        
        /// <summary><para>
        /// Set the output mode of a given HDMI port.
        /// </para></summary>
        /// <param name="mode">The selected HDMI mode.</param>
        /// <param name="output">The selected HDMI output port.</param>
        public void SetHdmiOutputVideoMode(HdmiVideoMode mode, HdmiOutput output) => SendToDevice("SET OUT" + (int)output + " HP VIDEO" + (int)mode);

        /// <summary><para>
        /// Set the output mode of a given HDBT port.
        /// </para></summary>
        /// <param name="mode">The selected HDBT mode.</param>
        /// <param name="output">The selected HDBT output port.</param>
        public void SetHdbtOutputVideoMode(HdbtVideoMode mode, HdbtOutput output) => SendToDevice("SET OUT" + (int)output + " TP VIDEO" + (int)mode);

        //Enable device specific functionality.
        protected override void SetupSwitcher()
        {
            SupportsFanSpeedFunction = true;
        }
    }
}
