// Copyright (c) Tunnel Vision Laboratories, LLC. All Rights Reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace StyleCop.Analyzers
{
    using System;
    using System.Collections.Immutable;
    using System.IO;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Text;
    using Newtonsoft.Json;
    using StyleCop.Analyzers.Settings.ObjectModel;

    /// <summary>
    /// Class that manages the settings files for StyleCopAnalyzers.
    /// </summary>
    internal static class SettingsHelper
    {
        internal const string SettingsFileName = "stylecop.json";

        private static readonly SourceTextValueProvider<StyleCopSettings> SettingsValueProvider =
            new SourceTextValueProvider<StyleCopSettings>(
                text => GetStyleCopSettings(text, DeserializationFailureBehavior.ReturnDefaultSettings));

        private static System.Collections.Generic.Dictionary<string, StyleCopSettings> settingsByFileName = new System.Collections.Generic.Dictionary<string, StyleCopSettings>();

        /// <summary>
        /// Returns the folder where the settings file reside
        /// </summary>
        /// <param name="options">The analyzer options that will be used to determine the StyleCop settings.</param>
        /// <returns>Empty string if no settings file is available.</returns>
        internal static string GetSettingsFolder(this AnalyzerOptions options)
        {
            foreach (var additionalFile in options.AdditionalFiles)
            {
                if (Path.GetFileName(additionalFile.Path).ToLowerInvariant() == SettingsFileName)
                {
                    return Path.GetDirectoryName(additionalFile.Path);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets the StyleCop settings.
        /// </summary>
        /// <remarks>
        /// <para>If a <see cref="JsonException"/> occurs while deserializing the settings file, a default settings
        /// instance is returned.</para>
        /// </remarks>
        /// <param name="context">The context that will be used to determine the StyleCop settings.</param>
        /// <param name="cancellationToken">The cancellation token that the operation will observe.</param>
        /// <returns>A <see cref="StyleCopSettings"/> instance that represents the StyleCop settings for the given context.</returns>
        internal static StyleCopSettings GetStyleCopSettings(this SyntaxTreeAnalysisContext context, CancellationToken cancellationToken)
        {
            return context.Options.GetStyleCopSettings(cancellationToken);
        }

        /// <summary>
        /// Gets the StyleCop settings.
        /// </summary>
        /// <remarks>
        /// <para>If a <see cref="JsonException"/> occurs while deserializing the settings file, a default settings
        /// instance is returned.</para>
        /// </remarks>
        /// <param name="options">The analyzer options that will be used to determine the StyleCop settings.</param>
        /// <param name="cancellationToken">The cancellation token that the operation will observe.</param>
        /// <returns>A <see cref="StyleCopSettings"/> instance that represents the StyleCop settings for the given context.</returns>
        internal static StyleCopSettings GetStyleCopSettings(this AnalyzerOptions options, CancellationToken cancellationToken)
        {
            return GetStyleCopSettings(options, DeserializationFailureBehavior.ReturnDefaultSettings, cancellationToken);
        }

        /// <summary>
        /// Gets the StyleCop settings.
        /// </summary>
        /// <param name="options">The analyzer options that will be used to determine the StyleCop settings.</param>
        /// <param name="failureBehavior">The behavior of the method when a <see cref="JsonException"/> occurs while
        /// deserializing the settings file.</param>
        /// <param name="cancellationToken">The cancellation token that the operation will observe.</param>
        /// <returns>A <see cref="StyleCopSettings"/> instance that represents the StyleCop settings for the given context.</returns>
        internal static StyleCopSettings GetStyleCopSettings(this AnalyzerOptions options, DeserializationFailureBehavior failureBehavior, CancellationToken cancellationToken)
        {
            return GetStyleCopSettings(options != null ? options.AdditionalFiles : ImmutableArray.Create<AdditionalText>(), failureBehavior, cancellationToken);
        }

        internal static StyleCopSettings GetStyleCopSettings(this AnalysisContext context, AnalyzerOptions options, CancellationToken cancellationToken)
        {
            return GetStyleCopSettings(context, options, DeserializationFailureBehavior.ReturnDefaultSettings, cancellationToken);
        }

        internal static StyleCopSettings GetStyleCopSettings(this AnalysisContext context, AnalyzerOptions options, DeserializationFailureBehavior failureBehavior, CancellationToken cancellationToken)
        {
            return GetCachedStyleCopSettings(context, options, failureBehavior, cancellationToken);
        }

        internal static StyleCopSettings GetStyleCopSettings(this CompilationStartAnalysisContext context, AnalyzerOptions options, CancellationToken cancellationToken)
        {
            return GetStyleCopSettings(context, options, DeserializationFailureBehavior.ReturnDefaultSettings, cancellationToken);
        }

#pragma warning disable RS1012 // Start action has no registered actions.
        internal static StyleCopSettings GetStyleCopSettings(this CompilationStartAnalysisContext context, AnalyzerOptions options, DeserializationFailureBehavior failureBehavior, CancellationToken cancellationToken)
#pragma warning restore RS1012 // Start action has no registered actions.
        {
            return GetCachedStyleCopSettings(context, options, failureBehavior, cancellationToken);
        }

        private static StyleCopSettings GetStyleCopSettings(SourceText text, DeserializationFailureBehavior failureBehavior)
        {
            try
            {
                var root = JsonConvert.DeserializeObject<SettingsFile>(text.ToString());

                if (root == null)
                {
                    throw new JsonException($"Settings file was missing or empty.");
                }

                return root.Settings;
            }
            catch (JsonException) when (failureBehavior == DeserializationFailureBehavior.ReturnDefaultSettings)
            {
                // The settings file is invalid -> return the default settings.
            }

            return new StyleCopSettings();
        }

        private static StyleCopSettings GetCachedStyleCopSettings(AnalysisContext context, AnalyzerOptions options, DeserializationFailureBehavior failureBehavior, CancellationToken cancellationToken)
        {
            return GetCachedStyleCopSettings(
                options,
                failureBehavior,
                cancellationToken,
                (sourceText, sourceTextValueProvider) =>
                {
                    StyleCopSettings settings = null;
                    context.TryGetValue(sourceText, sourceTextValueProvider, out settings);
                    return settings;
                });
        }

#pragma warning disable RS1012 // Start action has no registered actions
        private static StyleCopSettings GetCachedStyleCopSettings(CompilationStartAnalysisContext context, AnalyzerOptions options, DeserializationFailureBehavior failureBehavior, CancellationToken cancellationToken)
#pragma warning restore RS1012 // Start action has no registered actions
        {
            return GetCachedStyleCopSettings(
                options,
                failureBehavior,
                cancellationToken,
                (sourceText, sourceTextValueProvider) =>
                {
                    StyleCopSettings settings = null;
                    context.TryGetValue(sourceText, sourceTextValueProvider, out settings);
                    return settings;
                });
        }

        private static StyleCopSettings GetCachedStyleCopSettings(AnalyzerOptions options, DeserializationFailureBehavior failureBehavior, CancellationToken cancellationToken, Func<SourceText, SourceTextValueProvider<StyleCopSettings>, StyleCopSettings> contextSettingsProvider)
        {
            return GetCachedStyleCopSettings(options.AdditionalFiles, failureBehavior, cancellationToken, contextSettingsProvider);
        }

        private static StyleCopSettings GetCachedStyleCopSettings(ImmutableArray<AdditionalText> additionalFiles, DeserializationFailureBehavior failureBehavior, CancellationToken cancellationToken, Func<SourceText, SourceTextValueProvider<StyleCopSettings>, StyleCopSettings> contextSettingsProvider)
        {
            foreach (var additionalFile in additionalFiles)
            {
                if (Path.GetFileName(additionalFile.Path).ToLowerInvariant() == SettingsFileName)
                {
                    lock (settingsByFileName)
                    {
                        if (!settingsByFileName.ContainsKey(additionalFile.Path))
                        {
                            CreateAndAddStyleCopSettingsToCache(additionalFile, failureBehavior, cancellationToken, contextSettingsProvider);
                        }

                        return settingsByFileName[additionalFile.Path];
                    }
                }
            }

            return null;
        }

        private static void CreateAndAddStyleCopSettingsToCache(AdditionalText additionalFile, DeserializationFailureBehavior failureBehavior, CancellationToken cancellationToken, Func<SourceText, SourceTextValueProvider<StyleCopSettings>, StyleCopSettings> contextSettingsProvider)
        {
            SourceText text = GetStyleCopSettingsText(additionalFile, cancellationToken);
            if (text == null)
            {
                lock (settingsByFileName)
                {
                    settingsByFileName.Add(additionalFile.Path, new StyleCopSettings());
                }

                return;
            }

            if (failureBehavior == DeserializationFailureBehavior.ReturnDefaultSettings)
            {
                StyleCopSettings settings = contextSettingsProvider(text, SettingsValueProvider);
                if (settings == null)
                {
                    lock (settingsByFileName)
                    {
                        settingsByFileName.Add(additionalFile.Path, new StyleCopSettings());
                    }

                    return;
                }

                lock (settingsByFileName)
                {
                    settingsByFileName.Add(additionalFile.Path, settings);
                }

                return;
            }

            lock (settingsByFileName)
            {
                settingsByFileName.Add(additionalFile.Path, JsonConvert.DeserializeObject<SettingsFile>(text.ToString()).Settings);
            }

            return;
        }

        private static SourceText TryGetStyleCopSettingsText(this AnalyzerOptions options, CancellationToken cancellationToken)
        {
            foreach (var additionalFile in options.AdditionalFiles)
            {
                if (Path.GetFileName(additionalFile.Path).ToLowerInvariant() == SettingsFileName)
                {
                    return GetStyleCopSettingsText(additionalFile, cancellationToken);
                }
            }

            return null;
        }

        private static SourceText GetStyleCopSettingsText(AdditionalText additionalFile, CancellationToken cancellationToken)
        {
            return additionalFile.GetText(cancellationToken);
        }

        private static StyleCopSettings GetStyleCopSettings(ImmutableArray<AdditionalText> additionalFiles, DeserializationFailureBehavior failureBehavior, CancellationToken cancellationToken)
        {
            return GetCachedStyleCopSettings(
                additionalFiles,
                failureBehavior,
                cancellationToken,
                (sourceText, sourceTextValueProvider) =>
                {
                    SourceText additionalTextContent = sourceText;
                    var root = JsonConvert.DeserializeObject<SettingsFile>(additionalTextContent.ToString());

                    if (root == null)
                    {
                        throw new JsonException($"Settings file at 'UNKNOWN' was missing or empty.");
                    }

                    return root.Settings;
                });
        }
    }
}
