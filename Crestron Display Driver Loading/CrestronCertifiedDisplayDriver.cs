using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using ControlConceptsExampleCode;
using Crestron.RAD.Common.Interfaces;
using Crestron.RAD.Devices.CrestronConnectedDisplay;
using Crestron.RAD.Drivers.Displays;
using Crestron.RAD.ProTransports;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DM;
using Directory = Crestron.SimplSharp.CrestronIO.Directory;

namespace ExampleSourceCode.CrestronDriverLoading
{
    /// <summary><para>
    /// This class allows for the control of any display with Creston certified driver support.
    /// All types of available transport are supported. These include IP, Serial, CEC, IR, and Crestron Connected.
    /// </para></summary>
    public class CrestronCertifiedDisplayDriver
    {
        /// <summary><para>
        /// The default driver path in the user folder.
        /// VC-4 has different filesystem, so path will be slightly different.
        /// Best practice is to always use the "user" folder.
        /// </para></summary>
        private string DriverFileBasePath { get; } = CrestronEnvironment.DevicePlatform == eDevicePlatform.Appliance ? "/user/" : Path.Combine(Directory.GetApplicationRootDirectory(), @"User\");
        
        /// <summary><para>
        /// The serial <see cref="ComPort"/> used to communicate with a display over serial.
        /// </para></summary>
        private ComPort DriverComPort { get; }
        
        /// <summary><para>
        /// The <see cref="Cec"/> port used to communicate with a display over display CEC.
        /// </para></summary>
        private Cec DriverCecPort { get; }
        
        /// <summary><para>
        /// The <see cref="IROutputPort"/> port used to communicate with a display over IR.
        /// </para></summary>
        private IROutputPort DriverIrOutputPort { get; }
        
        /// <summary><para>
        /// The <see cref="IPAddress"/> of a network controlled display.
        /// </para></summary>
        private IPAddress DriverIpControlAddress { get; }
        
        /// <summary><para>
        /// The port of a network controlled display. This is usually set in the driver file, but can be overridden with this variable if needed.
        /// </para></summary>
        private ushort DriverIpControlPort { get; }
        
        /// <summary><para>
        /// True = Crestron Connected Display, False = Driver Display
        /// </para></summary>
        private bool IsCrestronConnectedDisplay { get; }
        
        /// <summary><para>
        /// Full file name of the driver as found in the file system.
        /// </para></summary>
        public string DriverFileName { get; set; }
        
        /// <summary><para>
        /// Enable/Disable subfolder creation for extracted .pkg driver files. NOTE: Must be set before loading the driver.
        /// </para></summary>
        public bool UseSubfolderForDriverFiles { get; set; } = true;
        
        /// <summary><para>
        /// If enabled, sets the name for the driver subfolder. NOTE: Must be set before loading the driver.
        /// </para></summary>
        public string DriverFileSubfolderName { get; set; } = "CrestronCertifiedDrivers";
        
        /// <summary><para>
        /// Interface for the display. Will be a null object until the driver is successfully loaded.
        /// Use this object to connect event handlers and other relevant display data, as well as utilize all control functionality.
        /// </para></summary>
        public IBasicVideoDisplay Display { get; private set; }
        
        /// <summary><para>
        /// Enabled while the driver is currently loaded.
        /// </para></summary>
        public bool IsDriverLoaded => Display != null;

        /// <summary><para>
        /// When enabled, the .pkg file will automatically be detected in the provided file structure. NOTE: Do not enable when multiple unique drivers are utilized.
        /// </para></summary>
        public bool UseAutomaticDriverFileDetection { get; set; } = false;
        
        //Message sent when a null reference exception is thrown. 
        private const string WrongTransportType = "Driver is using the wrong transport type. Verify that the correct constructor was called.";

        /// <summary><para>
        /// Instantiate a new instance of a <see cref="CrestronCertifiedDisplayDriver"/> object using an existing <see cref="ComPort"/>.
        /// This class allows for the control of any display with Creston certified driver support.
        /// </para></summary>
        /// <param name="comPort">Serial port that this driver will use as a communications transport. </param>
        /// <exception cref="ArgumentNullException">Throws an exception if the provided transport is null.</exception>
        public CrestronCertifiedDisplayDriver(ComPort comPort)
        {
            DriverComPort = comPort ?? throw new ArgumentNullException(nameof(comPort));
        }
        
        /// <summary><para>
        /// Instantiate a new instance of a <see cref="CrestronCertifiedDisplayDriver"/> object using an existing <see cref="Cec"/>.
        /// </para></summary>
        /// <param name="cecPort">CEC port that this driver will use as a communications transport. </param>
        /// <exception cref="ArgumentNullException">Throws an exception if the provided transport is null.</exception>
        public CrestronCertifiedDisplayDriver(Cec cecPort)
        {
            DriverCecPort = cecPort ?? throw new ArgumentNullException(nameof(cecPort));
        }
        
        /// <summary><para>
        /// Instantiate a new instance of a <see cref="CrestronCertifiedDisplayDriver"/> object using an existing <see cref="IROutputPort"/>.
        /// </para></summary>
        /// <param name="irOutputPort">IR port that this driver will use as a communications transport. </param>
        /// <exception cref="ArgumentNullException">Throws an exception if the provided transport is null.</exception>
        public CrestronCertifiedDisplayDriver(IROutputPort irOutputPort)
        {
            DriverIrOutputPort = irOutputPort ?? throw new ArgumentNullException(nameof(irOutputPort));
        }
        
        /// <summary><para>
        /// Instantiate a new instance of a <see cref="CrestronCertifiedDisplayDriver"/> object using a valid <see cref="IPAddress"/>.
        /// </para></summary>
        /// <param name="ipControlIpAddress">IP address that this driver will use to connect over a network. </param>
        /// <param name="ipControlPort">Optional port number that this driver will use to connect to at the provided IP address. </param>
        /// <exception cref="ArgumentNullException">Throws an exception if the provided transport is null.</exception>
        public CrestronCertifiedDisplayDriver(IPAddress ipControlIpAddress, ushort ipControlPort = 0)
        {
            DriverIpControlAddress = ipControlIpAddress ?? throw new ArgumentNullException(nameof(ipControlIpAddress));
            DriverIpControlPort = ipControlPort;
        }
        
        /// <summary><para>
        /// Instantiate a new instance of a <see cref="CrestronCertifiedDisplayDriver"/> object using a valid Crestron IpId and <see cref="ControlSystem"/>.
        /// </para></summary>
        /// <param name="crestronConnectedIpId">Crestron IpId that this driver will use to connect to a Crestron Connected Display.</param>
        /// <param name="controlSystem">Control System that the initializer will use to create the object.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the provided IpId is out of bounds.</exception>
        /// <exception cref="NullReferenceException">Thrown when the provided ControlSystem object is null.</exception>
        public CrestronCertifiedDisplayDriver(ushort crestronConnectedIpId, ControlSystem controlSystem)
        {
            //IpId cannot be less than 3.
            if (crestronConnectedIpId < 3)
                throw new ArgumentOutOfRangeException(nameof(crestronConnectedIpId));
            
            //Need a valid control system object to create this type of display driver.
            if (controlSystem == null)
                throw new ArgumentNullException(nameof(controlSystem));
            
            Display = new CrestronConnectedDisplay();
            ((CrestronConnectedDisplay)Display).Initialize(crestronConnectedIpId, controlSystem);
            IsCrestronConnectedDisplay = true;
        }


        /// <summary><para>
        /// Attempt to load the driver file from system storage.
        /// On success, the <see cref="IBasicVideoDisplay"/> object of this class will be available for use.
        /// </para></summary>
        /// <exception cref="ArgumentException">Thrown when the driver file name is invalid or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the specified file was not found in the file system of the processor.</exception>
        /// <exception cref="FileLoadException">Thrown when a driver file does not contain valid data.</exception>
        /// <exception cref="NullReferenceException">Thrown when a driver was loaded, but not matched to the correct constructor call.</exception>
        /// <exception cref="InvalidOperationException">Thrown when trying to load a driver after calling the crestron connected display constructor.</exception>
        public void LoadDriver()
        {
            //Check if this is a crestron connected display. No need to load any files if so.
            if (IsCrestronConnectedDisplay)
                throw new InvalidOperationException("Cannot load driver when using a crestron connected display.");

            //Check if the driver file name has been set, throw an exception if not.
            if (string.IsNullOrEmpty(DriverFileName) && !UseAutomaticDriverFileDetection)
                throw new ArgumentException("Error loading driver. Driver filename cannot be null or empty.");

            //Check if directory is created, and create it if not.
            if (UseSubfolderForDriverFiles && !Directory.Exists(Path.Combine(DriverFileBasePath, DriverFileSubfolderName)))
                Directory.CreateDirectory(Path.Combine(DriverFileBasePath, DriverFileSubfolderName));

            string fullDriverPath;
            
            //If we're using automatic discovery, scan the current directory for any .pkg files.
            if (UseAutomaticDriverFileDetection)
            {
                var driverDirectory = new DirectoryInfo(DriverFileBasePath);
                if (driverDirectory.GetFiles().ToList().Exists(file => file.Extension.Equals(".pkg")))
                    fullDriverPath = driverDirectory.GetFiles().ToList().First(file => file.Extension.Equals(".pkg")).FullName;
                else
                    throw new FileNotFoundException("Driver discovery was unable to find any .pkg driver files in the default directory.");
            }
            
            //Not using automatic discovery, so check to see if the specified package file exists.
            else
            {
                fullDriverPath = Path.Combine(DriverFileBasePath, DriverFileName);
                if (!File.Exists(fullDriverPath))
                    throw new FileNotFoundException("The specified driver file could not be located in the default directory.");
            }
            
            //Set up the file extraction path.
            var extractionPath = UseSubfolderForDriverFiles ? Path.Combine(DriverFileBasePath, DriverFileSubfolderName) : DriverFileBasePath;
            extractionPath = Path.Combine(extractionPath, new FileInfo(fullDriverPath).Name);
            
            //If the extraction directory already exists, overwrite and start fresh.
            if (Directory.Exists(extractionPath))
                Directory.Delete(extractionPath, true);
                
            //Create the new directory for storing the driver files.
            Directory.CreateDirectory(extractionPath);
            
            //Unzip the driver .pkg file to acquire the library file .dll (or .ir)
            ZipFile.ExtractToDirectory(fullDriverPath, extractionPath);
            
            //Check to see if the extracted driver file contained a .dll or .ir
            var extractedDirectory = new DirectoryInfo(extractionPath);
            if (!extractedDirectory.GetFiles().ToList().Exists(file => file.Extension.Equals(".dll") || file.Extension.Equals(".ir")))
                throw new FileLoadException("The .pkg driver file does not contain a .dll or .ir file.");
            
            //Load the driver file
            var theDriverFile = extractedDirectory.GetFiles().FirstOrDefault(file => file.Extension.Equals(".dll") || file.Extension.Equals(".ir"));
            
            //Check to see if this is a non-ir library driver (dll file)
            if (theDriverFile is { Extension: ".dll" })
            {
                //Load the assembly from the driver file
                var loadedDriver = Assembly.LoadFile(theDriverFile.FullName);
                
                //If this is a display driver, the assembly should contain a class implementing IBasicVideoDisplay
                if (loadedDriver.GetTypes().ToList().Exists(type => typeof(IBasicVideoDisplay).IsAssignableFrom(type)))
                {
                    //Load the type from the assembly, and create an instance of it.
                    var displayType = loadedDriver.GetTypes().ToList().Single(type => typeof(IBasicVideoDisplay).IsAssignableFrom(type));
                    Display = (IBasicVideoDisplay)Activator.CreateInstance(displayType);
                    if (Display == null)
                        throw new NullReferenceException("Failed to instantiate the provided driver file.");

                    //Check if driver uses serial communications. If so, initialize using provided serial comport (if not null).
                    if (typeof(ISerialComport).IsAssignableFrom(displayType))
                    {
                        if (DriverComPort == null)
                            throw new NullReferenceException(WrongTransportType);
                        ((ISerialComport)Display).Initialize(new SerialTransport(DriverComPort));
                    }

                    //Check if driver uses IP communications. If so, initialize using provided IP address (if not null).
                    else if (typeof(ITcp).IsAssignableFrom(displayType))
                    {
                        if (DriverIpControlAddress == null)
                            throw new NullReferenceException(WrongTransportType);
                        ((ITcp)Display).Initialize(DriverIpControlAddress, DriverIpControlPort > 0 ? DriverIpControlPort : ((ITcp)Display).Port);
                    }
                    
                    //Check if driver uses CEC communications. If so, initialize using provided CEC port (if not null).
                    else if (typeof(ICecDevice).IsAssignableFrom(displayType))
                    {
                        if (DriverCecPort == null)
                            throw new NullReferenceException(WrongTransportType);
                        var cecDevice = new CecTransport();
                        cecDevice.Initialize(DriverCecPort);
                        ((ICecDevice)Display).Initialize(cecDevice);
                    }
                }
                else 
                    throw new FileLoadException("The driver file does not implement the correct interface. Was a display driver loaded?");
            }
            
            //The driver is not a .dll, which means this driver contains an .ir file command structure.
            else
            {
                //Driver uses IR communications. Initialize using provided IR port (if not null)
                if (DriverIrOutputPort == null)
                    throw new NullReferenceException(WrongTransportType);
                Display = new IrDisplay();
                ((IrDisplay)Display).Initialize(new IrPortTransport(DriverIrOutputPort), theDriverFile?.FullName);
            }
        }
    }
}