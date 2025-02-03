using System.Text.RegularExpressions;
using ConferX.Utilities;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using IPAddress = System.Net.IPAddress;

namespace ConferX.AvProEdgeConferXDriver;

/// <summary><para>Base class for controlling / monitoring the ConferX line of AVPro Edge Matrix Switchers.</para></summary>
/// Models Supported - AC-CX42-AUHD | AC-CX62-AUHD | AC-CX84-AUHD
public abstract partial class AvProEdgeConferXBase
{
    #region ConferX Constants
    //The required com port specs as detailed by the manufacturer.
    private const ComPort.eComBaudRates ComPortBaudRate = ComPort.eComBaudRates.ComspecBaudRate57600;
    private const ComPort.eComDataBits ComPortDataBits = ComPort.eComDataBits.ComspecDataBits8;
    private const ComPort.eComParityType ComPortParityType = ComPort.eComParityType.ComspecParityNone;
    private const ComPort.eComStopBits ComPortStopBits = ComPort.eComStopBits.ComspecStopBits1;
    private const ComPort.eComProtocolType ComPortProtocolType = ComPort.eComProtocolType.ComspecProtocolRS232;
    private const ComPort.eComHardwareHandshakeType ComPortHandshakeType = ComPort.eComHardwareHandshakeType.ComspecHardwareHandshakeNone;
    
    #endregion

    #region ConferX Enums
    public enum HdmiVideoMode
    {
        Bypass = 1,
        Upscale2K = 3
    }

    public enum HdbtVideoMode
    {
        Downscale4K = 2,
        IctMode = 4
    }

    public enum HdbtOutput
    {
        All,
        Hdbt1 = 1,
        Hdbt3 = 3
    }
    
    public enum HdmiOutput
    {
        All,
        Hdmi2 = 2,
        Hdmi4 = 4
    }
    

    #endregion

    #region Private Properties
    ///<summary><para>The gather, for processing responses from the unit.</para></summary>
    private readonly SerialGather _gather = new("\r\n");
    
    /// <summary><para>IP transport for the matrix.</para></summary>
    private TcpIpWrapper ConferxClient { get; }
    
    ///<summary><para>Crestron serial port connected to the matrix.</para></summary>
    private ComPort ConferxComPort { get; }
    
    ///<summary><para>Number of usable outputs on the matrix.</para></summary>
    private int NumberOfOutputs { get; set; }
    
    /// <summary><para>Number of usable inputs on the matrix.</para></summary>
    private int NumberOfInputs { get; set; }
    
    #endregion

    #region Public Properties
    /// <summary><para>
    /// Property to get the current TCP connection status.
    /// </para></summary>
    public bool IsTcpClientConnected => ConferxClient is { IsClientCurrentlyConnected: true };
    
    ///<summary><para>Property that stores the current signal status of each input.</para></summary>
    public bool [] CurrentInputSourceDetectionStatus { get; private set; }
    
    ///<summary><para>Property that stores the current routing of each video output.</para></summary>
    public int [] CurrentOutputVideoSourceSelection { get; private set; }
        
    ///<summary><para>Property that stores the current routing of each ex-audio output.</para></summary>
    public int [] CurrentOutputAudioSourceSelection { get; private set; }
    
    ///<summary><para>Property to get the current system address (00-99).</para></summary>
    public ushort SystemAddress { get; private set; }

    /// <summary><para>
    /// Property to get the current DHCP status (enabled/disabled)
    /// </para></summary>
    public bool NetworkDhcpEnabled { get; private set; }
    
    /// <summary><para>
    /// Property to get the current MAC address.
    /// </para></summary>
    public string NetworkMacAddress { get; private set; }
    
    /// <summary><para>
    /// Property to get the current IP (host) address.
    /// </para></summary>
    public IPAddress NetworkIpAddress { get; private set; }
    
    /// <summary><para>
    /// Property to get the current TCP Port.
    /// </para></summary>
    public ushort NetworkTcpPort { get; private set; }
    
    /// <summary><para>
    /// Property to get the current gateway address.
    /// </para></summary>
    public IPAddress NetworkGatewayAddress { get; private set; }
    
    /// <summary><para>
    /// Property to get the current subnet mask.
    /// </para></summary>
    public IPAddress NetworkSubnetMask { get; private set; }

