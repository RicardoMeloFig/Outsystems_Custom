using System;
using System.Collections;
using System.Data;
using System.Reflection;
using System.Runtime.Serialization;
using OutSystems.ObjectKeys;
using OutSystems.RuntimeCommon;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.HubEdition.RuntimePlatform.Db;
using OutSystems.Internal.Db;

namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>
	/// Structure <code>STPersonalEditionRequestStructure</code> that represents the Service Studio
	///  structure <code>PersonalEditionRequest</code> <p> Description: Holds the structure for the data se
	/// t that will calculate the pool size for the Personal Editions in ODC.</p>
	/// </summary>
	[Serializable()]
	public partial struct STPersonalEditionRequestStructure: ISerializable, ITypedRecord<STPersonalEditionRequestStructure>, ISimpleRecord {
		internal static readonly GlobalObjectKey IdRequestDate = GlobalObjectKey.Parse("emFZbP4Up0K491JvIWJEaA*PixLuFcQxkaTVDIc4m4wMw");
		internal static readonly GlobalObjectKey IdRegionId = GlobalObjectKey.Parse("emFZbP4Up0K491JvIWJEaA*TWUu_E5rBk6HImU0HLJsxg");
		internal static readonly GlobalObjectKey IdRequestCount = GlobalObjectKey.Parse("emFZbP4Up0K491JvIWJEaA*iUEMqXF4UUWFssD9J68H9Q");

		public static void EnsureInitialized() {}
		[System.Xml.Serialization.XmlElement("RequestDate")]
		public DateTime ssRequestDate;

		[System.Xml.Serialization.XmlElement("RegionId")]
		public string ssRegionId;

		[System.Xml.Serialization.XmlElement("RequestCount")]
		public int ssRequestCount;


		public BitArray OptimizedAttributes;

		public STPersonalEditionRequestStructure(params string[] dummy) {
			OptimizedAttributes = null;
			ssRequestDate = new DateTime(1900, 1, 1, 0, 0, 0);
			ssRegionId = "";
			ssRequestCount = 0;
		}

		public BitArray[] GetDefaultOptimizedValues() {
			BitArray[] all = new BitArray[0];
			return all;
		}

		public BitArray[] AllOptimizedAttributes {
			set {
				if (value == null) {
				} else {
				}
			}
			get {
				BitArray[] all = new BitArray[0];
				return all;
			}
		}

		/// <summary>
		/// Read a record from database
		/// </summary>
		/// <param name="r"> Data base reader</param>
		/// <param name="index"> index</param>
		public void Read(IDataReader r, ref int index) {
			ssRequestDate = r.ReadDate(index++, "PersonalEditionRequest.RequestDate", new DateTime(1900, 1, 1, 0, 0, 0));
			ssRegionId = r.ReadText(index++, "PersonalEditionRequest.RegionId", "");
			ssRequestCount = r.ReadInteger(index++, "PersonalEditionRequest.RequestCount", 0);
		}
		/// <summary>
		/// Read from database
		/// </summary>
		/// <param name="r"> Data reader</param>
		public void ReadDB(IDataReader r) {
			int index = 0;
			Read(r, ref index);
		}

		/// <summary>
		/// Read from record
		/// </summary>
		/// <param name="r"> Record</param>
		public void ReadIM(STPersonalEditionRequestStructure r) {
			this = r;
		}


		public static bool operator == (STPersonalEditionRequestStructure a, STPersonalEditionRequestStructure b) {
			if (a.ssRequestDate != b.ssRequestDate) return false;
			if (a.ssRegionId != b.ssRegionId) return false;
			if (a.ssRequestCount != b.ssRequestCount) return false;
			return true;
		}

		public static bool operator != (STPersonalEditionRequestStructure a, STPersonalEditionRequestStructure b) {
			return !(a==b);
		}

		public override bool Equals(object o) {
			if (o.GetType() != typeof(STPersonalEditionRequestStructure)) return false;
			return (this == (STPersonalEditionRequestStructure) o);
		}

		public override int GetHashCode() {
			try {
				return base.GetHashCode()
				^ ssRequestDate.GetHashCode()
				^ ssRegionId.GetHashCode()
				^ ssRequestCount.GetHashCode()
				;
			} catch {
				return base.GetHashCode();
			}
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context) {
			Type objInfo = this.GetType();
			FieldInfo[] fields;
			fields = objInfo.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			for (int i = 0; i < fields.Length; i++)
			if (fields[i] .FieldType.IsSerializable)
			info.AddValue(fields[i] .Name, fields[i] .GetValue(this));
		}

		public STPersonalEditionRequestStructure(SerializationInfo info, StreamingContext context) {
			OptimizedAttributes = null;
			ssRequestDate = new DateTime(1900, 1, 1, 0, 0, 0);
			ssRegionId = "";
			ssRequestCount = 0;
			Type objInfo = this.GetType();
			FieldInfo fieldInfo = null;
			fieldInfo = objInfo.GetField("ssRequestDate", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssRequestDate' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssRequestDate = (DateTime) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
			fieldInfo = objInfo.GetField("ssRegionId", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssRegionId' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssRegionId = (string) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
			fieldInfo = objInfo.GetField("ssRequestCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssRequestCount' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssRequestCount = (int) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
		}

		public void RecursiveReset() {
		}

		public void InternalRecursiveSave() {
		}


		public STPersonalEditionRequestStructure Duplicate() {
			STPersonalEditionRequestStructure t;
			t.ssRequestDate = this.ssRequestDate;
			t.ssRegionId = this.ssRegionId;
			t.ssRequestCount = this.ssRequestCount;
			t.OptimizedAttributes = null;
			return t;
		}

		IRecord IRecord.Duplicate() {
			return Duplicate();
		}

		public void ToXml(Object parent, System.Xml.XmlElement baseElem, String fieldName, int detailLevel) {
			System.Xml.XmlElement recordElem = VarValue.AppendChild(baseElem, "Structure");
			if (fieldName != null) {
				VarValue.AppendAttribute(recordElem, "debug.field", fieldName);
				fieldName = fieldName.ToLowerInvariant();
			}
			if (detailLevel > 0) {
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".RequestDate")) VarValue.AppendAttribute(recordElem, "RequestDate", ssRequestDate, detailLevel, TypeKind.Date); else VarValue.AppendOptimizedAttribute(recordElem, "RequestDate");
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".RegionId")) VarValue.AppendAttribute(recordElem, "RegionId", ssRegionId, detailLevel, TypeKind.Text); else VarValue.AppendOptimizedAttribute(recordElem, "RegionId");
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".RequestCount")) VarValue.AppendAttribute(recordElem, "RequestCount", ssRequestCount, detailLevel, TypeKind.Integer); else VarValue.AppendOptimizedAttribute(recordElem, "RequestCount");
			} else {
				VarValue.AppendDeferredEvaluationElement(recordElem);
			}
		}

		public void EvaluateFields(VarValue variable, Object parent, String baseName, String fields) {
			String head = VarValue.GetHead(fields);
			String tail = VarValue.GetTail(fields);
			variable.Found = false;
			if (head == "requestdate") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".RequestDate")) variable.Value = ssRequestDate; else variable.Optimized = true;
			} else if (head == "regionid") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".RegionId")) variable.Value = ssRegionId; else variable.Optimized = true;
			} else if (head == "requestcount") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".RequestCount")) variable.Value = ssRequestCount; else variable.Optimized = true;
			}
			if (variable.Found && tail != null) variable.EvaluateFields(this, head, tail);
		}

		public bool ChangedAttributeGet(GlobalObjectKey key) {
			throw new Exception("Method not Supported");
		}

		public bool OptimizedAttributeGet(GlobalObjectKey key) {
			throw new Exception("Method not Supported");
		}

		public object AttributeGet(GlobalObjectKey key) {
			if (key == IdRequestDate) {
				return ssRequestDate;
			} else if (key == IdRegionId) {
				return ssRegionId;
			} else if (key == IdRequestCount) {
				return ssRequestCount;
			} else {
				throw new Exception("Invalid key");
			}
		}
		public void FillFromOther(IRecord other) {
			if (other == null) return;
			ssRequestDate = (DateTime) other.AttributeGet(IdRequestDate);
			ssRegionId = (string) other.AttributeGet(IdRegionId);
			ssRequestCount = (int) other.AttributeGet(IdRequestCount);
		}
		public bool IsDefault() {
			STPersonalEditionRequestStructure defaultStruct = new STPersonalEditionRequestStructure(null);
			if (this.ssRequestDate != defaultStruct.ssRequestDate) return false;
			if (this.ssRegionId != defaultStruct.ssRegionId) return false;
			if (this.ssRequestCount != defaultStruct.ssRequestCount) return false;
			return true;
		}
	} // STPersonalEditionRequestStructure

	/// <summary>
	/// Structure <code>STPersonalEditionPredictionStructure</code> that represents the Service Studio
	///  structure <code>PersonalEditionPrediction</code> <p> Description: Holds the structure for th
	/// e prediction.</p>
	/// </summary>
	[Serializable()]
	public partial struct STPersonalEditionPredictionStructure: ISerializable, ITypedRecord<STPersonalEditionPredictionStructure>, ISimpleRecord {
		internal static readonly GlobalObjectKey IdPredictionDate = GlobalObjectKey.Parse("emFZbP4Up0K491JvIWJEaA*PlsOcYRnW0WlVdYrfsjzPA");
		internal static readonly GlobalObjectKey IdRegionId = GlobalObjectKey.Parse("emFZbP4Up0K491JvIWJEaA*DLqvNJgQxUuqNcDnHgEUFA");
		internal static readonly GlobalObjectKey IdPredictionCount = GlobalObjectKey.Parse("emFZbP4Up0K491JvIWJEaA*zQY2XrUY5EyibpNNAeBYBg");

		public static void EnsureInitialized() {}
		[System.Xml.Serialization.XmlElement("PredictionDate")]
		public DateTime ssPredictionDate;

		[System.Xml.Serialization.XmlElement("RegionId")]
		public string ssRegionId;

		[System.Xml.Serialization.XmlElement("PredictionCount")]
		public int ssPredictionCount;


		public BitArray OptimizedAttributes;

		public STPersonalEditionPredictionStructure(params string[] dummy) {
			OptimizedAttributes = null;
			ssPredictionDate = new DateTime(1900, 1, 1, 0, 0, 0);
			ssRegionId = "";
			ssPredictionCount = 0;
		}

		public BitArray[] GetDefaultOptimizedValues() {
			BitArray[] all = new BitArray[0];
			return all;
		}

		public BitArray[] AllOptimizedAttributes {
			set {
				if (value == null) {
				} else {
				}
			}
			get {
				BitArray[] all = new BitArray[0];
				return all;
			}
		}

		/// <summary>
		/// Read a record from database
		/// </summary>
		/// <param name="r"> Data base reader</param>
		/// <param name="index"> index</param>
		public void Read(IDataReader r, ref int index) {
			ssPredictionDate = r.ReadDate(index++, "PersonalEditionPrediction.PredictionDate", new DateTime(1900, 1, 1, 0, 0, 0));
			ssRegionId = r.ReadText(index++, "PersonalEditionPrediction.RegionId", "");
			ssPredictionCount = r.ReadInteger(index++, "PersonalEditionPrediction.PredictionCount", 0);
		}
		/// <summary>
		/// Read from database
		/// </summary>
		/// <param name="r"> Data reader</param>
		public void ReadDB(IDataReader r) {
			int index = 0;
			Read(r, ref index);
		}

		/// <summary>
		/// Read from record
		/// </summary>
		/// <param name="r"> Record</param>
		public void ReadIM(STPersonalEditionPredictionStructure r) {
			this = r;
		}


		public static bool operator == (STPersonalEditionPredictionStructure a, STPersonalEditionPredictionStructure b) {
			if (a.ssPredictionDate != b.ssPredictionDate) return false;
			if (a.ssRegionId != b.ssRegionId) return false;
			if (a.ssPredictionCount != b.ssPredictionCount) return false;
			return true;
		}

		public static bool operator != (STPersonalEditionPredictionStructure a, STPersonalEditionPredictionStructure b) {
			return !(a==b);
		}

		public override bool Equals(object o) {
			if (o.GetType() != typeof(STPersonalEditionPredictionStructure)) return false;
			return (this == (STPersonalEditionPredictionStructure) o);
		}

		public override int GetHashCode() {
			try {
				return base.GetHashCode()
				^ ssPredictionDate.GetHashCode()
				^ ssRegionId.GetHashCode()
				^ ssPredictionCount.GetHashCode()
				;
			} catch {
				return base.GetHashCode();
			}
		}

		public void GetObjectData(SerializationInfo info, StreamingContext context) {
			Type objInfo = this.GetType();
			FieldInfo[] fields;
			fields = objInfo.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			for (int i = 0; i < fields.Length; i++)
			if (fields[i] .FieldType.IsSerializable)
			info.AddValue(fields[i] .Name, fields[i] .GetValue(this));
		}

		public STPersonalEditionPredictionStructure(SerializationInfo info, StreamingContext context) {
			OptimizedAttributes = null;
			ssPredictionDate = new DateTime(1900, 1, 1, 0, 0, 0);
			ssRegionId = "";
			ssPredictionCount = 0;
			Type objInfo = this.GetType();
			FieldInfo fieldInfo = null;
			fieldInfo = objInfo.GetField("ssPredictionDate", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssPredictionDate' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssPredictionDate = (DateTime) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
			fieldInfo = objInfo.GetField("ssRegionId", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssRegionId' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssRegionId = (string) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
			fieldInfo = objInfo.GetField("ssPredictionCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssPredictionCount' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssPredictionCount = (int) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
		}

		public void RecursiveReset() {
		}

		public void InternalRecursiveSave() {
		}


		public STPersonalEditionPredictionStructure Duplicate() {
			STPersonalEditionPredictionStructure t;
			t.ssPredictionDate = this.ssPredictionDate;
			t.ssRegionId = this.ssRegionId;
			t.ssPredictionCount = this.ssPredictionCount;
			t.OptimizedAttributes = null;
			return t;
		}

		IRecord IRecord.Duplicate() {
			return Duplicate();
		}

		public void ToXml(Object parent, System.Xml.XmlElement baseElem, String fieldName, int detailLevel) {
			System.Xml.XmlElement recordElem = VarValue.AppendChild(baseElem, "Structure");
			if (fieldName != null) {
				VarValue.AppendAttribute(recordElem, "debug.field", fieldName);
				fieldName = fieldName.ToLowerInvariant();
			}
			if (detailLevel > 0) {
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".PredictionDate")) VarValue.AppendAttribute(recordElem, "PredictionDate", ssPredictionDate, detailLevel, TypeKind.Date); else VarValue.AppendOptimizedAttribute(recordElem, "PredictionDate");
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".RegionId")) VarValue.AppendAttribute(recordElem, "RegionId", ssRegionId, detailLevel, TypeKind.Text); else VarValue.AppendOptimizedAttribute(recordElem, "RegionId");
				if (!VarValue.FieldIsOptimized(parent, fieldName + ".PredictionCount")) VarValue.AppendAttribute(recordElem, "PredictionCount", ssPredictionCount, detailLevel, TypeKind.Integer); else VarValue.AppendOptimizedAttribute(recordElem, "PredictionCount");
			} else {
				VarValue.AppendDeferredEvaluationElement(recordElem);
			}
		}

		public void EvaluateFields(VarValue variable, Object parent, String baseName, String fields) {
			String head = VarValue.GetHead(fields);
			String tail = VarValue.GetTail(fields);
			variable.Found = false;
			if (head == "predictiondate") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".PredictionDate")) variable.Value = ssPredictionDate; else variable.Optimized = true;
			} else if (head == "regionid") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".RegionId")) variable.Value = ssRegionId; else variable.Optimized = true;
			} else if (head == "predictioncount") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".PredictionCount")) variable.Value = ssPredictionCount; else variable.Optimized = true;
			}
			if (variable.Found && tail != null) variable.EvaluateFields(this, head, tail);
		}

		public bool ChangedAttributeGet(GlobalObjectKey key) {
			throw new Exception("Method not Supported");
		}

		public bool OptimizedAttributeGet(GlobalObjectKey key) {
			throw new Exception("Method not Supported");
		}

		public object AttributeGet(GlobalObjectKey key) {
			if (key == IdPredictionDate) {
				return ssPredictionDate;
			} else if (key == IdRegionId) {
				return ssRegionId;
			} else if (key == IdPredictionCount) {
				return ssPredictionCount;
			} else {
				throw new Exception("Invalid key");
			}
		}
		public void FillFromOther(IRecord other) {
			if (other == null) return;
			ssPredictionDate = (DateTime) other.AttributeGet(IdPredictionDate);
			ssRegionId = (string) other.AttributeGet(IdRegionId);
			ssPredictionCount = (int) other.AttributeGet(IdPredictionCount);
		}
		public bool IsDefault() {
			STPersonalEditionPredictionStructure defaultStruct = new STPersonalEditionPredictionStructure(null);
			if (this.ssPredictionDate != defaultStruct.ssPredictionDate) return false;
			if (this.ssRegionId != defaultStruct.ssRegionId) return false;
			if (this.ssPredictionCount != defaultStruct.ssPredictionCount) return false;
			return true;
		}
	} // STPersonalEditionPredictionStructure

} // OutSystems.NssODCTenantPoolSize
