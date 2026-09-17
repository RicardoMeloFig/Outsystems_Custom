using System;
using System.Collections.Generic;
using System.Linq;

namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>
	/// One forward-forecast point produced by <see cref="SarimaForecaster"/>. Mirrors the shape of
	/// <see cref="ForecastPoint"/> from the SSA family but uses <c>double</c> internally because
	/// SARIMA computes everything in double-precision. The field set is intentionally identical
	/// so the OutSystems-side serialisation path stays the same.
	/// </summary>
	internal struct SarimaForecastPoint {
		public DateTime Date;
		public double Predicted;
		public double LowerBound;
		public double UpperBound;
	}

	/// <summary>
	/// Forward-forecasting orchestrator for SARIMA / SARIMAX. Groups the input history by region,
	/// builds the per-region series, trims to the configured training horizon, fits the model and
	/// produces the requested forward horizon.
	/// <para>
	/// When SARIMAX is enabled the exogenous regressor is computed inside the extension via
	/// <see cref="ZScoreFilter.ComputeFlags"/>: rows whose absolute z-score (within the trimmed
	/// training slice) exceeds <see cref="SarimaForecastOptions.ZScoreThreshold"/> are flagged and
	/// surfaced as the <c>x</c> column. This is the symmetric counterpart of the SSA action's
	/// z-score handling — SSA drops outlier rows, SARIMAX flags them — and means the OutSystems
	/// caller does not have to populate an OutlierFlag column upstream: a single
	/// <c>ssZscoreThreshold</c> input controls the same notion of "unusual" for both actions.
	/// </para>
	/// <para>
	/// Future-x values for SARIMAX are taken to be zero across the forecast horizon. This matches
	/// the bake-off framing: the flag is a label of an already-observed unusual day, and we never
	/// know whether the next forecast day will turn out to be an outlier. β × 0 simply means
	/// "the SARIMA structure carries the day on its own, with the regression contribution adding
	/// zero".
	/// </para>
	/// </summary>
	internal sealed class SarimaForecaster {

		public IList<SarimaForecastPoint> Forecast(
			IList<STPersonalEditionRequestStructure> regionHistory,
			SarimaForecastOptions options) {

			if (regionHistory == null) {
				throw new ArgumentNullException("regionHistory");
			}
			if (options == null) {
				throw new ArgumentNullException("options");
			}
			options.Validate();

			if (regionHistory.Count < options.TrainingSeriesLength) {
				throw new InvalidOperationException(
					"Sarima: region history has " + regionHistory.Count + " observations but " +
					"TrainingSeriesLength = " + options.TrainingSeriesLength + " were requested. " +
					"Either lower TrainingSeriesLength or supply a longer history for the region.");
			}

			// Trim to the most recent TrainingSeriesLength rows so the fit honours the configured
			// training horizon, exactly as the canonical bake-off does in BacktestSarima. The
			// caller is expected to have sorted the history by date already; we sort defensively
			// here because the cost is negligible (LINQ over a few hundred rows) and a misordered
			// series silently produces a wrong fit.
			List<STPersonalEditionRequestStructure> ordered = regionHistory
				.OrderBy(r => r.ssRequestDate)
				.ToList();
			List<STPersonalEditionRequestStructure> trainingSlice = ordered
				.Skip(ordered.Count - options.TrainingSeriesLength)
				.ToList();

			double[] series = new double[trainingSlice.Count];
			for (int i = 0; i < trainingSlice.Count; i++) {
				series[i] = trainingSlice[i].ssRequestCount;
			}

			// SARIMAX needs an exogenous regressor at the same length as the training series.
			// We derive it here from a z-score test on the trimmed slice — flagging the rows
			// SSA's z-score filter would have dropped — so the same threshold draws the same
			// line on the histogram regardless of which action the caller invokes.
			double[] exogenous = null;
			if (options.IsSarimax) {
				bool[] flags = ZScoreFilter.ComputeFlags(trainingSlice, options.ZScoreThreshold);
				exogenous = new double[trainingSlice.Count];
				for (int i = 0; i < trainingSlice.Count; i++) {
					exogenous[i] = flags[i] ? 1.0 : 0.0;
				}
			}

			SarimaModel model = SarimaModel.Fit(
				series,
				exogenous,
				options.P, options.D, options.Q,
				options.SeasonalP, options.SeasonalD, options.SeasonalQ, options.SeasonalPeriod);

			// SARIMAX needs a future-x vector at forecast time, even when (as here) we have no
			// foreknowledge of which days will turn out to be outliers. Passing zero across the
			// horizon contributes β × 0 = 0 — the SARIMA structure alone determines the forecast,
			// while still satisfying the model's API contract that future-x must be supplied iff
			// the model was fitted with one.
			double[] futureX = options.IsSarimax ? new double[options.DaysToPredict] : null;
			SarimaForecast forecast = options.IsSarimax
				? model.Forecast(options.DaysToPredict, options.ConfidenceLevel, futureX)
				: model.Forecast(options.DaysToPredict, options.ConfidenceLevel);

			DateTime mostRecent = trainingSlice[trainingSlice.Count - 1].ssRequestDate;
			List<SarimaForecastPoint> points = new List<SarimaForecastPoint>(options.DaysToPredict);
			for (int h = 0; h < options.DaysToPredict; h++) {
				SarimaForecastPoint p;
				p.Date = mostRecent.AddDays(h + 1);
				p.Predicted = forecast.Forecast[h];
				p.LowerBound = forecast.LowerBound[h];
				p.UpperBound = forecast.UpperBound[h];
				points.Add(p);
			}
			return points;
		}
	}

}
