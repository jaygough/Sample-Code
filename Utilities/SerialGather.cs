using System;
using System.Text.RegularExpressions;
using Crestron.SimplSharp;

namespace ExampleSourceCode.Utilities
{
    /// <summary><para>
    /// Simple string gather that triggers an event when a delimiter is encountered from a text stream.
    /// </para></summary>
    public class SerialGather
    {
        /// <summary><para>
        /// The currently set delimiter.
        /// </para></summary>
        public string CurrentDelimiter { get; set; }

        /// <summary><para>
        /// Internal variable for the current string buffer.
        /// </para></summary>
        private string CurrentBuffer { get; set; } = string.Empty;
        
        /// <summary><para>
        /// Event that is invoked whenever the gather detects that the specified delimiter has been encountered.
        /// </para></summary>
        public event Action<string> OnDelimiterEncountered;
        
        /// <summary><para>
        /// Construct a new string gather using a given delimiter.
        /// The gather will trigger an event whenever a delimiter is encountered in a buffer update.
        /// </para></summary>
        /// <param name="delimiter">The delimiter to use with this gather.</param>
        /// <exception cref="ArgumentException">Thrown when an invalid (empty) delimiter is used in the constructor.</exception>
        public SerialGather(string delimiter)
        {
            //Throw an exception if the delimiter is empty. Delimiter needs to be at least a space or single character, at minimum.
            if (string.IsNullOrEmpty(delimiter))
                throw new ArgumentException("An empty delimiter is not valid for a communication gather.", nameof(delimiter));

            CurrentDelimiter = delimiter;
        }

        //Processes data from the transports.
        private void ProcessNewData(string newData)
        {
            //Add the new data to the buffer.
            CurrentBuffer += newData;

            //Check to see if the current buffer contains the delimiter
            if (!CurrentBuffer.Contains(CurrentDelimiter)) return;

            //It does, so now split the buffer by the delimiter (one or more times).
            //Use regex in the instance multiple characters are included in the delimiter string.
            var segments = Regex.Split(CurrentBuffer, CurrentDelimiter);

            //Iterate through each segment of the split buffer, except the last segment, which will become the start of the rebuilt buffer.
            //On each iteration, invoke the event for processing delimited text.
            for (var segNum = 0; segNum < segments.Length - 1; segNum++)
                OnDelimiterEncountered?.Invoke(segments[segNum]);

            //Set the current buffer to the last segment of the split.
            CurrentBuffer = segments[segments.Length - 1];
            CrestronConsole.PrintLine("[{0}] ", CurrentBuffer);
        }

        /// <summary><para>
        /// Method for receiving buffer data..
        /// </para></summary>
        public void GatherOnDataReceived(string newData)
        {
            if (!string.IsNullOrEmpty(newData))
                ProcessNewData(newData);
        }
    }
}