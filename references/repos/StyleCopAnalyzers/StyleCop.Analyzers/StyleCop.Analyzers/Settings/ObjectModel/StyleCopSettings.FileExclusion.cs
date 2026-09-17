// Copyright (c) Tunnel Vision Laboratories, LLC. All Rights Reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace StyleCop.Analyzers.Settings.ObjectModel
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    /// <summary>
    /// This partial is responsible to handle with excludedfiles
    /// </summary>
    internal partial class StyleCopSettings
    {
        private Dictionary<string, bool> excludedFilesPathResults = new Dictionary<string, bool>();

        internal bool IsExcludedFile(string filePath, string settingsFolder)
        {
            bool isExcluded;
            lock (this.excludedFilesPathResults)
            {
                // sometimes the filePath includes relative folders in the middle, so we need to normalize it as well
                var normalizedFilePath = NormalizePath(filePath);
                if (!this.excludedFilesPathResults.TryGetValue(normalizedFilePath, out isExcluded))
                {
                    var fileName = GetFileName(normalizedFilePath);
                    isExcluded = (this.ExcludedFiles != null &&
                        this.ExcludedFiles.Any(
                            file => fileName.Equals(GetFileName(file), StringComparison.OrdinalIgnoreCase) &&
                            normalizedFilePath.Equals(NormalizePath(Path.Combine(settingsFolder, file)), StringComparison.OrdinalIgnoreCase))) ||
                        (this.ExcludedFileFilters != null && this.ExcludedFileFilters.Any(
                            fileFilter => Regex.IsMatch(normalizedFilePath, fileFilter, RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)));
                    this.excludedFilesPathResults.Add(normalizedFilePath, isExcluded);
                }
            }

            return isExcluded;
        }

        private static string GetFileName(string filePath)
        {
            var lastBackSlashIndex = filePath.LastIndexOf("\\");

            return lastBackSlashIndex != -1 ?
                filePath.Substring(lastBackSlashIndex + 1, filePath.Length - lastBackSlashIndex - 1) :
                filePath;
        }

        private static string NormalizePath(string path)
        {
            return new Uri(path).LocalPath;
        }
    }
}
