using OutSystems.Model;
using OutSystems.Model.Logic;
using OutSystems.Model.Logic.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModelAPITest.ToggleElements
{
    class ClientAction : LogicGeneric<IClientAction>
    {
        protected override void SetActionsPrivacy(IClientAction action)
        {
            action.Public = false;
        }

        protected override void WriteHeader()
        {
            Console.WriteLine("Transformed Client Actions:");
        }
    }
}
