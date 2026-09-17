using OutSystems.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace ModelAPITest
{
    interface ToggleableElement
    {
        void GetDiffElements(IESpace old, IESpace newe, String newOrAltered);

        void GetAllElements(IESpace newe);

        void ToggleElement(IESpace espace, List<IKey> keys, String feature);

        void GetAllElementsFromList(IESpace newe, List<string> elements, String feature);
    }
}
