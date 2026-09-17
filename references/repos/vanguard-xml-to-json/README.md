# vanguard-xml-to-json
ODC XmlToJson Forge Component - RDV-691

This is a port of the O11 [XmlToJson](https://www.outsystems.com/forge/component-overview/3709/xmltojson-o11) to ODC.

**THIS CODE IS NOT SUPPORTED BY OUTSYSTEMS.**

------------

A big word of appreciation to the OutSystems Community team who created and is maintaining this component in O11:
- Tiago Bojikian Costa Vital
- Borislav Shumarov
- Kilian Hekhuis

------------
## Description
XmlToJson is a simple External Logic Library that converts an XML document to the equivalent JSON.

The library uses NewtonSoft.Json's [JsonConvert.SerializeXmlNode](https://www.newtonsoft.com/json/help/html/M_Newtonsoft_Json_JsonConvert_SerializeXmlNode_1.htm "JsonConvert.SerializeXmlNode") function for the heavy lifting, meaning that any limitations this asset has are due to that function.

## Usage
This library exposes a single method called ConvertXmlToJson.

### ConvertXmlToJson
This method converts an XML document to the equivalent JSON.

#### Input parameters
`XML`: {Text; Mandatory} XML to be converted to JSON.
It must be valid XML, or it cannot be parsed. Note that any namespaces are parsed as well and included in the JSON.
Any attributes are included as well, with an "@" in front of the name.

`ArrayNodes`: {Node List; Not Mandatory} Optional list of the (local) node names that must be converted to a JSON array, even if there's only a single element in the XML.
This helps with situations where you may sometimes have a single element and sometimes multiple elements.
Without specifying the node to be treated as an array, the JSON will sometimes contain an object, sometimes an array of objects, which means you cannot convert the JSON to a structure easily.
If specified, each node in the XML document that has a local name that is equal to one of the node names in the list will become an array in the output JSON.

#### Output parameters
`JSON`: {Text} The converted JSON.
The resulting JSON can be used to create structures from (design time), then serialized (run time).

## Acknowledgments
This project is based on the OutSystems 11 Forge component  [XmlToJson](https://www.outsystems.com/forge/component-overview/3709/xmltojson-o11).

This project uses the third-party Newtonsoft.Json framework. Please refer to https://www.newtonsoft.com/json, https://www.nuget.org/packages/Newtonsoft.Json/, and  https://github.com/JamesNK/Newtonsoft.Json for more information.
