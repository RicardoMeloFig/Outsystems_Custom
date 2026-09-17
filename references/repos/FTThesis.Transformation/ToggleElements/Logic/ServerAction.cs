using OutSystems.Model;
using OutSystems.Model.Logic;
using OutSystems.Model.Logic.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModelAPITest.ToggleElements
{
    class ServerAction : LogicGeneric<IServerAction>
    {
        protected override void SetActionsPrivacy(IServerAction action)
        {
            action.Public = false;
        }

        protected override void WriteHeader()
        {
            Console.WriteLine("Transformed Server Actions:");
        }

    }

}
