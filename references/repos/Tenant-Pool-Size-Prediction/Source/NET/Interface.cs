using System;
using System.Collections;
using System.Data;
using OutSystems.HubEdition.RuntimePlatform;

namespace OutSystems.NssODCTenantPoolSize {

	public interface IssODCTenantPoolSize {

		/// <summary>
		/// Action responsible to get the tenant pool size prediction for personal editions requests using Singular Spectrum Analysis.
		/// </summary>
		/// <param name="ssPersonalEditionRequest">The personal edition data set that will feed the model.</param>
		/// <param name="ssDaysToPredict">Number of days to predict. Default to 1, to only prediect tomorrow. Increase this value to predict more days — i.e. 7 would predict a whole week.</param>
		/// <param name="ssWindowSize">The number of recent observations the SSA model uses as its embedding window when looking for repeating patterns. Larger values let the model detect longer cycles (use 14 for a clear weekly rhythm; 7 is the bare minimum); the value must be smaller than half of TrainingSeriesLength.</param>
		/// <param name="ssTrainingSeriesLength">The number of recent observations the model is fitted on. The most recent TrainingSeriesLength rows of the input series are used for training and older rows are discarded; longer series capture trend better but react more slowly to recent shifts. Must exceed 2 × WindowSize.</param>
		/// <param name="ssConfidenceLevel">The probability mass covered by the predicted lower / upper bounds, expressed as a fraction in (0, 1). At 0.95 the bounds are 95 % credible intervals — i.e. the model&apos;s stated chance that an observation falls within them; raise it (e.g. 0.99) for safer provisioning, lower it for tighter bounds around the central estimate.</param>
		/// <param name="ssZscoreThreshold">Pre-filter that drops observations whose absolute z-score (distance from the mean, in standard deviations) exceeds the threshold before the model is fitted. A value of 0 disables the filter entirely; typical operating values are between 1.5 and 3.0, with lower numbers more aggressive about removing outliers and higher numbers more permissive. The default 2.5 strikes a balance — only genuinely unusual rows are removed.</param>
		/// <param name="ssPersonalEditionPrediction">The personal edition output that model produced.</param>
		void MssPersonalEditionPoolSizePredictionSSA_Get(RLPersonalEditionRequestRecordList ssPersonalEditionRequest, int ssDaysToPredict, int ssWindowSize, int ssTrainingSeriesLength, decimal ssConfidenceLevel, decimal ssZscoreThreshold, out RLPersonalEditionPredictionRecordList ssPersonalEditionPrediction);

		/// <summary>
		/// Action responsible to get the tenant pool size prediction for personal editions requests using SARIMA.
		/// </summary>
		/// <param name="ssPersonalEditionRequest">The personal edition data set that will feed the model.</param>
		/// <param name="ssDaysToPredict">Number of days to predict. Default to 1, to only prediect tomorrow. Increase this value to predict more days — i.e. 7 would predict a whole week.</param>
		/// <param name="ssTrainingSeriesLength">Number of historical days fed to the model after the look-back trim. Must exceed 2 × WindowSize. Longer histories improve the model&apos;s grasp of the underlying trend; very long histories can include stale data that no longer reflects current demand.</param>
		/// <param name="ssp">Non-seasonal autoregressive order — how many of the most recent days the model leans on directly when predicting the next day. 0 disables this term. Typical range 0–3.</param>
		/// <param name="ssd">Non-seasonal differencing order — how many times the series is differenced before fitting, to remove a steady upward or downward drift. 0 = use the raw series, 1 = remove a linear trend, 2 = remove a quadratic trend.</param>
		/// <param name="ssq">Non-seasonal moving-average order — how many of the most recent prediction errors the model feeds back into the next day&apos;s estimate. 0 disables this term. Typical range 0–3.</param>
		/// <param name="ssP1">Seasonal autoregressive order — like p, but stepping in multiples of the seasonal period s instead of single days. Captures recurring weekly patterns (the same weekday a week earlier, two weeks earlier, and so on). Typical range 0–2.</param>
		/// <param name="ssD1">Seasonal differencing order — like d, but applied across the seasonal cycle. Set to 1 to subtract last week&apos;s same-weekday value from each day before fitting; this is what isolates the underlying trend from the weekly rhythm.</param>
		/// <param name="ssQ1">Seasonal moving-average order — like q, but operating on errors made on the same weekday in earlier weeks. Captures repeating prediction misses tied to a particular weekday. Typical range 0–2</param>
		/// <param name="sss">Seasonal period in days — the length of the cycle the model is asked to recognise. Set to 7 for the weekly rhythm that dominates this dataset; 365 would model an annual cycle (rarely useful here).</param>
		/// <param name="ssIsSARIMAX">When true, the model is fitted as SARIMAX rather than SARIMA — i.e. it also reads the additional predictor columns supplied with the input record list (such as known campaign days or rate-limit caps), letting the prediction account for events the autoregressive structure cannot infer from the tenant counts alone. Leave false for a pure time-series fit.</param>
		/// <param name="ssConfidenceLevel">The probability mass covered by the predicted lower / upper bounds, expressed as a fraction in (0, 1). At 0.95 the bounds are 95 % credible intervals — i.e. the model&apos;s stated chance that an observation falls within them; raise it (e.g. 0.99) for safer provisioning, lower it for tighter bounds around the central estimate.</param>
		/// <param name="ssZscoreThreshold">Pre-filter that drops observations whose absolute z-score (distance from the mean, in standard deviations) exceeds the threshold before the model is fitted. A value of 0 disables the filter entirely; typical operating values are between 1.5 and 3.0, with lower numbers more aggressive about removing outliers and higher numbers more permissive. The default 2.5 strikes a balance — only genuinely unusual rows are removed.</param>
		/// <param name="ssPersonalEditionPrediction">The personal edition output that model produced.</param>
		void MssPersonalEditionPoolSizePredictionSARIMA_Get(RLPersonalEditionRequestRecordList ssPersonalEditionRequest, int ssDaysToPredict, int ssTrainingSeriesLength, int ssp, int ssd, int ssq, int ssP1, int ssD1, int ssQ1, int sss, bool ssIsSARIMAX, decimal ssConfidenceLevel, decimal ssZscoreThreshold, out RLPersonalEditionPredictionRecordList ssPersonalEditionPrediction);

	} // IssODCTenantPoolSize

} // OutSystems.NssODCTenantPoolSize