    /// <summary><para>
    /// Property to get the current system fan speed (0-3).
    /// </para></summary>
    public ushort SystemFanSpeed { get; private set; } = 1;
    
    /// <summary><para>
    /// Property to get the current system fan auto status (on/off).
    /// </para></summary>
    public bool SystemFanAutoStatus {get; private set;}
        
    /// <summary><para>
    /// Property that indicates if the unit is currently in single switch or double switch mode.
    /// </para></summary>
    public bool DoubleSwitchEnabled { get; private set; } = false;
        
    /// <summary><para>
    /// Property that indicates if the unit supports fan speed set function.
    /// </para></summary>
    public bool SupportsFanSpeedFunction {get; protected set;}
        
    /// <summary><para>
    /// Property that indicates if the unit supports fan speed auto function.
    /// </para></summary>
    public bool SupportsFanAutoFunction {get; protected set;}
        
    /// <summary><para>
    /// Property that indicates if the unit supports temperature function.
    /// </para></summary>
    public bool SupportsTemperatureFunction {get; protected set;}
    
    #endregion

    #region Events
    /// <summary><para>
    /// Event triggered when the switcher has successfully connected.
    /// </para></summary>
    public event Action<TcpIpWrapper.TcpWrapperConnectionStatus> OnSwitcherNetworkConnectionChange;

    public event Action <int, int> OnSwitcherVideoRoutingUpdate;
    
    public event Action <int, bool> OnSwitcherInputSourceChange;
    
    #endregion
    
    #region Constructors

    
    
    /// <summary><para>
    /// Construct a new AV Pro Edge ConferX matrix switcher using a provided valid <see cref="System.Net.IPAddress"/>.
    /// </para></summary>
    /// <param name="ipAddress">IP address of the matrix.</param>
    /// <param name="port">TCP port of the matrix (default is 23).</param>
    /// <param name="numberOfInputs">Total number of matrix inputs.</param>
    /// <param name="numberOfOutputs">Total number of matrix outputs.</param>
    protected AvProEdgeConferXBase(IPAddress ipAddress, ushort port, ushort numberOfInputs, ushort numberOfOutputs)
    {
        if (ipAddress is null)
            throw new ArgumentNullException(nameof(ipAddress));
        
        //Initialize the TCP client.
        ConferxClient = new TcpIpWrapper(ipAddress, port, 1024)
        {
            AutoReconnect = true,
            AutoReconnectInterval = 5
        };
            
        //Set up the connection listener.
        ConferxClient.OnClientConnectionChange += status =>
        {
            //Once the system connects, get all the current switch information.
            if (status == TcpIpWrapper.TcpWrapperConnectionStatus.Connected)
                GetSystemStatus();
            
            //Trigger the local connection change event.
            OnSwitcherNetworkConnectionChange?.Invoke(status);
        };
            

        //Set up the gather for device responses.
        ConferxClient.OnTextReceived += _gather.GatherOnDataReceived;
        _gather.OnDelimiterEncountered += ProcessResponse;
        
        //Initialize the input and output lists based on the number of video IO ports.
        InitializeIo(numberOfInputs, numberOfOutputs);
    }

    /// <summary><para>
    /// Construct a new AV Pro Edge ConferX matrix switcher using a provided Crestron <see cref="ComPort"/> as the transport.
    /// </para></summary>
    /// <param name="comPort">Serial port connected to the matrix unit.</param>
    /// <param name="numberOfInputs">Total number of matrix inputs.</param>
    /// <param name="numberOfOutputs">Total number of matrix outputs.</param>
    protected AvProEdgeConferXBase(ComPort comPort, ushort numberOfInputs, ushort numberOfOutputs)
    {
        ConferxComPort = comPort ?? throw new ArgumentNullException(nameof(comPort));
        
        //Set the ComPort spec as defined by the manufacturer.
        comPort.SetComPortSpec(ComPortBaudRate, ComPortDataBits, ComPortParityType, ComPortStopBits, ComPortProtocolType, 
            ComPortHandshakeType, ComPort.eComSoftwareHandshakeType.ComspecSoftwareHandshakeNone, false);
        
        //Set up the gather for device responses.
        comPort.SerialDataReceived += (_, args) => _gather.GatherOnDataReceived(args.SerialData);
        _gather.OnDelimiterEncountered += ProcessResponse;
            
        //Initialize the input and output lists based on the number of video IO ports.
        InitializeIo(numberOfInputs, numberOfOutputs);
        
        //Get all the current switcher status information.
        GetSystemStatus();
    }

