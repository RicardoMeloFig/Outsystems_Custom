using System;
using System.Collections;
using System.Data;
using System.Runtime.Serialization;
using System.Reflection;
using System.Xml;
using OutSystems.ObjectKeys;
using OutSystems.RuntimeCommon;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.HubEdition.RuntimePlatform.Db;
using OutSystems.Internal.Db;

namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>
	/// Structure <code>RCPersonalEditionRequestRecord</code>
	/// </summary>
	[Serializable()]
	public partial struct RCPersonalEditionRequestRecord: ISerializable, ITypedRecord<RCPersonalEditionRequestRecord> {
		internal static readonly GlobalObjectKey IdPersonalEditionRequest = GlobalObjectKey.Parse("2UmDmepsh0WSfJ_D1JexCA*SJ6dKeOmG3j5y8E2jD5pAg");

		public static void EnsureInitialized() {}
		[System.Xml.Serialization.XmlElement("PersonalEditionRequest")]
		public STPersonalEditionRequestStructure ssSTPersonalEditionRequest;


		public static implicit operator STPersonalEditionRequestStructure(RCPersonalEditionRequestRecord r) {
			return r.ssSTPersonalEditionRequest;
		}

		public static implicit operator RCPersonalEditionRequestRecord(STPersonalEditionRequestStructure r) {
			RCPersonalEditionRequestRecord res = new RCPersonalEditionRequestRecord(null);
			res.ssSTPersonalEditionRequest = r;
			return res;
		}

		public BitArray OptimizedAttributes;

		public RCPersonalEditionRequestRecord(params string[] dummy) {
			OptimizedAttributes = null;
			ssSTPersonalEditionRequest = new STPersonalEditionRequestStructure(null);
		}

		public BitArray[] GetDefaultOptimizedValues() {
			BitArray[] all = new BitArray[1];
			all[0] = null;
			return all;
		}

		public BitArray[] AllOptimizedAttributes {
			set {
				if (value == null) {
				} else {
					ssSTPersonalEditionRequest.OptimizedAttributes = value[0];
				}
			}
			get {
				BitArray[] all = new BitArray[1];
				all[0] = null;
				return all;
			}
		}

		/// <summary>
		/// Read a record from database
		/// </summary>
		/// <param name="r"> Data base reader</param>
		/// <param name="index"> index</param>
		public void Read(IDataReader r, ref int index) {
			ssSTPersonalEditionRequest.Read(r, ref index);
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
		public void ReadIM(RCPersonalEditionRequestRecord r) {
			this = r;
		}


		public static bool operator == (RCPersonalEditionRequestRecord a, RCPersonalEditionRequestRecord b) {
			if (a.ssSTPersonalEditionRequest != b.ssSTPersonalEditionRequest) return false;
			return true;
		}

		public static bool operator != (RCPersonalEditionRequestRecord a, RCPersonalEditionRequestRecord b) {
			return !(a==b);
		}

		public override bool Equals(object o) {
			if (o.GetType() != typeof(RCPersonalEditionRequestRecord)) return false;
			return (this == (RCPersonalEditionRequestRecord) o);
		}

		public override int GetHashCode() {
			try {
				return base.GetHashCode()
				^ ssSTPersonalEditionRequest.GetHashCode()
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

		public RCPersonalEditionRequestRecord(SerializationInfo info, StreamingContext context) {
			OptimizedAttributes = null;
			ssSTPersonalEditionRequest = new STPersonalEditionRequestStructure(null);
			Type objInfo = this.GetType();
			FieldInfo fieldInfo = null;
			fieldInfo = objInfo.GetField("ssSTPersonalEditionRequest", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssSTPersonalEditionRequest' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssSTPersonalEditionRequest = (STPersonalEditionRequestStructure) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
		}

		public void RecursiveReset() {
			ssSTPersonalEditionRequest.RecursiveReset();
		}

		public void InternalRecursiveSave() {
			ssSTPersonalEditionRequest.InternalRecursiveSave();
		}


		public RCPersonalEditionRequestRecord Duplicate() {
			RCPersonalEditionRequestRecord t;
			t.ssSTPersonalEditionRequest = (STPersonalEditionRequestStructure) this.ssSTPersonalEditionRequest.Duplicate();
			t.OptimizedAttributes = null;
			return t;
		}

		IRecord IRecord.Duplicate() {
			return Duplicate();
		}

		public void ToXml(Object parent, System.Xml.XmlElement baseElem, String fieldName, int detailLevel) {
			System.Xml.XmlElement recordElem = VarValue.AppendChild(baseElem, "Record");
			if (fieldName != null) {
				VarValue.AppendAttribute(recordElem, "debug.field", fieldName);
			}
			if (detailLevel > 0) {
				ssSTPersonalEditionRequest.ToXml(this, recordElem, "PersonalEditionRequest", detailLevel - 1);
			} else {
				VarValue.AppendDeferredEvaluationElement(recordElem);
			}
		}

		public void EvaluateFields(VarValue variable, Object parent, String baseName, String fields) {
			String head = VarValue.GetHead(fields);
			String tail = VarValue.GetTail(fields);
			variable.Found = false;
			if (head == "personaleditionrequest") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".PersonalEditionRequest")) variable.Value = ssSTPersonalEditionRequest; else variable.Optimized = true;
				variable.SetFieldName("personaleditionrequest");
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
			if (key == IdPersonalEditionRequest) {
				return ssSTPersonalEditionRequest;
			} else {
				throw new Exception("Invalid key");
			}
		}
		public void FillFromOther(IRecord other) {
			if (other == null) return;
			ssSTPersonalEditionRequest.FillFromOther((IRecord) other.AttributeGet(IdPersonalEditionRequest));
		}
		public bool IsDefault() {
			RCPersonalEditionRequestRecord defaultStruct = new RCPersonalEditionRequestRecord(null);
			if (this.ssSTPersonalEditionRequest != defaultStruct.ssSTPersonalEditionRequest) return false;
			return true;
		}
	} // RCPersonalEditionRequestRecord

	/// <summary>
	/// Structure <code>RCPersonalEditionPredictionRecord</code>
	/// </summary>
	[Serializable()]
	public partial struct RCPersonalEditionPredictionRecord: ISerializable, ITypedRecord<RCPersonalEditionPredictionRecord> {
		internal static readonly GlobalObjectKey IdPersonalEditionPrediction = GlobalObjectKey.Parse("2UmDmepsh0WSfJ_D1JexCA*eOA3EUW4YYuZJIaixkE0fA");

		public static void EnsureInitialized() {}
		[System.Xml.Serialization.XmlElement("PersonalEditionPrediction")]
		public STPersonalEditionPredictionStructure ssSTPersonalEditionPrediction;


		public static implicit operator STPersonalEditionPredictionStructure(RCPersonalEditionPredictionRecord r) {
			return r.ssSTPersonalEditionPrediction;
		}

		public static implicit operator RCPersonalEditionPredictionRecord(STPersonalEditionPredictionStructure r) {
			RCPersonalEditionPredictionRecord res = new RCPersonalEditionPredictionRecord(null);
			res.ssSTPersonalEditionPrediction = r;
			return res;
		}

		public BitArray OptimizedAttributes;

		public RCPersonalEditionPredictionRecord(params string[] dummy) {
			OptimizedAttributes = null;
			ssSTPersonalEditionPrediction = new STPersonalEditionPredictionStructure(null);
		}

		public BitArray[] GetDefaultOptimizedValues() {
			BitArray[] all = new BitArray[1];
			all[0] = null;
			return all;
		}

		public BitArray[] AllOptimizedAttributes {
			set {
				if (value == null) {
				} else {
					ssSTPersonalEditionPrediction.OptimizedAttributes = value[0];
				}
			}
			get {
				BitArray[] all = new BitArray[1];
				all[0] = null;
				return all;
			}
		}

		/// <summary>
		/// Read a record from database
		/// </summary>
		/// <param name="r"> Data base reader</param>
		/// <param name="index"> index</param>
		public void Read(IDataReader r, ref int index) {
			ssSTPersonalEditionPrediction.Read(r, ref index);
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
		public void ReadIM(RCPersonalEditionPredictionRecord r) {
			this = r;
		}


		public static bool operator == (RCPersonalEditionPredictionRecord a, RCPersonalEditionPredictionRecord b) {
			if (a.ssSTPersonalEditionPrediction != b.ssSTPersonalEditionPrediction) return false;
			return true;
		}

		public static bool operator != (RCPersonalEditionPredictionRecord a, RCPersonalEditionPredictionRecord b) {
			return !(a==b);
		}

		public override bool Equals(object o) {
			if (o.GetType() != typeof(RCPersonalEditionPredictionRecord)) return false;
			return (this == (RCPersonalEditionPredictionRecord) o);
		}

		public override int GetHashCode() {
			try {
				return base.GetHashCode()
				^ ssSTPersonalEditionPrediction.GetHashCode()
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

		public RCPersonalEditionPredictionRecord(SerializationInfo info, StreamingContext context) {
			OptimizedAttributes = null;
			ssSTPersonalEditionPrediction = new STPersonalEditionPredictionStructure(null);
			Type objInfo = this.GetType();
			FieldInfo fieldInfo = null;
			fieldInfo = objInfo.GetField("ssSTPersonalEditionPrediction", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
			if (fieldInfo == null) {
				throw new Exception("The field named 'ssSTPersonalEditionPrediction' was not found.");
			}
			if (fieldInfo.FieldType.IsSerializable) {
				ssSTPersonalEditionPrediction = (STPersonalEditionPredictionStructure) info.GetValue(fieldInfo.Name, fieldInfo.FieldType);
			}
		}

		public void RecursiveReset() {
			ssSTPersonalEditionPrediction.RecursiveReset();
		}

		public void InternalRecursiveSave() {
			ssSTPersonalEditionPrediction.InternalRecursiveSave();
		}


		public RCPersonalEditionPredictionRecord Duplicate() {
			RCPersonalEditionPredictionRecord t;
			t.ssSTPersonalEditionPrediction = (STPersonalEditionPredictionStructure) this.ssSTPersonalEditionPrediction.Duplicate();
			t.OptimizedAttributes = null;
			return t;
		}

		IRecord IRecord.Duplicate() {
			return Duplicate();
		}

		public void ToXml(Object parent, System.Xml.XmlElement baseElem, String fieldName, int detailLevel) {
			System.Xml.XmlElement recordElem = VarValue.AppendChild(baseElem, "Record");
			if (fieldName != null) {
				VarValue.AppendAttribute(recordElem, "debug.field", fieldName);
			}
			if (detailLevel > 0) {
				ssSTPersonalEditionPrediction.ToXml(this, recordElem, "PersonalEditionPrediction", detailLevel - 1);
			} else {
				VarValue.AppendDeferredEvaluationElement(recordElem);
			}
		}

		public void EvaluateFields(VarValue variable, Object parent, String baseName, String fields) {
			String head = VarValue.GetHead(fields);
			String tail = VarValue.GetTail(fields);
			variable.Found = false;
			if (head == "personaleditionprediction") {
				if (!VarValue.FieldIsOptimized(parent, baseName + ".PersonalEditionPrediction")) variable.Value = ssSTPersonalEditionPrediction; else variable.Optimized = true;
				variable.SetFieldName("personaleditionprediction");
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
			if (key == IdPersonalEditionPrediction) {
				return ssSTPersonalEditionPrediction;
			} else {
				throw new Exception("Invalid key");
			}
		}
		public void FillFromOther(IRecord other) {
			if (other == null) return;
			ssSTPersonalEditionPrediction.FillFromOther((IRecord) other.AttributeGet(IdPersonalEditionPrediction));
		}
		public bool IsDefault() {
			RCPersonalEditionPredictionRecord defaultStruct = new RCPersonalEditionPredictionRecord(null);
			if (this.ssSTPersonalEditionPrediction != defaultStruct.ssSTPersonalEditionPrediction) return false;
			return true;
		}
	} // RCPersonalEditionPredictionRecord
}
