using System;

namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>
	/// Configuration for one forward forecast pass with the SARIMA(p, d, q) × (P, D, Q, s) model
	/// (or SARIMAX, when <see cref="IsSarimax"/> is true). Field semantics mirror the parameters
	/// declared on the OutSystems action.
	/// <para>
	/// The OutSystems action exposes every parameter so callers may tune. The bake-off-winning
	/// configuration on the refreshed dataset is SARIMAX(1, 0, 0) × (1, 0, 0, 7) with a training
	/// horizon of 140 days; the matching action call therefore looks like
	/// <c>(p=1, d=0, q=0, P=1, D=0, Q=0, s=7, TrainingSeriesLength=140, IsSarimax=true)</c>. The
	/// (1, 1, 1) × (1, 0, 1, 7) variant is a close second on hard-fail rate; either is suitable
	/// for the operational target of robustness against the 15-tenants-per-hour regeneration
	/// rule.
	/// </para>
	/// </summary>
	internal sealed class SarimaForecastOptions {

		public const int MaxDaysToPredict = 7;

		public int DaysToPredict { get; set; }

		/// <summary>Number of historical days fed to the model after the look-back trim. Must
		/// satisfy SARIMA's mathematical fit requirement
		/// <c>length &gt; max(p + P × s, q + Q × s) + 2</c> after differencing — the model checks
		/// this internally and throws if violated.</summary>
		public int TrainingSeriesLength { get; set; }

		public int P { get; set; }
		public int D { get; set; }
		public int Q { get; set; }
		public int SeasonalP { get; set; }
		public int SeasonalD { get; set; }
		public int SeasonalQ { get; set; }
		public int SeasonalPeriod { get; set; }
		public bool IsSarimax { get; set; }

		/// <summary>Confidence level for the forecast band. Required because <see cref="SarimaModel"/>
		/// returns a band even when the action discards it; kept on the options object so a future
		/// caller wishing to expose the upper-bound view does not need a model rewrite. The action
		/// at the time of writing returns the central prediction only, so this value is plumbed
		/// through but does not affect <c>PredictionCount</c>.</summary>
		public double ConfidenceLevel { get; set; }

		/// <summary>Threshold on the absolute z-score above which a training row is flagged as an
		/// outlier and surfaced through SARIMAX's exogenous regressor. Mirrors the SSA action's
		/// <c>ssZscoreThreshold</c> input but with a different downstream effect: SSA <i>drops</i>
		/// the rows, whereas SARIMAX <i>flags</i> them so the model can learn a coefficient β for
		/// "this row was unusual". A value of zero (or any non-positive value) disables the
		/// regressor entirely — equivalent to running plain SARIMA — and is therefore only
		/// meaningful when <see cref="IsSarimax"/> is true. Typical operating values are between
		/// 1.5 and 3.0.</summary>
		public double ZScoreThreshold { get; set; }

		public void Validate() {
			if (DaysToPredict < 1 || DaysToPredict > MaxDaysToPredict) {
				throw new ArgumentOutOfRangeException("DaysToPredict",
					"DaysToPredict must lie in [1, " + MaxDaysToPredict + "]. Longer horizons are " +
					"rejected to avoid silently producing a degraded forecast.");
			}
			if (TrainingSeriesLength < 14) {
				throw new ArgumentOutOfRangeException("TrainingSeriesLength",
					"TrainingSeriesLength must be at least 14 days for SARIMA to have any chance of " +
					"capturing a weekly cycle (got " + TrainingSeriesLength + ").");
			}
			if (P < 0 || D < 0 || Q < 0 || SeasonalP < 0 || SeasonalD < 0 || SeasonalQ < 0) {
				throw new ArgumentOutOfRangeException(
					"P/D/Q/SeasonalP/SeasonalD/SeasonalQ",
					"All SARIMA orders must be non-negative.");
			}
			if (SeasonalPeriod < 1) {
				throw new ArgumentOutOfRangeException("SeasonalPeriod",
					"SeasonalPeriod must be at least 1. Set to 7 for the weekly cycle on daily data.");
			}
			if (ConfidenceLevel <= 0 || ConfidenceLevel >= 1) {
				throw new ArgumentOutOfRangeException("ConfidenceLevel",
					"ConfidenceLevel must lie in (0, 1). Even when the action returns the central " +
					"prediction, SARIMA's downstream band machinery requires a finite confidence level.");
			}
			if (ZScoreThreshold < 0) {
				throw new ArgumentOutOfRangeException("ZScoreThreshold",
					"ZScoreThreshold must be zero (disable the SARIMAX regressor) or positive " +
					"(typical 1.5–3.0).");
			}
		}
	}

}
