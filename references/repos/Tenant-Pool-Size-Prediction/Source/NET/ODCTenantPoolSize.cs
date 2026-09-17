using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using OutSystems.HubEdition.RuntimePlatform;
using OutSystems.RuntimePublic.Db;

namespace OutSystems.NssODCTenantPoolSize {

	public class CssODCTenantPoolSize: IssODCTenantPoolSize {

		/// <summary>
		/// Action responsible to get the tenant pool size prediction for personal editions requests
		/// using Per-weekday Singular Spectrum Analysis. For each forward day the action queries an
		/// SSA model fitted on the strictly-prior occurrences of that day's weekday — so Monday's
		/// forecast leans on past Mondays alone, Tuesday's on past Tuesdays, and so on.
		/// <para>
		/// This was the most accurate strategy in the head-to-head bake-off on the refreshed
		/// dataset (see <c>docs/Bake-Off.md</c>): MAE 18.19 against the next-best Hybrid (drift)
		/// at 19.05. The bake-off-winning configuration is <c>WindowSize = 21</c> and
		/// <c>TrainingSeriesLength = 112</c>, both expressed in days; the action divides by seven
		/// internally so each weekday's model sees three weeks of weekly window and sixteen weeks
		/// of weekly history.
		/// </para>
		/// <para>
		/// The action returns the central prediction per (PredictionDate, RegionId), rounded up
		/// and clamped to a non-negative integer. The 95 % confidence band is computed internally
		/// to satisfy ML.NET's contract but is not returned: the regional cushion against the
		/// 15-tenants-per-hour pool regeneration rule belongs in the caller, since the
		/// share-by-volume cushion is per-region and depends on context the action does not see.
		/// </para>
		/// </summary>
		/// <param name="ssPersonalEditionRequest">The personal edition data set that will feed the model.</param>
		/// <param name="ssDaysToPredict">Number of days to predict. Default to 1, to only prediect tomorrow. Increase this value to predict more days — i.e. 7 would predict a whole week.</param>
		/// <param name="ssWindowSize">The number of recent observations the SSA model uses as its embedding window when looking for repeating patterns. Larger values let the model detect longer cycles (use 14 for a clear weekly rhythm; 7 is the bare minimum); the value must be smaller than half of TrainingSeriesLength.</param>
		/// <param name="ssTrainingSeriesLength">The number of recent observations the model is fitted on. The most recent TrainingSeriesLength rows of the input series are used for training and older rows are discarded; longer series capture trend better but react more slowly to recent shifts. Must exceed 2 × WindowSize.</param>
		/// <param name="ssConfidenceLevel">Confidence level that ML.NET requires to fit the SSA model. Plumbed through to the underlying forecaster but not surfaced in the output: the action returns the central prediction only. Leave at 0.95 unless you have a specific reason to change it.</param>
		/// <param name="ssZscoreThreshold">Pre-filter that drops observations whose absolute z-score (distance from the mean, in standard deviations) exceeds the threshold before the model is fitted. A value of 0 disables the filter entirely; typical operating values are between 1.5 and 3.0, with lower numbers more aggressive about removing outliers and higher numbers more permissive. The default 2.5 strikes a balance — only genuinely unusual rows are removed.</param>
		/// <param name="ssPersonalEditionPrediction">The personal edition output that model produced. One row per (forecast date, region), with the central prediction rounded up and clamped to a non-negative integer.</param>
		public void MssPersonalEditionPoolSizePredictionSSA_Get(RLPersonalEditionRequestRecordList ssPersonalEditionRequest, int ssDaysToPredict, int ssWindowSize, int ssTrainingSeriesLength, decimal ssConfidenceLevel, decimal ssZscoreThreshold, out RLPersonalEditionPredictionRecordList ssPersonalEditionPrediction) {
			ssPersonalEditionPrediction = new RLPersonalEditionPredictionRecordList();

			if (ssPersonalEditionRequest == null) {
				throw new ArgumentNullException("ssPersonalEditionRequest");
			}

			SsaForecastOptions options = new SsaForecastOptions {
				DaysToPredict = ssDaysToPredict,
				WindowSize = ssWindowSize,
				TrainingSeriesLength = ssTrainingSeriesLength,
				ConfidenceLevel = (float)ssConfidenceLevel,
				ZScoreThreshold = (double)ssZscoreThreshold,
			};
			options.Validate();

			List<STPersonalEditionRequestStructure> allRequests = ExtractRequests(ssPersonalEditionRequest);
			if (allRequests.Count == 0) {
				return;
			}

			PerWeekdaySsaForecaster forecaster = new PerWeekdaySsaForecaster();
			IEnumerable<IGrouping<string, STPersonalEditionRequestStructure>> byRegion = allRequests
				.GroupBy(r => r.ssRegionId, StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

			foreach (IGrouping<string, STPersonalEditionRequestStructure> group in byRegion) {
				List<STPersonalEditionRequestStructure> regionHistory = group
					.OrderBy(r => r.ssRequestDate)
					.ToList();

				IList<ForecastPoint> forecast = forecaster.Forecast(regionHistory, options);
				foreach (ForecastPoint fp in forecast) {
					RCPersonalEditionPredictionRecord record = new RCPersonalEditionPredictionRecord(null);
					record.ssSTPersonalEditionPrediction.ssPredictionDate = fp.Date;
					record.ssSTPersonalEditionPrediction.ssRegionId = group.Key;
					// Central prediction, rounded up and clamped to non-negative. The OutSystems
					// caller applies the per-region cushion derived from the 15-tenants-per-hour
					// regeneration rule; this action returns the model's best estimate of demand,
					// nothing more.
					double safeCount = Math.Ceiling((double)fp.Predicted);
					if (safeCount < 0) {
						safeCount = 0;
					}
					record.ssSTPersonalEditionPrediction.ssPredictionCount = (int)safeCount;
					ssPersonalEditionPrediction.Append(record);
				}
			}
		} // MssPersonalEditionPoolSizePredictionSSA_Get

		/// <summary>
		/// Action responsible to get the tenant pool size prediction for personal editions requests
		/// using SARIMA(p, d, q) × (P, D, Q, s), or SARIMAX with a binary outlier-flag exogenous
		/// regressor — derived inside the extension from the z-score test described below — when
		/// <paramref name="ssIsSARIMAX"/> is true. The OutSystems caller supplies only the request
		/// series and the threshold; no <c>OutlierFlag</c> column is required on the input rows.
		/// <para>
		/// The bake-off-winning configuration on the refreshed dataset for operational
		/// robustness against the 15-tenants-per-hour replenishment rule was
		/// SARIMAX(1, 0, 0) × (1, 0, 0, 7) at <c>TrainingSeriesLength = 140</c> — see
		/// <c>docs/Bake-Off.md</c> for the full hard-fail and worst-shortfall numbers. The
		/// runner-up, SARIMAX(1, 1, 1) × (1, 0, 1, 7) at the same training horizon, trades a
		/// slightly higher hard-fail count for tighter MAE and is a sensible alternative when
		/// bias matters more than tail behaviour.
		/// </para>
		/// <para>
		/// One row is returned per (forecast date, region). The central prediction is rounded up
		/// and clamped to a non-negative integer; the confidence band SARIMA computes internally is
		/// not returned. When SARIMAX is enabled the exogenous regressor is computed inside the
		/// extension via a z-score test (mirroring the SSA action's <c>ssZscoreThreshold</c> input,
		/// but flagging rather than dropping the unusual rows). Future-x is fed as zero across the
		/// forecast horizon: we do not know which forward days will turn out to be outliers, so the
		/// regression contribution is null and the SARIMA structure carries the forecast on its
		/// own.
		/// </para>
		/// </summary>
		/// <param name="ssPersonalEditionRequest">The personal edition data set that will feed the model.</param>
		/// <param name="ssDaysToPredict">Number of days to predict. Default to 1, to only prediect tomorrow. Increase this value to predict more days — i.e. 7 would predict a whole week.</param>
		/// <param name="ssTrainingSeriesLength">Number of historical days fed to the model after the look-back trim. Must exceed 2 × WindowSize. Longer histories improve the model&apos;s grasp of the underlying trend; very long histories can include stale data that no longer reflects current demand.</param>
		/// <param name="ssConfidenceLevel">Confidence level used internally by SARIMA&apos;s residual-band machinery, expressed as a fraction in (0, 1). The action returns the central prediction only, so this value does not directly shape the output, but it must be a finite value in (0, 1) for the model to fit. Leave at 0.95 unless you have a specific reason to change it.</param>
		/// <param name="ssZscoreThreshold">Threshold on the absolute z-score above which a training row is flagged as an outlier and surfaced through SARIMAX&apos;s exogenous regressor. Mirrors the SSA action&apos;s ssZscoreThreshold input, but with a different downstream effect: SSA drops the rows, whereas SARIMAX flags them so the model can learn a coefficient β for &apos;this row was unusual&apos;. A value of 0 (or any non-positive value) disables the regressor entirely — equivalent to running plain SARIMA — and is therefore only meaningful when ssIsSARIMAX is true. Typical operating values are between 1.5 and 3.0; the default 2.5 strikes a balance — only genuinely unusual rows are flagged.</param>
		/// <param name="ssp">Non-seasonal autoregressive order — how many of the most recent days the model leans on directly when predicting the next day. 0 disables this term. Typical range 0–3.</param>
		/// <param name="ssd">Non-seasonal differencing order — how many times the series is differenced before fitting, to remove a steady upward or downward drift. 0 = use the raw series, 1 = remove a linear trend, 2 = remove a quadratic trend.</param>
		/// <param name="ssq">Non-seasonal moving-average order — how many of the most recent prediction errors the model feeds back into the next day&apos;s estimate. 0 disables this term. Typical range 0–3.</param>
		/// <param name="ssP1">Seasonal autoregressive order — like p, but stepping in multiples of the seasonal period s instead of single days. Captures recurring weekly patterns (the same weekday a week earlier, two weeks earlier, and so on). Typical range 0–2.</param>
		/// <param name="ssD1">Seasonal differencing order — like d, but applied across the seasonal cycle. Set to 1 to subtract last week&apos;s same-weekday value from each day before fitting; this is what isolates the underlying trend from the weekly rhythm.</param>
		/// <param name="ssQ1">Seasonal moving-average order — like q, but operating on errors made on the same weekday in earlier weeks. Captures repeating prediction misses tied to a particular weekday. Typical range 0–2</param>
		/// <param name="sss">Seasonal period in days — the length of the cycle the model is asked to recognise. Set to 7 for the weekly rhythm that dominates this dataset; 365 would model an annual cycle (rarely useful here).</param>
		/// <param name="ssIsSARIMAX">When true, the model is fitted as SARIMAX rather than SARIMA — i.e. it derives a per-row outlier flag from the z-score test controlled by ssZscoreThreshold, and learns a regression coefficient against that flag in addition to the autoregressive structure. Leave false for a pure time-series fit; ssZscoreThreshold is then ignored.</param>
		/// <param name="ssPersonalEditionPrediction">The personal edition output that model produced. One row per (forecast date, region), with the central prediction rounded up and clamped to a non-negative integer.</param>
		public void MssPersonalEditionPoolSizePredictionSARIMA_Get(RLPersonalEditionRequestRecordList ssPersonalEditionRequest, int ssDaysToPredict, int ssTrainingSeriesLength, int ssp, int ssd, int ssq, int ssP1, int ssD1, int ssQ1, int sss, bool ssIsSARIMAX, decimal ssConfidenceLevel, decimal ssZscoreThreshold, out RLPersonalEditionPredictionRecordList ssPersonalEditionPrediction) {
			ssPersonalEditionPrediction = new RLPersonalEditionPredictionRecordList();

			if (ssPersonalEditionRequest == null) {
				throw new ArgumentNullException("ssPersonalEditionRequest");
			}

			SarimaForecastOptions options = new SarimaForecastOptions {
				DaysToPredict = ssDaysToPredict,
				TrainingSeriesLength = ssTrainingSeriesLength,
				P = ssp,
				D = ssd,
				Q = ssq,
				SeasonalP = ssP1,
				SeasonalD = ssD1,
				SeasonalQ = ssQ1,
				SeasonalPeriod = sss,
				IsSarimax = ssIsSARIMAX,
				// SARIMA's band machinery requires a finite confidence level even when the action
				// returns the central prediction only. The OutSystems caller supplies it so the SSA
				// and SARIMA actions stay symmetrical; 0.95 is the level at which the bake-off was
				// scored and the obvious default.
				ConfidenceLevel = (double)ssConfidenceLevel,
				ZScoreThreshold = (double)ssZscoreThreshold,
			};
			options.Validate();

			List<STPersonalEditionRequestStructure> allRequests = ExtractRequests(ssPersonalEditionRequest);
			if (allRequests.Count == 0) {
				return;
			}

			SarimaForecaster forecaster = new SarimaForecaster();
			IEnumerable<IGrouping<string, STPersonalEditionRequestStructure>> byRegion = allRequests
				.GroupBy(r => r.ssRegionId, StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

			foreach (IGrouping<string, STPersonalEditionRequestStructure> group in byRegion) {
				List<STPersonalEditionRequestStructure> regionHistory = group
					.OrderBy(r => r.ssRequestDate)
					.ToList();

				IList<SarimaForecastPoint> forecast = forecaster.Forecast(regionHistory, options);
				foreach (SarimaForecastPoint fp in forecast) {
					RCPersonalEditionPredictionRecord record = new RCPersonalEditionPredictionRecord(null);
					record.ssSTPersonalEditionPrediction.ssPredictionDate = fp.Date;
					record.ssSTPersonalEditionPrediction.ssRegionId = group.Key;
					double safeCount = Math.Ceiling(fp.Predicted);
					if (safeCount < 0) {
						safeCount = 0;
					}
					record.ssSTPersonalEditionPrediction.ssPredictionCount = (int)safeCount;
					ssPersonalEditionPrediction.Append(record);
				}
			}
		} // MssPersonalEditionPoolSizePredictionSARIMA_Get

		/// <summary>
		/// Extract the raw <see cref="STPersonalEditionRequestStructure"/> rows from the OutSystems
		/// record list. The wrapping <see cref="RCPersonalEditionRequestRecord"/> defines an implicit
		/// operator to its inner structure (see <c>Records.cs</c>), so we walk the list with
		/// <c>StartIteration</c> / <c>MoveNext</c> / <c>CurrentRec</c> and unwrap each record one at
		/// a time. <c>EndIteration</c> is called in a <c>finally</c> so that an exception thrown by
		/// the caller does not leave the record list in an iterating state.
		/// </summary>
		private static List<STPersonalEditionRequestStructure> ExtractRequests(
			RLPersonalEditionRequestRecordList recordList) {
			List<STPersonalEditionRequestStructure> result = new List<STPersonalEditionRequestStructure>();
			recordList.StartIteration();
			try {
				while (recordList.MoveNext()) {
					RCPersonalEditionRequestRecord row = recordList.CurrentRec;
					result.Add(row.ssSTPersonalEditionRequest);
				}
			} finally {
				recordList.EndIteration();
			}
			return result;
		}

	} // CssODCTenantPoolSize

} // OutSystems.NssODCTenantPoolSize
