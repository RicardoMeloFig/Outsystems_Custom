using OutSystems.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace ModelAPITest
{
    interface FeatureToggler
    {
        void Run(string[] args);

        bool IsTraditional(IESpace module);

    }
}
