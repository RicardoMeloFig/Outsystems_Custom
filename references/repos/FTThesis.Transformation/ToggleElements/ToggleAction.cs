using OutSystems.Model;
using OutSystems.Model.Logic;
using System;
using System.Collections.Generic;
using System.Text;

namespace ModelAPITest.ToggleElements
{
    interface ToggleAction
    {
        IAction GetToggleAction(IESpace espace);

    }
}
