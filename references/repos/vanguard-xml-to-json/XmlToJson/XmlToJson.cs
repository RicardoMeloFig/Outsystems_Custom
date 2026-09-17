using Newtonsoft.Json;
using OutSystems.ExternalLibraries.SDK;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Xml;
using System.Xml.Linq;

namespace OutSystems.XmlToJson
{
    public class XmlToJson : IXmlToJson
    {
        public string ConvertXmlToJson(
            [OSParameter(DataType = OSDataType.Text, Description = "XML to be converted to JSON.")]
            string XML,
            [OSParameter(Description = "Optional list of nodes that must be converted to a JSON array, even if there's only a single element in the XML.")]
            IEnumerable<Structures.Node>? ArrayNodes = null)
        {
            string json = string.Empty;

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(XML);


            if (ArrayNodes != null) {
            foreach (var item in ArrayNodes)
            {
                // it may be possible to be a better select than fetch all, but I couldn't get it to work with random namespaces
                foreach (XmlNode node in doc.SelectNodes("//*")!)
                {
                    if (node.LocalName == item.Name)
                    {
                        XmlAttribute attr = doc.CreateAttribute("json", "Array", "http://james.newtonking.com/projects/json");
                        attr.Value = "true";

                        node.Attributes!.Append(attr);
                    }

                }

            }
        }

            json = JsonConvert.SerializeXmlNode(doc);

            return json;
        }

    }
}