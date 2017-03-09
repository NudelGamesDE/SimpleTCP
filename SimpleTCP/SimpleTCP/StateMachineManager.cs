using System;
using System.Collections.Generic;

namespace SimpleTCP
{
    public interface IStateMachine<in T>
    {
        bool ApplyObject(T aInput);
        bool IsFinished();
    }

    public class StateMachineManager<TStateMachine, TInput> where TStateMachine : IStateMachine<TInput>
    {

        private readonly Func<TStateMachine> stateMachineGenerator;
        public StateMachineManager(Func<TStateMachine> aStateMachineGenerator)
        {
            stateMachineGenerator = aStateMachineGenerator;
        }

        private readonly List<TStateMachine> inCalculation = new List<TStateMachine>();

        private readonly object ApplyObjectLocker = new object();
        public List<TStateMachine> ApplyObjects(IEnumerable<TInput> aInputs)
        {
            lock (ApplyObjectLocker)
            {
                var ret = new List<TStateMachine>();
                foreach (var _input in aInputs)
                {
                    var _newStateMachine = stateMachineGenerator();
                    if (_newStateMachine != null)
                        inCalculation.Add(_newStateMachine);
                    var _ElemetsToRemove = new List<TStateMachine>();
                    foreach (var v in inCalculation)
                    {
                        if (!v.ApplyObject(_input))
                            _ElemetsToRemove.Add(v);
                        if (!v.IsFinished()) continue;
                        ret.Add(v);
                        _ElemetsToRemove.Add(v);
                    }
                    foreach (var v in _ElemetsToRemove)
                        inCalculation.Remove(v);
                }
                return ret;
            }
        }

    }
}