    #endregion
    
    #region Misc & Abstract Methods
    private void InitializeIo(ushort numberOfInputs, ushort numberOfOutputs)
    {
        NumberOfInputs = numberOfInputs;
        CurrentInputSourceDetectionStatus = new bool[NumberOfInputs];
            
        NumberOfOutputs = numberOfOutputs;
        CurrentOutputVideoSourceSelection = new int[NumberOfOutputs];
        CurrentOutputAudioSourceSelection = new int[NumberOfOutputs];
    }
    
    /// <summary><para>
    /// Runs a custom command.
    /// </para></summary>
    /// <param name="command">Full command string to send to the device.</param>
    public void RunCustomCommand(string command)
    {
        SendToDevice(command);
    }

    protected abstract void SetupSwitcher();
    
    #endregion

    #region Get Commands
    /// <summary><para>
    /// Method to fetch all status items from the unit and update all properties.
    /// </para></summary>
    public void GetSystemStatus() => SendToDevice("GET STA");
    
    /// <summary><para>
    /// Method to fetch the current system address.
    /// </para></summary>
    public void GetSystemAddress() => SendToDevice("GET ADDR");
    
    /// <summary><para>
    /// Method to fetch the current system baud rate.
    /// </para></summary>
    public void GetBaudRate() => SendToDevice("GET BAUDR");
    
    /// <summary><para>
    /// Method to fetch the current network gateway address.
    /// </para></summary>
    public void GetNetworkGateway() => SendToDevice("GET RIP");
    
    /// <summary><para>
    /// Method to fetch the current network address.
    /// </para></summary>
    public void GetNetworkAddress() => SendToDevice("GET HIP");
    
    /// <summary><para>
    /// Method to fetch the current network subnet mask.
    /// </para></summary>
    public void GetNetworkSubnetMask() => SendToDevice("GET NMK");
    
    /// <summary><para>
    /// Method to fetch the current network TCP/IP port.
    /// </para></summary>
    public void GetNetworkPort() => SendToDevice("GET TIP");
    
    /// <summary><para>
    /// Method to fetch the current network DHCP status.
    /// </para></summary>
    public void GetNetworkDhcpStatus() => SendToDevice("GET DHCP");
    
    /// <summary><para>
    /// Method to fetch the current network MAC address.
    /// </para></summary>
    public void GetMacAddress() => SendToDevice("GET MAC");
    
    /// <summary><para>
    /// Method to fetch the current input source status.
    /// </para></summary>
    /// <param name="sourceNumber">The input source number of the switcher to query.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the input does not exist on the switcher.</exception>
    public void GetSourceInputStatus(ushort sourceNumber){
        if (sourceNumber > NumberOfInputs)
            throw new ArgumentOutOfRangeException(nameof(sourceNumber));
        SendToDevice("GET IN" + sourceNumber + " SIG STA");
    }
    
    /// <summary><para>
    /// Method to fetch the current output source selection.
    /// </para></summary>
    /// <param name="outputNumber">The output number of the switcher to query.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the output does not exist on the switcher.</exception>
    public void GetOutputRoutingStatus(ushort outputNumber){
        if (outputNumber > NumberOfOutputs)
            throw new ArgumentOutOfRangeException(nameof(outputNumber));
        SendToDevice("GET OUT" + outputNumber + " VS");
    }

    #endregion
    
    #region Set Commands

    /// <summary><para>
    /// Method to reboot the switcher.
    /// </para></summary>
    public void Reboot() => SendToDevice("SET RBT");
    
    /// <summary><para>
    /// Method to reset all settings back to factory.
    /// </para></summary>
    public void ResetSettings() => SendToDevice("SET RST");
        
    /// <summary><para>
    /// Method to enable/disable double switch behavior.
    /// <param name="doubleSwitchEnabled">True = Enabled, False = Disabled.</param>
    /// </para></summary>
    public void SetDoubleSwitch(bool doubleSwitchEnabled) => SendToDevice("SET SWITCH MODE" + (doubleSwitchEnabled ? 1 : 0));

