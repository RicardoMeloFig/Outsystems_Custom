namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>ML.NET input shape for the SSA forecaster: a single float feature per time step.</summary>
	internal sealed class TenantInput {
		public float Value { get; set; }
	}

	/// <summary>ML.NET output shape: the horizon forecast and the matching lower / upper bounds.
	/// Each array is <c>horizon</c> long.</summary>
	internal sealed class TenantForecastOutput {
		public float[] ForecastedValues { get; set; }
		public float[] LowerBound { get; set; }
		public float[] UpperBound { get; set; }
	}

}
