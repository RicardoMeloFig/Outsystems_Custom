using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OutSystems.ExternalLibraries.SDK;

namespace OutSystems.XmlToJson
{
    [OSInterface(
        Description = "Converts the supplied XML to JSON.",
        Name = "XmlToJson", 
        IconResourceName = "XmlToJson.resources.xmltojson.png")]
    public interface IXmlToJson
    {
        [OSAction(Description = "Converts the supplied XML to JSON.",
            ReturnName = "JSON", OriginalName = "XmlToJson")]
        public string ConvertXmlToJson(
            [OSParameter(DataType = OSDataType.Text, Description = "XML to be converted to JSON.")] 
            string XML,
            [OSParameter(Description = "Optional list of nodes that must be converted to a JSON array, even if there's only a single element in the XML.")]
            IEnumerable<Structures.Node>? ArrayNodes = null);
    }
}
