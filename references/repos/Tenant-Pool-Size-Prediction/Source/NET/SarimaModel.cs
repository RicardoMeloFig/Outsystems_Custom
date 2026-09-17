using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>
	/// One SARIMA(p, d, q) × (P, D, Q, s) (or SARIMAX with one exogenous regressor) forecast horizon:
	/// the central prediction together with the lower and upper confidence bounds at the requested
	/// confidence level. Bounds are derived from the model's estimated residual variance under a
	/// normal-error assumption. Plain class rather than a record, since the OutSystems target
	/// framework (.NET Framework 4.8) does not support C# 9 records.
	/// </summary>
	internal sealed class SarimaForecast {

		public IList<double> Forecast { get; private set; }
		public IList<double> LowerBound { get; private set; }
		public IList<double> UpperBound { get; private set; }
		public double ResidualStdDev { get; private set; }

		public SarimaForecast(IList<double> forecast, IList<double> lowerBound, IList<double> upperBound, double residualStdDev) {
			Forecast = forecast;
			LowerBound = lowerBound;
			UpperBound = upperBound;
			ResidualStdDev = residualStdDev;
		}
	}

	/// <summary>
	/// Self-contained Box-Jenkins SARIMA(p, d, q) × (P, D, Q, s) implementation built on top of
	/// <c>MathNet.Numerics</c>, with an optional single-regressor SARIMAX layer. Fits the joint AR /
	/// seasonal AR / MA / seasonal MA coefficients by minimising the conditional sum of squared
	/// residuals of the doubly-differenced series; the optimisation is derivative-free via
	/// Nelder-Mead simplex. The seasonal extension is mathematically the multiplication of the
	/// non-seasonal and seasonal AR / MA polynomials, expanded once at fit time so the residual
	/// recurrence remains a simple linear combination of past observations and past residuals.
	/// <para>
	/// When a non-null exogenous series is supplied to the SARIMAX <c>Fit</c> overload, the model
	/// becomes <c>y_t = β x_t + u_t</c> where <c>u_t</c> follows the SARIMA structure above. The
	/// regression coefficient β is estimated by ordinary least squares on the demeaned training
	/// pair, and the SARIMA component is then fitted to the resulting residual series.
	/// Forecast-time exogenous values must be supplied via the <c>Forecast</c> overload that takes
	/// a <c>futureExogenous</c> argument; the SARIMA forecast and the regression contribution are
	/// summed at each horizon step. This is the conventional concentrated-MLE simplification of
	/// SARIMAX and matches how <c>statsmodels</c> behaves when the regression coefficient is
	/// conditioned on rather than jointly maximised.
	/// </para>
	/// <para>
	/// This class is a faithful back-port of <c>SarimaModel</c> in the canonical
	/// <c>TenantPoolForecast</c> project, adapted from C# 12 / .NET 8 syntax to the C# 7.3 / .NET
	/// Framework 4.8 surface that Integration Studio targets. The numeric algorithm is
	/// byte-equivalent — the differences are purely syntactic (no records, no nullable reference
	/// annotations, no collection expressions, explicit ArgumentNullException calls in place of
	/// <c>ThrowIfNull</c>, etc.).
	/// </para>
	/// </summary>
	internal sealed class SarimaModel {

		private readonly int _p;
		private readonly int _d;
		private readonly int _q;
		private readonly int _seasonalP;
		private readonly int _seasonalD;
		private readonly int _seasonalQ;
		private readonly int _seasonalPeriod;
		private readonly double[] _expandedAr;
		private readonly double[] _expandedMa;
		private readonly double _intercept;
		private readonly double _residualVariance;
		private readonly double[] _trainingSeries;
		private readonly double[] _trainingResiduals;
		private readonly double _exogCoefficient;
		private readonly bool _hasExogenousRegressor;

		private SarimaModel(
			int p, int d, int q,
			int seasonalP, int seasonalD, int seasonalQ, int seasonalPeriod,
			double[] expandedAr,
			double[] expandedMa,
			double intercept,
			double residualVariance,
			double[] trainingSeries,
			double[] trainingResiduals,
			double exogCoefficient,
			bool hasExogenousRegressor) {
			_p = p;
			_d = d;
			_q = q;
			_seasonalP = seasonalP;
			_seasonalD = seasonalD;
			_seasonalQ = seasonalQ;
			_seasonalPeriod = seasonalPeriod;
			_expandedAr = expandedAr;
			_expandedMa = expandedMa;
			_intercept = intercept;
			_residualVariance = residualVariance;
			_trainingSeries = trainingSeries;
			_trainingResiduals = trainingResiduals;
			_exogCoefficient = exogCoefficient;
			_hasExogenousRegressor = hasExogenousRegressor;
		}

		/// <summary>True iff the model was fitted with an exogenous regressor (i.e. is SARIMAX),
		/// regardless of whether the resulting β was zero.</summary>
		public bool HasExogenousRegressor { get { return _hasExogenousRegressor; } }

		/// <summary>The OLS coefficient β on the exogenous regressor, or 0 when none was used (or
		/// when the training x was constant and the OLS denominator was zero).</summary>
		public double ExogenousCoefficient { get { return _exogCoefficient; } }

		public int P { get { return _p; } }
		public int D { get { return _d; } }
		public int Q { get { return _q; } }
		public int SeasonalP { get { return _seasonalP; } }
		public int SeasonalD { get { return _seasonalD; } }
		public int SeasonalQ { get { return _seasonalQ; } }
		public int SeasonalPeriod { get { return _seasonalPeriod; } }

		/// <summary>Estimated standard deviation of the one-step-ahead residuals on the training series.</summary>
		public double ResidualStdDev { get { return Math.Sqrt(Math.Max(_residualVariance, 0)); } }

		/// <summary>
		/// Convenience wrapper for plain SARIMA — no exogenous regressor — that delegates to the
		/// SARIMAX overload with the exogenous series set to <c>null</c>.
		/// </summary>
		public static SarimaModel Fit(
			IList<double> series,
			int p, int d, int q,
			int seasonalP, int seasonalD, int seasonalQ, int seasonalPeriod) {
			return Fit(series, null, p, d, q, seasonalP, seasonalD, seasonalQ, seasonalPeriod);
		}

		/// <summary>
		/// SARIMAX fit. When <paramref name="exogenousSeries"/> is non-null and has the same length
		/// as <paramref name="series"/>, the model becomes <c>y_t = β x_t + u_t</c> where
		/// <c>u_t</c> follows the SARIMA structure above. β is estimated by ordinary least squares
		/// on the demeaned training pair, and SARIMA is then fitted on the residual series; the
		/// regression component is restored at forecast time by adding β × x_future to each SARIMA
		/// projection. When <paramref name="exogenousSeries"/> is null this overload behaves
		/// identically to the bare SARIMA fit.
		/// </summary>
		public static SarimaModel Fit(
			IList<double> series,
			IList<double> exogenousSeries,
			int p, int d, int q,
			int seasonalP, int seasonalD, int seasonalQ, int seasonalPeriod) {

			if (series == null) throw new ArgumentNullException("series");
			if (p < 0) throw new ArgumentOutOfRangeException("p");
			if (d < 0) throw new ArgumentOutOfRangeException("d");
			if (q < 0) throw new ArgumentOutOfRangeException("q");
			if (seasonalP < 0) throw new ArgumentOutOfRangeException("seasonalP");
			if (seasonalD < 0) throw new ArgumentOutOfRangeException("seasonalD");
			if (seasonalQ < 0) throw new ArgumentOutOfRangeException("seasonalQ");
			if (seasonalPeriod < 1) throw new ArgumentOutOfRangeException("seasonalPeriod");
			if (exogenousSeries != null && exogenousSeries.Count != series.Count) {
				throw new ArgumentException(
					"Exogenous series length (" + exogenousSeries.Count + ") must match the y series length (" +
					series.Count + ").",
					"exogenousSeries");
			}

			double[] raw = series.ToArray();

			// SARIMAX layer: regress y on x by OLS over the demeaned training window. Storing β
			// separately from the SARIMA intercept lets the forecast restore β × x_future as a
			// deterministic regression component without conflating the two pieces of information.
			double exogCoefficient = 0;
			bool hasExogenous = exogenousSeries != null;
			double[] workingSeries = raw;
			if (exogenousSeries != null) {
				double[] xValues = exogenousSeries.ToArray();
				double xMean = xValues.Length == 0 ? 0 : xValues.Average();
				double yMean = raw.Average();
				double xxSum = 0;
				double xySum = 0;
				for (int i = 0; i < raw.Length; i++) {
					double dx = xValues[i] - xMean;
					double dy = raw[i] - yMean;
					xxSum += dx * dx;
					xySum += dx * dy;
				}
				// Degenerate exogenous series (constant column, in particular all-zero) — β is
				// undefined, so we set it to zero. The model still records that an exogenous
				// regressor was supplied at fit time so callers must (and may) pass future-x at
				// forecast time, where it will contribute β × x = 0 — i.e. the SARIMAX result
				// gracefully matches the plain SARIMA result on this slice.
				exogCoefficient = xxSum > 0 ? xySum / xxSum : 0;

				workingSeries = new double[raw.Length];
				for (int i = 0; i < raw.Length; i++) {
					workingSeries[i] = raw[i] - exogCoefficient * xValues[i];
				}
			}

			// Combined differencing: d first-differences and D seasonal-differences at lag s. The
			// order does not matter mathematically; we apply non-seasonal first, mirroring the
			// convention of statsmodels' SARIMAX implementation.
			double[] differenced = ApplyDifferencing(workingSeries, d, seasonalD, seasonalPeriod);
			int maxLagFit = Math.Max(p + seasonalP * seasonalPeriod, q + seasonalQ * seasonalPeriod);
			if (differenced.Length < maxLagFit + 2) {
				throw new ArgumentException(
					"Differenced series of length " + differenced.Length + " is too short for SARIMA (" +
					p + ", " + d + ", " + q + ") × (" + seasonalP + ", " + seasonalD + ", " + seasonalQ +
					", " + seasonalPeriod + "); need at least max(p + P × s, q + Q × s) + 2 = " +
					(maxLagFit + 2) + " points after differencing.",
					"series");
			}

			double mean = differenced.Average();
			double[] centred = new double[differenced.Length];
			for (int i = 0; i < differenced.Length; i++) {
				centred[i] = differenced[i] - mean;
			}

			int nParams = p + q + seasonalP + seasonalQ;
			if (nParams == 0) {
				// Degenerate case: SARIMA(0, d, 0) × (0, D, 0, s) — no dynamics, mean of differenced
				// series is the only parameter. The forecast is the mean repeated, integrated.
				double sse0 = 0;
				for (int i = 0; i < centred.Length; i++) sse0 += centred[i] * centred[i];
				double variance0 = centred.Length == 0 ? 0 : sse0 / centred.Length;
				return new SarimaModel(
					p, d, q, seasonalP, seasonalD, seasonalQ, seasonalPeriod,
					new double[seasonalP * seasonalPeriod + p],
					new double[seasonalQ * seasonalPeriod + q],
					mean, variance0,
					workingSeries, centred,
					exogCoefficient, hasExogenous);
			}

			// Yule-Walker on the centred series gives a usable AR seed; zeros for MA / seasonal MA.
			double[] arSeed = p > 0 ? YuleWalker(centred, p) : new double[0];
			double[] seasonalArSeed = new double[seasonalP];
			double[] maSeed = new double[q];
			double[] seasonalMaSeed = new double[seasonalQ];

			double[] seedArray = new double[nParams];
			int seedOffset = 0;
			for (int i = 0; i < p; i++) seedArray[seedOffset++] = arSeed[i];
			for (int i = 0; i < seasonalP; i++) seedArray[seedOffset++] = seasonalArSeed[i];
			for (int i = 0; i < q; i++) seedArray[seedOffset++] = maSeed[i];
			for (int i = 0; i < seasonalQ; i++) seedArray[seedOffset++] = seasonalMaSeed[i];
			Vector<double> initialGuess = Vector<double>.Build.Dense(seedArray);

			int pLocal = p, sPLocal = seasonalP, qLocal = q, sQLocal = seasonalQ, sPeriodLocal = seasonalPeriod;
			IObjectiveFunction objective = ObjectiveFunction.Value(theta => {
				double[] arT, sarT, maT, smaT;
				Unpack(theta, pLocal, sPLocal, qLocal, sQLocal, out arT, out sarT, out maT, out smaT);
				double[] expandedArInner = ExpandPolynomial(arT, sarT, sPeriodLocal);
				double[] expandedMaInner = ExpandPolynomial(maT, smaT, sPeriodLocal);
				return ConditionalSumOfSquares(centred, expandedArInner, expandedMaInner);
			});

			Vector<double> perturbation = Vector<double>.Build.Dense(nParams, idx => 0.1);
			Vector<double> solution;
			try {
				MinimizationResult result = NelderMeadSimplex.Minimum(
					objective, initialGuess, perturbation,
					convergenceTolerance: 1e-8, maximumIterations: 5000);
				solution = result.MinimizingPoint;
			} catch (MaximumIterationsException ex) {
				// MathNet stashes the best-so-far point in the exception's Data dictionary under
				// the key "MinimizingPoint" — keep the same fallback behaviour as the canonical
				// project so unconverged fits still yield a usable model.
				object partial = ex.Data["MinimizingPoint"];
				if (partial is Vector<double>) {
					solution = (Vector<double>)partial;
				} else {
					throw;
				}
			}

			double[] fittedAr, fittedSar, fittedMa, fittedSma;
			Unpack(solution, p, seasonalP, q, seasonalQ, out fittedAr, out fittedSar, out fittedMa, out fittedSma);
			double[] finalExpandedAr = ExpandPolynomial(fittedAr, fittedSar, seasonalPeriod);
			double[] finalExpandedMa = ExpandPolynomial(fittedMa, fittedSma, seasonalPeriod);

			double[] residuals;
			double sse;
			ComputeResiduals(centred, finalExpandedAr, finalExpandedMa, out residuals, out sse);
			int residualCount = Math.Max(1, residuals.Length - Math.Max(finalExpandedAr.Length, finalExpandedMa.Length));
			double residualVariance = sse / residualCount;

			return new SarimaModel(
				p, d, q, seasonalP, seasonalD, seasonalQ, seasonalPeriod,
				finalExpandedAr, finalExpandedMa, mean, residualVariance, workingSeries, residuals,
				exogCoefficient, hasExogenous);
		}

		/// <summary>
		/// Forecast <paramref name="horizon"/> steps ahead. SARIMA-only overload — throws if the
		/// model was fitted with an exogenous regressor.
		/// </summary>
		public SarimaForecast Forecast(int horizon, double confidenceLevel) {
			return Forecast(horizon, confidenceLevel, null);
		}

		/// <summary>
		/// Forecast <paramref name="horizon"/> steps ahead, threading the supplied future
		/// exogenous values through the SARIMAX regression component. The SARIMA projection runs
		/// on the residual scale (<c>y − β x</c>) and the regression contribution is added back
		/// point-wise, mirroring how the SARIMAX <c>Fit</c> overload constructed the working
		/// series.
		/// </summary>
		public SarimaForecast Forecast(int horizon, double confidenceLevel, IList<double> futureExogenous) {
			if (horizon <= 0) throw new ArgumentOutOfRangeException("horizon");
			if (confidenceLevel <= 0 || confidenceLevel >= 1) throw new ArgumentOutOfRangeException("confidenceLevel");
			if (HasExogenousRegressor && futureExogenous == null) {
				throw new InvalidOperationException(
					"Model was fitted with an exogenous regressor; future exogenous values must be supplied " +
					"to the Forecast call. Use the overload that takes a futureExogenous argument.");
			}
			if (!HasExogenousRegressor && futureExogenous != null) {
				throw new InvalidOperationException(
					"Model was fitted without an exogenous regressor; futureExogenous must be null. " +
					"If you intended a SARIMAX forecast, refit the model with an exogenous training series.");
			}
			if (futureExogenous != null && futureExogenous.Count != horizon) {
				throw new ArgumentException(
					"futureExogenous length (" + futureExogenous.Count + ") must equal horizon (" + horizon + ").",
					"futureExogenous");
			}

			double[] differencedHistory = ApplyDifferencing(_trainingSeries, _d, _seasonalD, _seasonalPeriod);
			List<double> centredHistory = new List<double>(differencedHistory.Length + horizon);
			for (int i = 0; i < differencedHistory.Length; i++) {
				centredHistory.Add(differencedHistory[i] - _intercept);
			}
			List<double> residualHistory = new List<double>(_trainingResiduals.Length + horizon);
			for (int i = 0; i < _trainingResiduals.Length; i++) {
				residualHistory.Add(_trainingResiduals[i]);
			}

			double[] forecastsCentred = new double[horizon];
			for (int h = 0; h < horizon; h++) {
				double prediction = 0;
				for (int i = 0; i < _expandedAr.Length; i++) {
					if (_expandedAr[i] == 0) continue;
					int idx = centredHistory.Count - 1 - i;
					if (idx < 0) break;
					prediction += _expandedAr[i] * centredHistory[idx];
				}
				for (int j = 0; j < _expandedMa.Length; j++) {
					if (_expandedMa[j] == 0) continue;
					int idx = residualHistory.Count - 1 - j;
					if (idx < 0) break;
					prediction += _expandedMa[j] * residualHistory[idx];
				}
				forecastsCentred[h] = prediction;
				centredHistory.Add(prediction);
				residualHistory.Add(0);
			}

			double[] forecastsDifferenced = new double[horizon];
			for (int h = 0; h < horizon; h++) {
				forecastsDifferenced[h] = forecastsCentred[h] + _intercept;
			}
			// SARIMA forecast lives on the (y − β x) scale; integrate over the doubly-differenced
			// chain back to that scale, then add the regression contribution β × x_future to
			// recover the original-y forecast.
			double[] forecastWorking = UndoDifferencing(_trainingSeries, forecastsDifferenced, _d, _seasonalD, _seasonalPeriod);
			double[] forecastOriginal = new double[horizon];
			Array.Copy(forecastWorking, forecastOriginal, horizon);
			if (futureExogenous != null) {
				for (int h = 0; h < horizon; h++) {
					forecastOriginal[h] = forecastWorking[h] + _exogCoefficient * futureExogenous[h];
				}
			}

			double[] psi = MovingAverageRepresentation(_expandedAr, _expandedMa, horizon);
			double z = NormalQuantile(0.5 * (1.0 + confidenceLevel));
			double[] lower = new double[horizon];
			double[] upper = new double[horizon];
			double cumPsiSquared = 0;
			for (int h = 0; h < horizon; h++) {
				cumPsiSquared += psi[h] * psi[h];
				double stdH = Math.Sqrt(_residualVariance * cumPsiSquared);
				double halfWidth = z * stdH;
				lower[h] = forecastOriginal[h] - halfWidth;
				upper[h] = forecastOriginal[h] + halfWidth;
			}

			return new SarimaForecast(forecastOriginal, lower, upper, ResidualStdDev);
		}

		// ---------------------------------------------------------------------------------------
		// Helpers
		// ---------------------------------------------------------------------------------------

		/// <summary>Apply <paramref name="d"/> first-differences then <paramref name="seasonalD"/>
		/// seasonal differences at lag <paramref name="seasonalPeriod"/>.</summary>
		private static double[] ApplyDifferencing(IList<double> series, int d, int seasonalD, int seasonalPeriod) {
			double[] current = series.ToArray();
			for (int pass = 0; pass < d; pass++) {
				current = FirstDifference(current);
			}
			for (int pass = 0; pass < seasonalD; pass++) {
				current = SeasonalDifference(current, seasonalPeriod);
			}
			return current;
		}

		private static double[] FirstDifference(double[] source) {
			double[] next = new double[source.Length - 1];
			for (int i = 0; i < next.Length; i++) next[i] = source[i + 1] - source[i];
			return next;
		}

		private static double[] SeasonalDifference(double[] source, int seasonalPeriod) {
			if (source.Length <= seasonalPeriod) {
				throw new ArgumentException(
					"Cannot apply seasonal differencing at lag " + seasonalPeriod + " to a series of length " +
					source.Length + ".");
			}
			double[] next = new double[source.Length - seasonalPeriod];
			for (int i = 0; i < next.Length; i++) next[i] = source[i + seasonalPeriod] - source[i];
			return next;
		}

		/// <summary>
		/// Undo <paramref name="d"/> first-differences and <paramref name="seasonalD"/> seasonal-
		/// differences on a forecast. Operations are inverted in reverse order: seasonal first,
		/// then non-seasonal.
		/// </summary>
		private static double[] UndoDifferencing(
			IList<double> originalSeries,
			double[] differencedForecast,
			int d, int seasonalD, int seasonalPeriod) {

			if (d == 0 && seasonalD == 0) {
				double[] copy = new double[differencedForecast.Length];
				Array.Copy(differencedForecast, copy, copy.Length);
				return copy;
			}

			List<double[]> levels = new List<double[]>();
			levels.Add(originalSeries.ToArray());
			double[] current = levels[0];
			for (int pass = 0; pass < d; pass++) {
				current = FirstDifference(current);
				levels.Add(current);
			}
			for (int pass = 0; pass < seasonalD; pass++) {
				current = SeasonalDifference(current, seasonalPeriod);
				levels.Add(current);
			}

			double[] integrated = new double[differencedForecast.Length];
			Array.Copy(differencedForecast, integrated, integrated.Length);
			for (int pass = 0; pass < seasonalD; pass++) {
				int levelIndex = d + seasonalD - 1 - pass;
				double[] prevSeries = levels[levelIndex];
				integrated = IntegrateSeasonal(prevSeries, integrated, seasonalPeriod);
			}
			for (int pass = 0; pass < d; pass++) {
				int levelIndex = d - 1 - pass;
				double[] prevSeries = levels[levelIndex];
				integrated = IntegrateFirst(prevSeries, integrated);
			}
			return integrated;
		}

		private static double[] IntegrateFirst(IList<double> prevSeries, double[] differencedForecast) {
			double[] rebuilt = new double[differencedForecast.Length];
			double running = prevSeries[prevSeries.Count - 1];
			for (int i = 0; i < differencedForecast.Length; i++) {
				running += differencedForecast[i];
				rebuilt[i] = running;
			}
			return rebuilt;
		}

		private static double[] IntegrateSeasonal(IList<double> prevSeries, double[] differencedForecast, int seasonalPeriod) {
			double[] rebuilt = new double[differencedForecast.Length];
			List<double> buffer = new List<double>(seasonalPeriod + differencedForecast.Length);
			int startIndex = Math.Max(0, prevSeries.Count - seasonalPeriod);
			for (int i = startIndex; i < prevSeries.Count; i++) buffer.Add(prevSeries[i]);

			for (int i = 0; i < differencedForecast.Length; i++) {
				int bufferLen = buffer.Count;
				int anchorIndex = bufferLen - seasonalPeriod;
				double anchor = anchorIndex >= 0 ? buffer[anchorIndex] : 0;
				double value = anchor + differencedForecast[i];
				rebuilt[i] = value;
				buffer.Add(value);
			}
			return rebuilt;
		}

		private static void Unpack(
			Vector<double> theta, int p, int seasonalP, int q, int seasonalQ,
			out double[] ar, out double[] seasonalAr, out double[] ma, out double[] seasonalMa) {

			ar = new double[p];
			seasonalAr = new double[seasonalP];
			ma = new double[q];
			seasonalMa = new double[seasonalQ];

			int offset = 0;
			for (int i = 0; i < p; i++) ar[i] = theta[offset++];
			for (int i = 0; i < seasonalP; i++) seasonalAr[i] = theta[offset++];
			for (int i = 0; i < q; i++) ma[i] = theta[offset++];
			for (int i = 0; i < seasonalQ; i++) seasonalMa[i] = theta[offset++];
		}

		/// <summary>
		/// Multiply the non-seasonal polynomial by the seasonal polynomial (at lags s, 2s, …) and
		/// return the expanded coefficient array indexed by lag (1..maxLag). Element at index
		/// <c>i - 1</c> is the coefficient at lag <c>i</c>; missing lags hold zero.
		/// </summary>
		private static double[] ExpandPolynomial(double[] nonSeasonal, double[] seasonal, int seasonalPeriod) {
			int p = nonSeasonal.Length;
			int seasonalOrder = seasonal.Length;
			int maxLag = p + seasonalOrder * seasonalPeriod;
			if (maxLag == 0) return new double[0];

			double[] expanded = new double[maxLag];

			for (int i = 1; i <= p; i++) {
				expanded[i - 1] += nonSeasonal[i - 1];
			}
			for (int j = 1; j <= seasonalOrder; j++) {
				int lag = j * seasonalPeriod;
				expanded[lag - 1] += seasonal[j - 1];
			}
			// SARIMA: (1 - phi B)(1 - Phi B^s) = 1 - phi B - Phi B^s + phi Phi B^{1+s}. The +
			// signs above match the conventional residual recurrence; the cross term contributes
			// -phi_i × Phi_j at lag i + j × s.
			for (int i = 1; i <= p; i++) {
				for (int j = 1; j <= seasonalOrder; j++) {
					int lag = i + j * seasonalPeriod;
					expanded[lag - 1] -= nonSeasonal[i - 1] * seasonal[j - 1];
				}
			}
			return expanded;
		}

		private static double ConditionalSumOfSquares(double[] centred, double[] expandedAr, double[] expandedMa) {
			double[] residualsUnused;
			double sse;
			ComputeResiduals(centred, expandedAr, expandedMa, out residualsUnused, out sse);
			return sse;
		}

		private static void ComputeResiduals(double[] centred, double[] expandedAr, double[] expandedMa, out double[] residuals, out double sumOfSquares) {
			int n = centred.Length;
			residuals = new double[n];
			int maxLag = Math.Max(expandedAr.Length, expandedMa.Length);
			double sse = 0;
			for (int t = 0; t < n; t++) {
				double pred = 0;
				for (int i = 0; i < expandedAr.Length; i++) {
					if (expandedAr[i] == 0) continue;
					int idx = t - 1 - i;
					if (idx < 0) break;
					pred += expandedAr[i] * centred[idx];
				}
				for (int j = 0; j < expandedMa.Length; j++) {
					if (expandedMa[j] == 0) continue;
					int idx = t - 1 - j;
					if (idx < 0) break;
					pred += expandedMa[j] * residuals[idx];
				}
				residuals[t] = centred[t] - pred;
				if (t >= maxLag) sse += residuals[t] * residuals[t];
			}
			sumOfSquares = sse;
		}

		/// <summary>Closed-form Yule-Walker AR seed.</summary>
		private static double[] YuleWalker(double[] centred, int p) {
			int n = centred.Length;
			double[] gamma = new double[p + 1];
			for (int k = 0; k <= p; k++) {
				double sum = 0;
				for (int t = k; t < n; t++) sum += centred[t] * centred[t - k];
				gamma[k] = sum / n;
			}
			Matrix<double> R = Matrix<double>.Build.Dense(p, p, (i, j) => gamma[Math.Abs(i - j)]);
			Vector<double> b = Vector<double>.Build.Dense(p, i => gamma[i + 1]);
			try {
				Vector<double> solution = R.Solve(b);
				double[] arr = new double[p];
				for (int i = 0; i < p; i++) arr[i] = solution[i];
				return arr;
			} catch {
				return new double[p];
			}
		}

		/// <summary>First <paramref name="length"/> coefficients of the MA(∞) representation of
		/// the expanded SARIMA polynomials.</summary>
		private static double[] MovingAverageRepresentation(double[] expandedAr, double[] expandedMa, int length) {
			double[] psi = new double[length];
			psi[0] = 1.0;
			for (int j = 1; j < length; j++) {
				double value = 0;
				int upper = Math.Min(j, expandedAr.Length);
				for (int i = 1; i <= upper; i++) {
					value += expandedAr[i - 1] * psi[j - i];
				}
				if (j - 1 < expandedMa.Length) {
					value += expandedMa[j - 1];
				}
				psi[j] = value;
			}
			return psi;
		}

		/// <summary>Inverse standard-normal CDF via Acklam's rational approximation.</summary>
		private static double NormalQuantile(double p) {
			if (p <= 0 || p >= 1) throw new ArgumentOutOfRangeException("p");

			const double a1 = -39.69683028665376;
			const double a2 = 220.9460984245205;
			const double a3 = -275.9285104469687;
			const double a4 = 138.3577518672690;
			const double a5 = -30.66479806614716;
			const double a6 = 2.506628277459239;
			const double b1 = -54.47609879822406;
			const double b2 = 161.5858368580409;
			const double b3 = -155.6989798598866;
			const double b4 = 66.80131188771972;
			const double b5 = -13.28068155288572;
			const double c1 = -0.007784894002430293;
			const double c2 = -0.3223964580411365;
			const double c3 = -2.400758277161838;
			const double c4 = -2.549732539343734;
			const double c5 = 4.374664141464968;
			const double c6 = 2.938163982698783;
			const double d1 = 0.007784695709041462;
			const double d2 = 0.3224671290700398;
			const double d3 = 2.445134137142996;
			const double d4 = 3.754408661907416;
			const double pLow = 0.02425;
			const double pHigh = 1 - pLow;

			if (p < pLow) {
				double qLow = Math.Sqrt(-2 * Math.Log(p));
				return (((((c1 * qLow + c2) * qLow + c3) * qLow + c4) * qLow + c5) * qLow + c6) /
					   ((((d1 * qLow + d2) * qLow + d3) * qLow + d4) * qLow + 1);
			}
			if (p <= pHigh) {
				double qMid = p - 0.5;
				double rMid = qMid * qMid;
				return (((((a1 * rMid + a2) * rMid + a3) * rMid + a4) * rMid + a5) * rMid + a6) * qMid /
					   (((((b1 * rMid + b2) * rMid + b3) * rMid + b4) * rMid + b5) * rMid + 1);
			}
			{
				double qHigh = Math.Sqrt(-2 * Math.Log(1 - p));
				return -(((((c1 * qHigh + c2) * qHigh + c3) * qHigh + c4) * qHigh + c5) * qHigh + c6) /
						((((d1 * qHigh + d2) * qHigh + d3) * qHigh + d4) * qHigh + 1);
			}
		}
	}

}
