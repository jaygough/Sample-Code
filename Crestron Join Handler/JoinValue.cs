using System.Collections.Generic;
using Crestron.SimplSharpPro;

namespace ExampleSourceCode.JoinHandler
{
    /// <summary><para>
    /// Represents a join value, which links part of the program to an external user interface.
    /// </para></summary>
    public class JoinValue
    {
        //Internal variables
        private string _internalSerialVar;
        private ushort _internalAnalogVar;
        private bool _internalDigitalVar;
        
        //Interface input sigs
        private readonly List<StringInputSig> _stringInputSigs = new();
        private readonly List<UShortInputSig> _analogInputSigs = new();
        private readonly List<BoolInputSig> _digitalInputSigs = new();
    
        //Default values
        private readonly string _defaultSerialValue;
        private readonly bool _defaultDigitalValue;
        private readonly ushort _defaultAnalogValue;

        /// <summary><para>
        /// Represents a join value, which links part of the program to an external user interface.
        /// </para></summary>
        /// <param name="type">eSigType of this join value.</param>
        /// <param name="number">Number indicating what join the value will use.</param>
        /// <param name="defaultSerialValue">Optional variable to indicate what the default serial value of this join should be.</param>
        /// <param name="defaultDigitalValue">Optional variable to indicate what the default boolean value of this join should be.</param>
        /// <param name="defaultAnalogValue">Optional variable to indicate what the default analog value of this join should be.</param>
        public JoinValue(eSigType type, uint number, string defaultSerialValue = "", bool defaultDigitalValue = false, ushort defaultAnalogValue = 0)
        {
            _defaultSerialValue = defaultSerialValue;
            _defaultDigitalValue = defaultDigitalValue;
            _defaultAnalogValue = defaultAnalogValue;
            JoinValueType = type;
            JoinValueNumericValue = number;
        }

        /// <summary><para>
        /// The serial value of this join.
        /// </para></summary>
        public string SerialValue
        {
            get => _internalSerialVar;
            set
            {
                _internalSerialVar = value;
                if (_stringInputSigs.Count == 0) return;
                foreach (var sig in _stringInputSigs)
                    sig.StringValue = value;
            }
        }

        /// <summary><para>
        /// The analog (ushort) value of this join.
        /// </para></summary>
        public ushort AnalogValue
        {
            get => _internalAnalogVar;
            set
            {
                _internalAnalogVar = value;
                if (_analogInputSigs.Count == 0) return;
                foreach (var sig in _analogInputSigs)
                    sig.UShortValue = value;
            }
        }
        
        /// <summary><para>
        /// The boolean value of this join.
        /// </para></summary>
        public bool DigitalValue
        {
            get => _internalDigitalVar;
            set
            {
                _internalDigitalVar = value;
                if (_digitalInputSigs.Count == 0) return;
                foreach (var sig in _digitalInputSigs)
                    sig.BoolValue = value;
            }
        }
        /// <summary><para>
        /// Reset all values back to default.
        /// </para></summary>
        public void ResetValueToDefault()
        {
            SerialValue = _defaultSerialValue;
            AnalogValue = _defaultAnalogValue;
            DigitalValue = _defaultDigitalValue;
        }

        /// <summary><para>
        /// Adds an input sig to the join. This is updated whenever the value of the join is changed.
        /// </para></summary>
        /// <param name="inputSig">The input sig to add.</param>
        public void AddInputSig(Sig inputSig)
        {
            if (inputSig.GetType() == typeof(BoolInputSig))
            {
                _digitalInputSigs.Add(inputSig as BoolInputSig);
                DigitalValue = DigitalValue; //Trigger digital sig update
            }
            else if (inputSig.GetType() == typeof(StringInputSig))
            {
                _stringInputSigs.Add(inputSig as StringInputSig);
                SerialValue = SerialValue; //Trigger serial sig update
            }
            else if (inputSig.GetType() == typeof(UShortInputSig))
            {
                _analogInputSigs.Add(inputSig as UShortInputSig);
                AnalogValue = AnalogValue; //Trigger analog sig update
            }
        }

        /// <summary><para>
        /// Removes an existing input sig to the join.
        /// </para></summary>
        /// <param name="inputSig">The input sig to remove</param>
        public void RemoveInputSig(Sig inputSig)
        {
            if (inputSig.GetType() == typeof(BoolInputSig))
                _digitalInputSigs.Remove(inputSig as BoolInputSig);
            else if (inputSig.GetType() == typeof(StringInputSig))
                _stringInputSigs.Remove(inputSig as StringInputSig);
            else if (inputSig.GetType() == typeof(UShortInputSig))
                _analogInputSigs.Remove(inputSig as UShortInputSig);
        }
        
        //Value type
        public eSigType JoinValueType { get; private set; }
        
        //Value uint number
        public uint JoinValueNumericValue { get; private set; }
    }
}