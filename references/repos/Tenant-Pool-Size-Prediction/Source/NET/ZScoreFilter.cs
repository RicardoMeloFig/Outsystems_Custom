using System;
using System.Collections.Generic;

namespace OutSystems.NssODCTenantPoolSize {

	/// <summary>
	/// Z-score utilities used by both forecasting actions. The two consumers operate on opposite
	/// sides of the same statistic:
	/// <list type="bullet">
	/// <item><see cref="Filter"/> — used by the SSA action. SSA is a univariate technique with no
	///       way to absorb a "this row is unusual" signal, so the right treatment is to drop the
	///       outlier rows before fitting. Filter therefore returns the subset whose absolute
	///       z-score is at most <c>threshold</c>.</item>
	/// <item><see cref="ComputeFlags"/> — used by the SARIMAX action. SARIMAX can learn from a
	///       per-row flag, so the right treatment is to keep all rows and surface a parallel
	///       boolean array that marks the unusual ones. ComputeFlags therefore returns a
	///       <c>bool[]</c> of the same length as the input, with <c>true</c> on rows whose
	///       absolute z-score exceeds <c>threshold</c>.</item>
	/// </list>
	/// Both helpers share the same z-score arithmetic via <see cref="ComputeStatistics"/>, so the
	/// SSA-side and SARIMAX-side definitions of "outlier" cannot drift apart silently.
	/// <para>
	/// Three fallbacks match the canonical reference semantics: fewer than three values, a
	/// constant series (zero standard deviation), or fewer than two values surviving the filter
	/// all return the input unchanged from <see cref="Filter"/>, since dropping further would
	/// yield a degenerate series. <see cref="ComputeFlags"/> is more permissive — its degenerate
	/// outcome is simply "no rows flagged", which still gives SARIMAX a usable (albeit constant)
	/// exogenous regressor.
	/// </para>
	/// </summary>
	internal static class ZScoreFilter {

		/// <summary>
		/// Returns the subset of <paramref name="observations"/> whose absolute z-score is at most
		/// <paramref name="threshold"/>, in original order. Falls back to the input on edge cases.
		/// The <see cref="STPersonalEditionRequestStructure.ssRequestCount"/> field carries the value
		/// against which the z-score is computed.
		/// </summary>
		public static IList<STPersonalEditionRequestStructure> Filter(
			IList<STPersonalEditionRequestStructure> observations,
			double threshold) {
			if (observations == null) {
				throw new ArgumentNullException("observations");
			}
			if (observations.Count < 3) {
				return observations;
			}

			double mean, sigma;
			ComputeStatistics(observations, out mean, out sigma);
			if (sigma == 0) {
				return observations;
			}

			List<STPersonalEditionRequestStructure> kept = new List<STPersonalEditionRequestStructure>(observations.Count);
			for (int i = 0; i < observations.Count; i++) {
				double z = (observations[i].ssRequestCount - mean) / sigma;
				if (Math.Abs(z) <= threshold) {
					kept.Add(observations[i]);
				}
			}
			return kept.Count < 2 ? observations : (IList<STPersonalEditionRequestStructure>)kept;
		}

		/// <summary>
		/// Returns a parallel boolean array marking the rows of <paramref name="observations"/>
		/// whose absolute z-score strictly exceeds <paramref name="threshold"/>. The output is
		/// always the same length as the input, in the same order, so callers can use it directly
		/// as an exogenous regressor for SARIMAX.
		/// <para>
		/// The "strictly exceeds" predicate matches the symmetric inverse of <see cref="Filter"/>:
		/// rows that survive Filter (|z| ≤ threshold) are not flagged, and rows that Filter would
		/// drop (|z| &gt; threshold) are flagged. This keeps the two operations philosophically
		/// equivalent — the same threshold draws the same line on the histogram.
		/// </para>
		/// <para>
		/// Edge cases yield "no rows flagged" rather than throwing, since SARIMAX tolerates a
		/// constant exogenous regressor (β collapses to zero and the SARIMA structure carries the
		/// fit unaided). Specifically: fewer than three rows, a constant series (sigma = 0), or a
		/// non-positive threshold all return an all-false array.
		/// </para>
		/// </summary>
		public static bool[] ComputeFlags(
			IList<STPersonalEditionRequestStructure> observations,
			double threshold) {
			if (observations == null) {
				throw new ArgumentNullException("observations");
			}
			bool[] flags = new bool[observations.Count];
			if (observations.Count < 3 || threshold <= 0) {
				return flags;
			}

			double mean, sigma;
			ComputeStatistics(observations, out mean, out sigma);
			if (sigma == 0) {
				return flags;
			}

			for (int i = 0; i < observations.Count; i++) {
				double z = (observations[i].ssRequestCount - mean) / sigma;
				if (Math.Abs(z) > threshold) {
					flags[i] = true;
				}
			}
			return flags;
		}

		/// <summary>Population mean and standard deviation (ddof = 0) of
		/// <see cref="STPersonalEditionRequestStructure.ssRequestCount"/>. Population rather than
		/// sample variance matches the Python reference implementation that originally seeded this
		/// code path, and keeps SSA and SARIMAX in lockstep on the meaning of "z".</summary>
		private static void ComputeStatistics(
			IList<STPersonalEditionRequestStructure> observations,
			out double mean,
			out double sigma) {
			double sum = 0;
			for (int i = 0; i < observations.Count; i++) {
				sum += observations[i].ssRequestCount;
			}
			mean = sum / observations.Count;

			double sumSq = 0;
			for (int i = 0; i < observations.Count; i++) {
				double d = observations[i].ssRequestCount - mean;
				sumSq += d * d;
			}
			sigma = Math.Sqrt(sumSq / observations.Count);
		}
	}

}