    /// <summary><para>
    /// Method to set the new system address.
    /// </para></summary>
    /// <param name="systemAddress">New system address (00-99).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the requested system address is > 99.</exception>
    public void SetSystemAddress(ushort systemAddress)
    {
        if (systemAddress > 99)
            throw new ArgumentOutOfRangeException(nameof(systemAddress));
        SendToDevice("SET ADDR " + systemAddress);
    }
    
    /// <summary><para>
    /// Method to set a new system IP address.
    /// </para></summary>
    /// <param name="ipAddress">New IP address the unit should use.</param>
    public void SetSystemIpAddress(IPAddress ipAddress)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);
        SendToDevice("SET HIP " + ipAddress);
    }

    /// <summary><para>
    /// Method to set a new system gateway address.
    /// </para></summary>
    /// <param name="gatewayAddress">New gateway IP address the unit should use.</param>
    public void SetSystemGatewayAddress(IPAddress gatewayAddress)
    {
        ArgumentNullException.ThrowIfNull(gatewayAddress);
        SendToDevice("SET RIP " + gatewayAddress);
    }
    
    /// <summary><para>
    /// Method to set a new system gateway address.
    /// </para></summary>
    /// <param name="subnetMask">New subnet mask the unit should use.</param>
    public void SetSystemSubnetMask(IPAddress subnetMask)
    {
        ArgumentNullException.ThrowIfNull(subnetMask);
        SendToDevice("SET NMK " + subnetMask);
    }
        

    /// <summary><para>
    /// Method to route a given input to a given output.
    /// </para></summary>
    /// <param name="inputNumber">The input number of the source port.</param>
    /// <param name="outputNumber">The output number of the destination port.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an input or output does not exist on the matrix.</exception>
    public void RouteVideoOutput(ushort inputNumber, ushort outputNumber)
    {
        if (inputNumber > NumberOfInputs)
            throw new ArgumentOutOfRangeException("Input " + inputNumber + " does not exist on this matrix.");
        
        if (outputNumber > NumberOfOutputs)
            throw new ArgumentOutOfRangeException("Output " + outputNumber + " does not exist on this matrix.");
        
        SendToDevice("SET OUT" + outputNumber + " VS IN" + inputNumber);
    }
        
    /// <summary><para>
    /// Method to route audio from a given input to a given output.
    /// </para></summary>
    /// <param name="inputNumber">The input number of the source port.</param>
    /// <param name="outputNumber">The output number of the destination port.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an input or output does not exist on the matrix.</exception>
    public void RouteExAudioOutput(ushort inputNumber, ushort outputNumber)
    {
        if (inputNumber > NumberOfInputs)
            throw new ArgumentOutOfRangeException("Input " + inputNumber + " does not exist on this matrix.");
        
        if (outputNumber > NumberOfOutputs)
            throw new ArgumentOutOfRangeException("Output " + outputNumber + " does not exist on this matrix.");
        
        SendToDevice("SET OUT" + outputNumber + " AS IN" + inputNumber);
    }
        
    /// <summary><para>
    /// Method to enable or disable the fan auto run behaviour.
    /// </para></summary>
    /// <param name="autoRun">True = Enabled, False = Disabled</param>
    /// <exception cref="autoRun">Thrown when attempting to control fans on a device that does not have fan functionality.</exception>
    public void SetAutoFanRun(bool autoRun)
    {
        if (!SupportsFanAutoFunction)
            throw new NotSupportedException("Fan auto function is not supported on this model.");
        SendToDevice("SET FAN AUTO " + (autoRun ? "EN" : "DIS"));
    }

    /// <summary><para>
    /// Method to set the current fan speed.
    /// </para></summary>
    /// <param name="fanSpeed">New fan speed (0-3).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the fan speed is > 3</exception>
    /// <exception cref="NotSupportedException">Thrown when attempting to control fans on a device that does not have fan functionality.</exception>
    public void SetFanSpeed(ushort fanSpeed)
    {
        if (!SupportsFanSpeedFunction)
            throw new NotSupportedException("Fan speed function is not supported on this model.");
        if (fanSpeed > 3)
            throw new ArgumentOutOfRangeException(nameof(fanSpeed));
        SendToDevice("SET FAN SPEED " + fanSpeed);
    }
        
    #endregion

    #region Communication & Response Processing
    /// <summary><para>
    /// Connect to the switcher using TCP transport.
    /// </para></summary>
    /// <exception cref="NullReferenceException">Thrown when serial is the transport used in the constructor.</exception>
    public void Connect()
    {
        //Don't need to connect if the transport is serial.
        if (ConferxClient is null)
            throw new NullReferenceException(nameof(ConferxClient));
        ConferxClient.Connect();
    }

    //Sends a command to the currently instantiated transport.
    protected void SendToDevice(string dataToSend)
    {
        if (dataToSend is null)
            throw new ArgumentException("Cannot send an empty string to the device.");

        ConferxComPort?.Send(dataToSend + "\r\n");
        ConferxClient?.SendText("\r" + dataToSend + "\r\n");
    }

    //Processes all responses from the switcher, and parses out return values.
    private void ProcessResponse(string command)
    {
        try
        {
            CrestronConsole.PrintLine("Received command: " + command);
            switch (command)
            {
                //Process MAC address.
                case not null when command.Contains("MAC"):
                    NetworkMacAddress = command.Replace("MAC", "").Trim();
                    break;
                //Process DHCP.
                case not null when command.Contains("DHCP"):
                    NetworkDhcpEnabled = command.Split(' ')[1] == "1";
                    break;
                //Process gateway address.
                case not null when command.Contains("RIP"):
                    NetworkGatewayAddress = IPAddress.Parse(command.Replace("RIP", "").Trim());
                    break;
                //Process device IP address.
                case not null when command.Contains("HIP"):
                    NetworkIpAddress = IPAddress.Parse(command.Replace("HIP", "").Trim());
                    break;
                //Process subnet mask.
                case not null when command.Contains("NMK"):
                    NetworkSubnetMask = IPAddress.Parse(command.Replace("NMK", "").Trim());
                    break;
                //Process TCP port.
                case not null when command.Contains("TIP"):
                    NetworkTcpPort = ushort.Parse(command.Split(' ')[1]);
                    break;
                //Process system address.
                case not null when command.Contains("ADDR"):
                    SystemAddress = ushort.Parse(command.Split(' ')[1]);
                    break;
                //Process fan speed.
                case not null when command.Contains("FAN SPEED"):
                    SystemFanSpeed = ushort.Parse(command.Split(' ')[2]);
                    break;
                //Process fan auto status.
                case not null when command.Contains("FAN AUTO"):
                    SystemFanAutoStatus = command.Split(' ')[1] == "1";
                    break;
                //Process video routing change.
                case not null when VideoRouteChange().IsMatch(command):
                    var outputNumberV = int.Parse(command[3].ToString());
                    var inputNumberV = int.Parse(command[10].ToString());
                    CurrentOutputVideoSourceSelection[outputNumberV-1] = inputNumberV-1;
                    OnSwitcherVideoRoutingUpdate?.Invoke(outputNumberV-1, inputNumberV-1);
                    break;
                //Process ex-audio routing change.
                case not null when ExAudioRouteChange().IsMatch(command):
                    var outputNumberA = int.Parse(command[3].ToString());
                    var inputNumberA = int.Parse(command[10].ToString());
                    CurrentOutputAudioSourceSelection[outputNumberA-1] = inputNumberA-1;
                    break;
                //Process input change.
                case not null when SourceInputChange().IsMatch(command):
                    var sourceNumber = int.Parse(command[10].ToString());
                    var newStatus = int.Parse(command[12].ToString()) == 1;
                    CurrentInputSourceDetectionStatus[sourceNumber - 1] = newStatus;
                    OnSwitcherInputSourceChange?.Invoke(sourceNumber - 1, newStatus);
                    break;
            }
        }
        catch (Exception ex)
        {
            //Something happened during the processing of the response.
            //Print to text console as well as the error log.
            CrestronConsole.PrintLine(ex.Message);
            ErrorLog.Error(ex.Message);
        }
    }
    
    #endregion

    #region Regex Expression Matching
    //Regex for video source change.
    [GeneratedRegex(@"OUT\d VS IN\d")]
    private static partial Regex VideoRouteChange();
        
    //Regex for ex-audio source change.
    [GeneratedRegex(@"OUT\d AS IN\d")]
    private static partial Regex ExAudioRouteChange();
    
    //Regex for source input change.
    [GeneratedRegex(@"SIG STA IN\d \d")]
    private static partial Regex SourceInputChange();
    
    #endregion
}