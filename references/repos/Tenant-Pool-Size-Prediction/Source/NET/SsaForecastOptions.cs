using System;

namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>
	/// Configuration for one forward forecast pass against a single region's daily series, in the
	/// hybrid-stacked variant adapted for OutSystems. Field semantics mirror the parameters declared
	/// on the OutSystems action: <c>WindowSize</c> and <c>TrainingSeriesLength</c> are expressed in
	/// days, but for the per-weekday limb (and the residual stack derived from it) they are divided
	/// by seven internally to yield weekly window and series lengths per weekday — so passing
	/// <c>WindowSize = 14, TrainingSeriesLength = 84</c> gives twelve weeks of weekly granularity per
	/// weekday and fourteen days of daily granularity for the contiguous residual fit.
	/// </summary>
	internal sealed class SsaForecastOptions {

		/// <summary>Maximum forward horizon supported by the hybrid-stacked strategy. The strategy
		/// was designed and validated for a one-week horizon; longer horizons are rejected loudly
		/// rather than allowed to silently produce a degraded forecast.</summary>
		public const int MaxDaysToPredict = 7;

		public int DaysToPredict { get; set; }
		public int WindowSize { get; set; }
		public int TrainingSeriesLength { get; set; }
		public float ConfidenceLevel { get; set; }

		/// <summary>When greater than zero, observations whose absolute z-score exceeds this value
		/// are dropped before SSA fits the series. Set to zero to disable the filter entirely.
		/// Typical operating values are 1.5 to 3.0, with 2.5 a sensible default.</summary>
		public double ZScoreThreshold { get; set; }

		/// <summary>True iff the z-score pre-filter is enabled (i.e. <see cref="ZScoreThreshold"/>
		/// is strictly positive). Per the OutSystems contract a value of zero disables the filter.</summary>
		public bool ZScoreEnabled { get { return ZScoreThreshold > 0; } }

		/// <summary>Effective (window, series) pair after the day → week conversion. The hybrid
		/// strategy's per-weekday limb consumes same-weekday sub-series, so the configured daily
		/// values are divided by seven before being passed to SSA. The contiguous residual limb,
		/// by contrast, sees a daily series and uses the configured values verbatim.</summary>
		public void EffectiveWeekly(out int window, out int series) {
			window = Math.Max(2, WindowSize / 7);
			series = Math.Max(1, TrainingSeriesLength / 7);
		}

		/// <summary>SSA requires <c>WindowSize &lt; SeriesLength / 2</c> and at least one full
		/// period in the chosen units. In the per-weekday limb the daily values must therefore be
		/// at least 14 (window) and 7 (series) so the weekly mapping yields non-degenerate values.
		/// The forward horizon must lie in <c>[1, MaxDaysToPredict]</c>.</summary>
		public void Validate() {
			if (DaysToPredict < 1 || DaysToPredict > MaxDaysToPredict) {
				throw new ArgumentOutOfRangeException("DaysToPredict",
					"DaysToPredict must lie in [1, " + MaxDaysToPredict + "]. The hybrid-stacked " +
					"strategy is designed for a one-week horizon; longer horizons are rejected to " +
					"avoid silently producing a degraded forecast.");
			}
			if (ConfidenceLevel <= 0f || ConfidenceLevel >= 1f) {
				throw new ArgumentOutOfRangeException("ConfidenceLevel",
					"ConfidenceLevel must lie in (0, 1) — typically 0.95 for 95% credible bounds.");
			}
			if (ZScoreThreshold < 0) {
				throw new ArgumentOutOfRangeException("ZScoreThreshold",
					"ZScoreThreshold must be zero (disable the filter) or positive (typical 1.5–3.0).");
			}
			if (WindowSize < 14) {
				throw new ArgumentOutOfRangeException("WindowSize",
					"The hybrid-stacked strategy divides WindowSize by 7 internally for the " +
					"per-weekday limb; values below 14 would yield a weekly window of less than 2.");
			}
			if (TrainingSeriesLength <= 2 * WindowSize) {
				throw new ArgumentOutOfRangeException("TrainingSeriesLength",
					"TrainingSeriesLength (" + TrainingSeriesLength + ") must exceed 2 × WindowSize " +
					"(" + (2 * WindowSize) + ") — SSA's mathematical requirement for a non-degenerate " +
					"trajectory matrix.");
			}
			int wWeekly, sWeekly;
			EffectiveWeekly(out wWeekly, out sWeekly);
			if (sWeekly <= 2 * wWeekly) {
				throw new ArgumentOutOfRangeException("TrainingSeriesLength",
					"Effective weekly series length (" + sWeekly + ") must exceed 2 × effective " +
					"weekly window (" + (2 * wWeekly) + ") for the per-weekday limb. Increase " +
					"TrainingSeriesLength or decrease WindowSize.");
			}
		}
	}

}
