using System;
using System.Collections;
using System.Collections.Generic;
using Crestron.SimplSharpPro;

namespace ExampleSourceCode.JoinHandler
{
    /// <summary><para>
    /// Represents an indexed list of grouped join values.
    /// Best used for similar groups of data that work well with sequential join numbers.
    /// </para></summary>
    public class JoinValueList : IEnumerable<JoinValue>
    {
        //The list of join values added to this group.
        private readonly List<JoinValue> _joinValues;
        
        /// <summary><para>
        /// Get the join value at the specified index.
        /// </para></summary>
        /// <param name="joinValueIndex"></param>
        /// <exception cref="IndexOutOfRangeException">Thrown when an attempt is made to access a join value that does not exist in the internal list.</exception>
        public JoinValue this[int joinValueIndex] => _joinValues[joinValueIndex];
        
        
        /// <summary><para>
        /// Creates a new join value list object.
        /// </para></summary>
        /// <param name="joinType">The type of join this list will contain.</param>
        /// <param name="joinIndexStart">The starting join number. List will be incremented starting from this number.</param>
        /// <param name="joinCount">The number of unique join values to add to the list.</param>
        /// <param name="defaultSerialValue">Optional default serial value applied to all join values in the list.</param>
        /// <param name="defaultDigitalValue">Optional default digital value applied to all join values in the list.</param>
        /// <param name="defaultAnalogValue">Optional default analog value applied to all join values in the list.</param>
        public JoinValueList(eSigType joinType, ushort joinIndexStart, ushort joinCount, string defaultSerialValue = "", bool defaultDigitalValue = false, ushort defaultAnalogValue = 0)
        {
            //Instantiates new join values based on the origin type
            _joinValues = new List<JoinValue>();
            for (var index = joinIndexStart; index < joinIndexStart+joinCount; index++)
                switch(joinType)
                {
                    case eSigType.Bool:
                        _joinValues.Add(new JoinValue(eSigType.Bool, index, defaultDigitalValue: defaultDigitalValue));
                        break;
                    case eSigType.String:
                        _joinValues.Add(new JoinValue(eSigType.String, index, defaultSerialValue: defaultSerialValue));
                        break;
                    case eSigType.UShort:
                        _joinValues.Add(new JoinValue(eSigType.UShort, index, defaultAnalogValue: defaultAnalogValue));
                        break;
                    case eSigType.NA:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(joinType), joinType, null);
                }
        }

        /// <summary><para>
        /// Set all the digital values in this list to a new value.
        /// </para></summary>
        /// <param name="newValue">New value to apply to all digital joins.</param>
        public void SetAllDigital(bool newValue)
        {
            foreach (var value in _joinValues)
                value.DigitalValue = newValue;
        }
        
        /// <summary><para>
        /// Set all the analog values in this list to a new value.
        /// </para></summary>
        /// <param name="newValue">New value to apply to all analog joins.</param>
        public void SetAllAnalog(ushort newValue)
        {
            foreach (var value in _joinValues)
                value.AnalogValue = newValue;
        }
        
        
        /// <summary><para>
        /// Set all the serial values in this list to a new value.
        /// </para></summary>
        /// <param name="newValue">New value to apply to all serial joins.</param>
        public void SetAllSerial(string newValue)
        {
            foreach (var value in _joinValues)
                value.SerialValue = newValue;
        }
        
        public IEnumerator<JoinValue> GetEnumerator()
        {
            return _joinValues.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}