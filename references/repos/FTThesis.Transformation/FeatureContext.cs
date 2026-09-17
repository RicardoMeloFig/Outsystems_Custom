using System;
using System.Collections.Generic;
using System.Text;

namespace ModelAPITest
{
    
    class FeatureSet
    {
        public List<Feature> Features { get; set; }
    }

    class Feature
    {
        public string Name { get; set; }

        public List<String> Elements { get; set; }

    }
    
}
