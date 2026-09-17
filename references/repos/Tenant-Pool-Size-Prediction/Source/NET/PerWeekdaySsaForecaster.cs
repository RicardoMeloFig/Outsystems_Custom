using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;

namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>
	/// One forward-forecast point produced by <see cref="PerWeekdaySsaForecaster"/>: the central
	/// prediction together with its lower and upper bounds. The action returns only the central
	/// prediction (rounded up) as <c>PredictionCount</c>, but the bounds are surfaced here for
	/// completeness and to permit unit-testing the band.
	/// </summary>
	internal struct ForecastPoint {
		public DateTime Date;
		public float Predicted;
		public float LowerBound;
		public float UpperBound;
	}

	/// <summary>
	/// Forward-forecasting variant of the Per-weekday SSA strategy. Each forward day queries an SSA
	/// model fitted on the strictly-prior occurrences of that day's weekday, mirroring the
	/// canonical <c>BacktestPerWeekday</c> in the source-of-truth project but with no held-out
	/// window: the whole region history feeds the fit, and the forecast covers the next
	/// <see cref="SsaForecastOptions.DaysToPredict"/> calendar days after the most recent
	/// observation.
	/// <para>
	/// This is the bake-off-winning configuration for raw forecast accuracy on the refreshed
	/// dataset; see <c>docs/Bake-Off.md</c> for the cross-region results that placed it ahead of
	/// every other catalogue strategy on MAE. It does not, on its own, guarantee operational
	/// robustness against the 15-tenants-per-hour regeneration rule — for that, use the SARIMAX
	/// variant exposed alongside.
	/// </para>
	/// <para>
	/// Implementation note: the <c>SsaForecastOptions.WindowSize</c> and
	/// <c>TrainingSeriesLength</c> values are interpreted in days at the OutSystems boundary and
	/// divided by seven internally to yield weekly units per weekday, consistent with how the
	/// hybrid-stacked forecaster reads the same options. So <c>WindowSize = 21,
	/// TrainingSeriesLength = 112</c> — the bake-off winner — gives a weekly window of three and a
	/// weekly series length of sixteen weeks per weekday.
	/// </para>
	/// </summary>
	internal sealed class PerWeekdaySsaForecaster {

		private readonly MLContext _mlContext = new MLContext(seed: 0);

		/// <summary>
		/// Produce a forward forecast for one region. The history must already be sorted by date —
		/// the caller's responsibility, kept that way to avoid hidden side effects. On insufficient
		/// same-weekday history the method throws with a clearly-worded explanation, in line with
		/// the project's fail-loud policy.
		/// </summary>
		public IList<ForecastPoint> Forecast(
			IList<STPersonalEditionRequestStructure> regionHistory,
			SsaForecastOptions options) {

			if (regionHistory == null) {
				throw new ArgumentNullException("regionHistory");
			}
			if (options == null) {
				throw new ArgumentNullException("options");
			}
			options.Validate();

			if (regionHistory.Count == 0) {
				throw new InvalidOperationException(
					"PerWeekdaySsa: cannot forecast for a region with no observations. Supply at " +
					"least 2 × (WindowSize / 7) same-weekday observations across the history.");
			}

			int windowWeekly, seriesWeekly;
			options.EffectiveWeekly(out windowWeekly, out seriesWeekly);

			// Group the entire history by weekday so each forward day's model has its full
			// same-weekday history available.
			Dictionary<DayOfWeek, List<STPersonalEditionRequestStructure>> byWeekday =
				regionHistory
					.GroupBy(o => o.ssRequestDate.DayOfWeek)
					.ToDictionary(
						g => g.Key,
						g => g.OrderBy(o => o.ssRequestDate).ToList());

			DateTime mostRecent = regionHistory[regionHistory.Count - 1].ssRequestDate;
			List<ForecastPoint> forecasts = new List<ForecastPoint>(options.DaysToPredict);

			for (int dayOffset = 1; dayOffset <= options.DaysToPredict; dayOffset++) {
				DateTime forecastDate = mostRecent.AddDays(dayOffset);
				DayOfWeek targetWeekday = forecastDate.DayOfWeek;

				List<STPersonalEditionRequestStructure> sameWeekday;
				if (!byWeekday.TryGetValue(targetWeekday, out sameWeekday)) {
					throw new InvalidOperationException(
						"PerWeekdaySsa: no observations of weekday " + targetWeekday + " in the " +
						"region's history. The strategy requires at least " + seriesWeekly + " " +
						"same-weekday observations to fit the per-weekday SSA model.");
				}

				IList<STPersonalEditionRequestStructure> filtered = sameWeekday;
				if (options.ZScoreEnabled) {
					filtered = ZScoreFilter.Filter(sameWeekday, options.ZScoreThreshold);
				}
				if (filtered.Count < seriesWeekly) {
					throw new InvalidOperationException(
						"PerWeekdaySsa: only " + filtered.Count + " " + targetWeekday + " " +
						"observations available after filtering, but " + seriesWeekly + " are " +
						"required (TrainingSeriesLength / 7). Lower TrainingSeriesLength, relax " +
						"the z-score filter, or supply a longer history.");
				}

				// Take the most recent 'seriesWeekly' weekday occurrences so the effective
				// training horizon honours the configured value rather than silently growing
				// with whatever filtered history happens to remain.
				List<TenantInput> inputs = filtered
					.Skip(filtered.Count - seriesWeekly)
					.Select(o => new TenantInput { Value = o.ssRequestCount })
					.ToList();

				TenantForecastOutput raw = FitAndPredict(
					inputs, windowWeekly, seriesWeekly,
					horizon: 1, confidenceLevel: options.ConfidenceLevel);

				ForecastPoint p;
				p.Date = forecastDate;
				p.Predicted = raw.ForecastedValues != null && raw.ForecastedValues.Length > 0
					? raw.ForecastedValues[0]
					: 0f;
				p.LowerBound = raw.LowerBound != null && raw.LowerBound.Length > 0
					? raw.LowerBound[0]
					: 0f;
				p.UpperBound = raw.UpperBound != null && raw.UpperBound.Length > 0
					? raw.UpperBound[0]
					: 0f;
				forecasts.Add(p);
			}
			return forecasts;
		}

		/// <summary>
		/// Fit SSA on the supplied <paramref name="inputs"/> and return the next
		/// <paramref name="horizon"/> forecasted values. The horizon-1 contract is the only one
		/// exercised by this class but the API is left general so callers may experiment.
		/// Asserts <c>inputs.Count == seriesLength</c> to surface the past bug where SeriesLength
		/// silently became a no-op because <c>trainSize</c> was set to whatever rows happened to
		/// be available rather than the configured training horizon.
		/// </summary>
		private TenantForecastOutput FitAndPredict(
			IList<TenantInput> inputs,
			int windowSize,
			int seriesLength,
			int horizon,
			float confidenceLevel) {

			if (inputs.Count != seriesLength) {
				throw new ArgumentException(
					"Caller must trim inputs to seriesLength before calling FitAndPredict (got " +
					inputs.Count + ", expected " + seriesLength + ").",
					"inputs");
			}

			IDataView dataView = _mlContext.Data.LoadFromEnumerable(inputs);
			var pipeline = _mlContext.Forecasting.ForecastBySsa(
				outputColumnName: "ForecastedValues",
				inputColumnName: "Value",
				windowSize: windowSize,
				seriesLength: seriesLength,
				trainSize: seriesLength,
				horizon: horizon,
				confidenceLevel: confidenceLevel,
				confidenceLowerBoundColumn: "LowerBound",
				confidenceUpperBoundColumn: "UpperBound");

			var model = pipeline.Fit(dataView);
			var engine = model.CreateTimeSeriesEngine<TenantInput, TenantForecastOutput>(_mlContext);
			return engine.Predict();
		}
	}

}
