using OutSystems.ExternalLibraries.SDK;
using System.Collections;

namespace OutSystems.XmlToJson.Structures
{
    [OSStructure(Description = "XML Nodes that must be converted to a JSON array, even if there's only a single element in the XML.")]
    public struct Node
    {
        [OSStructureField(Description = "Name of XML node.", IsMandatory = true)]
        public string Name;
    }
}
