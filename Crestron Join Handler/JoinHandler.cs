using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using BasicTriList = Crestron.SimplSharpPro.DeviceSupport.BasicTriList;

namespace ExampleSourceCode.JoinHandler
{
    /// <summary><para>
    /// The JoinHandler class allows for a much easier interaction with Crestron Touch panels.
    /// This class uses <see cref="JoinValue"/> objects as both references to join values, as well as storage containers for current data.
    /// This class allows delegates to be mapped to each specific join value, which streamlines event driven logic originating from the UI.
    /// </para></summary>
    public static class JoinHandler
    {
        //Dictionary containing separate lists for each type of join value.
        private static readonly Dictionary<eSigType, List<JoinValue>> JoinValueList;
        
        //Lookup dictionary for the action to invoke when a sig event is raised.
        private static readonly Dictionary<eSigType, Dictionary<uint, Action<Sig>>> Handlers;
        
        //Keeps track of digital sigs, and if action should be invoked on rising/falling edge, or both
        private static readonly Dictionary<uint, bool []> BooleanTriggers;
        
        //List containing the interface objects.
        private static readonly List<BasicTriListWithSmartObject> TheInterfaces = new();
        
        //Default button behaviors - default will only trigger on rising edge, unless otherwise specified when adding actions.
        private const bool RisingEdgeTrigger = true;
        private const bool FallingEdgeTrigger = false;

        static JoinHandler()
        {
            Handlers = new Dictionary<eSigType, Dictionary<uint, Action<Sig>>>
            {
                { eSigType.Bool, new Dictionary<uint, Action<Sig>>() },
                { eSigType.UShort, new Dictionary<uint, Action<Sig>>() },
                { eSigType.String, new Dictionary<uint, Action<Sig>>() }
            };
            
            JoinValueList = new Dictionary<eSigType, List<JoinValue>>
            {
                { eSigType.Bool, new List<JoinValue> { } },
                { eSigType.String, new List<JoinValue> { } },
                { eSigType.UShort, new List<JoinValue> { } }
            };
            
            BooleanTriggers = new Dictionary<uint, bool[]>();
        }

        private static void InvokeWithParams(BasicTriList device, SigEventArgs args)
        {
            try
            {
                if (args.Sig.Type == eSigType.Bool)
                {
                    //Sig was digital, check to see if we need to invoke the action based on rising/falling edge
                    if ((BooleanTriggers[args.Sig.Number][0] && args.Sig.BoolValue) ||
                        (BooleanTriggers[args.Sig.Number][1] && !args.Sig.BoolValue))
                        Handlers[eSigType.Bool][args.Sig.Number].Invoke(args.Sig);
                }
                //Wasn't a digital sig, so invoke the action
                else Handlers[args.Sig.Type][args.Sig.Number].Invoke(args.Sig);
            }
            catch (Exception)
            {
                //Nothing to invoke at the specified join - just catch the lookup exception instead of implementing additional logic.
            }
        }

        //Applies all previously created join values to a user interface.
        private static void ApplyJoinValuesToInterface(BasicTriListWithSmartObject theInterface)
        {
            foreach (var digitalJoinValue in JoinValueList[eSigType.Bool])
                digitalJoinValue.AddInputSig(theInterface.BooleanInput[digitalJoinValue.JoinValueNumericValue]);
            foreach (var analogJoinValue in JoinValueList[eSigType.UShort])
                analogJoinValue.AddInputSig(theInterface.UShortInput[analogJoinValue.JoinValueNumericValue]);
            foreach (var stringJoinValue in JoinValueList[eSigType.String])
                stringJoinValue.AddInputSig(theInterface.StringInput[stringJoinValue.JoinValueNumericValue]);
        }
        
        //Removes all previously created join values from a user interface.
        private static void RemoveJoinValuesFromInterface(BasicTriListWithSmartObject theInterface)
        {
            foreach (var digitalJoinValue in JoinValueList[eSigType.Bool])
                digitalJoinValue.AddInputSig(theInterface.BooleanInput[digitalJoinValue.JoinValueNumericValue]);
            foreach (var analogJoinValue in JoinValueList[eSigType.UShort])
                analogJoinValue.AddInputSig(theInterface.UShortInput[analogJoinValue.JoinValueNumericValue]);
            foreach (var stringJoinValue in JoinValueList[eSigType.String])
                stringJoinValue.AddInputSig(theInterface.StringInput[stringJoinValue.JoinValueNumericValue]);
        }
    
        /// Adds a new JoinValue to the handler.
        public static void AddJoinValue(JoinValue joinValue)
        {
            //Check to see if this value exists in the relevant list.
            if (JoinValueList[joinValue.JoinValueType].Contains(joinValue))
                throw new ArgumentException("The handler already contains the join value that was passed in as an argument.");
            
            //Add the join value to the appropriate list
            JoinValueList[joinValue.JoinValueType].Add(joinValue);
        }

        /// <summary><para>
        /// Resets each join value present in the handler back to the default value.
        /// </para></summary>
        public static void ResetAllValuesToDefault()
        {
            foreach (var joinvalue in JoinValueList.Values.SelectMany(valueList => valueList))
                joinvalue.ResetValueToDefault();
        }
        
        /// <summary><para>
        /// Add a new event handler to be run whenever the joinvalue changes.
        /// </para></summary>
        /// <param name="joinValue">The specific join value.</param>
        /// <param name="onJoinChange">The delegate event to run whenever a change is detected in the joinvalue.</param>
        /// <param name="risingEdge">If this is a bool join, should the handler run on a rising edge?</param>
        /// <param name="fallingEdge">If this is a bool join, should the handler run on a falling edge?</param>
        public static void SetJoinAction(JoinValue joinValue,Action<Sig> onJoinChange, bool risingEdge = RisingEdgeTrigger, bool fallingEdge = FallingEdgeTrigger)
        {
            //Add a new dictionary entry 
            Handlers[joinValue.JoinValueType].Add(joinValue.JoinValueNumericValue, onJoinChange);
            
            if (joinValue.JoinValueType == eSigType.Bool)
                //Join value was digital, so add new rising/falling edge parameters
                BooleanTriggers.Add(joinValue.JoinValueNumericValue, new [] {risingEdge, fallingEdge});
        }
    
        /// <summary><para>
        /// Add a new user interface to the handler.
        /// </para></summary>
        /// <param name="userInterface">The user interface to add.</param>
        public static void AddUserInterface(BasicTriListWithSmartObject userInterface)
        {
            if (TheInterfaces.Contains(userInterface))
                throw new ArgumentException("The user interface provided has already been added to the interface list.");
            userInterface.SigChange += InvokeWithParams;
            TheInterfaces.Add(userInterface);
            ApplyJoinValuesToInterface(userInterface);
        }
        /// <summary><para>
        /// Remove an existing user interface from the handler.
        /// </para></summary>
        /// <param name="userInterface">The user interface to remove.</param>
        public static void RemoveUserInterface(BasicTriListWithSmartObject userInterface)
        {
            if (!TheInterfaces.Contains(userInterface))
                throw new ArgumentException("The user interface provided is not in the interface list.");
            userInterface.SigChange -= InvokeWithParams;
            RemoveJoinValuesFromInterface(userInterface);
            TheInterfaces.Remove(userInterface);
        }
    }
}