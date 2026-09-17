using System;
using System.Data;
using System.Collections;
using System.Runtime.Serialization;
using System.Reflection;
using System.Xml;
using OutSystems.ObjectKeys;
using OutSystems.RuntimeCommon;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.HubEdition.RuntimePlatform.Db;
using OutSystems.Internal.Db;
using OutSystems.HubEdition.RuntimePlatform.NewRuntime;

namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>
	/// RecordList type <code>RLPersonalEditionRequestRecordList</code> that represents a record list of
	///  <code>PersonalEditionRequest</code>
	/// </summary>
	[Serializable()]
	public partial class RLPersonalEditionRequestRecordList: GenericRecordList<RCPersonalEditionRequestRecord>, IEnumerable, IEnumerator, ISerializable {
		public static void EnsureInitialized() {}

		protected override RCPersonalEditionRequestRecord GetElementDefaultValue() {
			return new RCPersonalEditionRequestRecord("");
		}

		public T[] ToArray<T>(Func<RCPersonalEditionRequestRecord, T> converter) {
			return ToArray(this, converter);
		}

		public static T[] ToArray<T>(RLPersonalEditionRequestRecordList recordlist, Func<RCPersonalEditionRequestRecord, T> converter) {
			return InnerToArray(recordlist, converter);
		}
		public static implicit operator RLPersonalEditionRequestRecordList(RCPersonalEditionRequestRecord[] array) {
			RLPersonalEditionRequestRecordList result = new RLPersonalEditionRequestRecordList();
			result.InnerFromArray(array);
			return result;
		}

		public static RLPersonalEditionRequestRecordList ToList<T>(T[] array, Func <T, RCPersonalEditionRequestRecord> converter) {
			RLPersonalEditionRequestRecordList result = new RLPersonalEditionRequestRecordList();
			result.InnerFromArray(array, converter);
			return result;
		}

		public static RLPersonalEditionRequestRecordList FromRestList<T>(RestList<T> restList, Func <T, RCPersonalEditionRequestRecord> converter) {
			RLPersonalEditionRequestRecordList result = new RLPersonalEditionRequestRecordList();
			result.InnerFromRestList(restList, converter);
			return result;
		}
		/// <summary>
		/// Default Constructor
		/// </summary>
		public RLPersonalEditionRequestRecordList(): base() {
		}

		/// <summary>
		/// Constructor with transaction parameter
		/// </summary>
		/// <param name="trans"> IDbTransaction Parameter</param>
		[Obsolete("Use the Default Constructor and set the Transaction afterwards.")]
		public RLPersonalEditionRequestRecordList(IDbTransaction trans): base(trans) {
		}

		/// <summary>
		/// Constructor with transaction parameter and alternate read method
		/// </summary>
		/// <param name="trans"> IDbTransaction Parameter</param>
		/// <param name="alternateReadDBMethod"> Alternate Read Method</param>
		[Obsolete("Use the Default Constructor and set the Transaction afterwards.")]
		public RLPersonalEditionRequestRecordList(IDbTransaction trans, ReadDBMethodDelegate alternateReadDBMethod): this(trans) {
			this.alternateReadDBMethod = alternateReadDBMethod;
		}

		/// <summary>
		/// Constructor declaration for serialization
		/// </summary>
		/// <param name="info"> SerializationInfo</param>
		/// <param name="context"> StreamingContext</param>
		public RLPersonalEditionRequestRecordList(SerializationInfo info, StreamingContext context): base(info, context) {
		}

		public override BitArray[] GetDefaultOptimizedValues() {
			BitArray[] def = new BitArray[1];
			def[0] = null;
			return def;
		}
		/// <summary>
		/// Create as new list
		/// </summary>
		/// <returns>The new record list</returns>
		protected override OSList<RCPersonalEditionRequestRecord> NewList() {
			return new RLPersonalEditionRequestRecordList();
		}


	} // RLPersonalEditionRequestRecordList

	/// <summary>
	/// RecordList type <code>RLPersonalEditionPredictionRecordList</code> that represents a record list of
	///  <code>PersonalEditionPrediction</code>
	/// </summary>
	[Serializable()]
	public partial class RLPersonalEditionPredictionRecordList: GenericRecordList<RCPersonalEditionPredictionRecord>, IEnumerable, IEnumerator, ISerializable {
		public static void EnsureInitialized() {}

		protected override RCPersonalEditionPredictionRecord GetElementDefaultValue() {
			return new RCPersonalEditionPredictionRecord("");
		}

		public T[] ToArray<T>(Func<RCPersonalEditionPredictionRecord, T> converter) {
			return ToArray(this, converter);
		}

		public static T[] ToArray<T>(RLPersonalEditionPredictionRecordList recordlist, Func<RCPersonalEditionPredictionRecord, T> converter) {
			return InnerToArray(recordlist, converter);
		}
		public static implicit operator RLPersonalEditionPredictionRecordList(RCPersonalEditionPredictionRecord[] array) {
			RLPersonalEditionPredictionRecordList result = new RLPersonalEditionPredictionRecordList();
			result.InnerFromArray(array);
			return result;
		}

		public static RLPersonalEditionPredictionRecordList ToList<T>(T[] array, Func <T, RCPersonalEditionPredictionRecord> converter) {
			RLPersonalEditionPredictionRecordList result = new RLPersonalEditionPredictionRecordList();
			result.InnerFromArray(array, converter);
			return result;
		}

		public static RLPersonalEditionPredictionRecordList FromRestList<T>(RestList<T> restList, Func <T, RCPersonalEditionPredictionRecord> converter) {
			RLPersonalEditionPredictionRecordList result = new RLPersonalEditionPredictionRecordList();
			result.InnerFromRestList(restList, converter);
			return result;
		}
		/// <summary>
		/// Default Constructor
		/// </summary>
		public RLPersonalEditionPredictionRecordList(): base() {
		}

		/// <summary>
		/// Constructor with transaction parameter
		/// </summary>
		/// <param name="trans"> IDbTransaction Parameter</param>
		[Obsolete("Use the Default Constructor and set the Transaction afterwards.")]
		public RLPersonalEditionPredictionRecordList(IDbTransaction trans): base(trans) {
		}

		/// <summary>
		/// Constructor with transaction parameter and alternate read method
		/// </summary>
		/// <param name="trans"> IDbTransaction Parameter</param>
		/// <param name="alternateReadDBMethod"> Alternate Read Method</param>
		[Obsolete("Use the Default Constructor and set the Transaction afterwards.")]
		public RLPersonalEditionPredictionRecordList(IDbTransaction trans, ReadDBMethodDelegate alternateReadDBMethod): this(trans) {
			this.alternateReadDBMethod = alternateReadDBMethod;
		}

		/// <summary>
		/// Constructor declaration for serialization
		/// </summary>
		/// <param name="info"> SerializationInfo</param>
		/// <param name="context"> StreamingContext</param>
		public RLPersonalEditionPredictionRecordList(SerializationInfo info, StreamingContext context): base(info, context) {
		}

		public override BitArray[] GetDefaultOptimizedValues() {
			BitArray[] def = new BitArray[1];
			def[0] = null;
			return def;
		}
		/// <summary>
		/// Create as new list
		/// </summary>
		/// <returns>The new record list</returns>
		protected override OSList<RCPersonalEditionPredictionRecord> NewList() {
			return new RLPersonalEditionPredictionRecordList();
		}


	} // RLPersonalEditionPredictionRecordList
}
