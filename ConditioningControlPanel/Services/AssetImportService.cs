using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles importing assets from ZIP files, folders, and individual files.
    /// Automatically routes files to correct folders based on file type.
    /// </summary>
    public class AssetImportService
    {
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpeg", ".mpg", ".3gp"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
        };

        /// <summary>
        /// Import assets from the given paths (files, folders, or ZIPs).
        /// </summary>
        public async Task<ImportResult> ImportAsync(string[] paths, IProgress<ImportProgress>? progress = null)
        {
            var result = new ImportResult();

            // Ensure destination folders exist
            var imagesFolder = Path.Combine(App.EffectiveAssetsPath, "images");
            var videosFolder = Path.Combine(App.EffectiveAssetsPath, "videos");
            Directory.CreateDirectory(imagesFolder);
            Directory.CreateDirectory(videosFolder);

            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        await ImportFolderAsync(path, result, progress);
                    }
                    else if (File.Exists(path))
                    {
                        var ext = Path.GetExtension(path);
                        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            await ImportZipAsync(path, result, progress);
                        }
                        else
                        {
                            ImportFile(path, result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to import: {Path}", path);
                    result.Errors.Add($"Failed to import {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            App.Logger?.Information("Asset import complete: {Images} images, {Videos} videos, {Skipped} skipped",
                result.ImagesImported, result.VideosImported, result.Skipped);

            return result;
        }

        /// <summary>
        /// Import a single file to the appropriate folder.
        /// </summary>
        private void ImportFile(string filePath, ImportResult result)
        {
            var ext = Path.GetExtension(filePath);
            var destFolder = GetDestinationFolder(ext);

            if (destFolder == null)
            {
                result.Skipped++;
                return;
            }

            var fileName = Path.GetFileName(filePath);
            var destPath = GetUniqueDestPath(destFolder, fileName);

            try
            {
                File.Copy(filePath, destPath, overwrite: false);

                if (IsVideo(ext))
                {
                    result.VideosImported++;
                    App.Logger?.Debug("Imported video: {File}", fileName);
                }
                else if (IsImage(ext))
                {
                    result.ImagesImported++;
                    App.Logger?.Debug("Imported image: {File}", fileName);
                }
            }
            catch (IOException) when (File.Exists(destPath))
            {
                // File already exists
                result.Skipped++;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to copy file: {File}", fileName);
                result.Errors.Add($"Failed to copy {fileName}");
            }
        }

        /// <summary>
        /// Import all supported files from a folder (recursively), preserving subfolder structure.
        /// </summary>
        private async Task ImportFolderAsync(string folderPath, ImportResult result, IProgress<ImportProgress>? progress)
        {
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => IsSupportedFile(Path.GetExtension(f)))
                .ToList();

            var total = files.Count;
            var current = 0;

            foreach (var file in files)
            {
                // Calculate relative path from source folder to preserve structure
                var relativePath = Path.GetRelativePath(folderPath, file);
                var relativeDir = Path.GetDirectoryName(relativePath) ?? "";

                ImportFileWithSubfolder(file, relativeDir, result);
                current++;
                progress?.Report(new ImportProgress(current, total, Path.GetFileName(file)));

                // Yield to UI thread periodically
                if (current % 10 == 0)
                {
                    await Task.Delay(1);
                }
            }
        }

        /// <summary>
        /// Import a file preserving its subfolder structure.
        /// </summary>
        private void ImportFileWithSubfolder(string filePath, string relativeSubfolder, ImportResult result)
        {
            var ext = Path.GetExtension(filePath);
            var baseDestFolder = GetDestinationFolder(ext);

            if (baseDestFolder == null)
            {
                result.Skipped++;
                return;
            }

            // Preserve subfolder structure
            var destFolder = string.IsNullOrEmpty(relativeSubfolder)
                ? baseDestFolder
                : Path.Combine(baseDestFolder, relativeSubfolder);

            Directory.CreateDirectory(destFolder);

            var fileName = Path.GetFileName(filePath);
            var destPath = GetUniqueDestPath(destFolder, fileName);

            try
            {
                File.Copy(filePath, destPath, overwrite: false);

                if (IsVideo(ext))
                {
                    result.VideosImported++;
                    App.Logger?.Debug("Imported video: {SubFolder}/{File}", relativeSubfolder, fileName);
                }
                else if (IsImage(ext))
                {
                    result.ImagesImported++;
                    App.Logger?.Debug("Imported image: {SubFolder}/{File}", relativeSubfolder, fileName);
                }
            }
            catch (IOException) when (File.Exists(destPath))
            {
                result.Skipped++;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to copy file: {File}", fileName);
                result.Errors.Add($"Failed to copy {fileName}");
            }
        }

        /// <summary>
        /// Import assets from a ZIP file, preserving subfolder structure.
        /// </summary>
        private async Task ImportZipAsync(string zipPath, ImportResult result, IProgress<ImportProgress>? progress)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var entries = archive.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name) && IsSupportedFile(Path.GetExtension(e.Name)))
                    .ToList();

                var total = entries.Count;
                var current = 0;

                foreach (var entry in entries)
                {
                    try
                    {
                        var ext = Path.GetExtension(entry.Name);
                        var baseDestFolder = GetDestinationFolder(ext);

                        if (baseDestFolder != null)
                        {
                            // Preserve subfolder structure from ZIP
                            var entryDir = Path.GetDirectoryName(entry.FullName) ?? "";
                            var destFolder = string.IsNullOrEmpty(entryDir)
                                ? baseDestFolder
                                : Path.Combine(baseDestFolder, entryDir);

                            Directory.CreateDirectory(destFolder);

                            var fileName = Path.GetFileName(entry.Name);
                            var destPath = GetUniqueDestPath(destFolder, fileName);

                            // Extract to destination
                            entry.ExtractToFile(destPath, overwrite: false);

                            if (IsVideo(ext))
                            {
                                result.VideosImported++;
                                App.Logger?.Debug("Extracted video from ZIP: {File}", fileName);
                            }
                            else if (IsImage(ext))
                            {
                                result.ImagesImported++;
                                App.Logger?.Debug("Extracted image from ZIP: {File}", fileName);
                            }
                        }
                    }
                    catch (IOException)
                    {
                        // File already exists
                        result.Skipped++;
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to extract: {Entry}", entry.Name);
                        result.Skipped++;
                    }

                    current++;
                    progress?.Report(new ImportProgress(current, total, entry.Name));

                    // Yield to UI thread periodically
                    if (current % 10 == 0)
                    {
                        await Task.Delay(1);
                    }
                }
            }
            catch (InvalidDataException)
            {
                result.Errors.Add($"Invalid or corrupted ZIP file: {Path.GetFileName(zipPath)}");
                App.Logger?.Warning("Invalid ZIP file: {Path}", zipPath);
            }
        }

        /// <summary>
        /// Gets a unique destination path, adding a number suffix if file exists.
        /// </summary>
        private static string GetUniqueDestPath(string folder, string fileName)
        {
            var destPath = Path.Combine(folder, fileName);

            if (!File.Exists(destPath))
                return destPath;

            // File exists, generate unique name
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var counter = 1;

            while (File.Exists(destPath))
            {
                destPath = Path.Combine(folder, $"{nameWithoutExt}_{counter}{ext}");
                counter++;
            }

            return destPath;
        }

        private static string? GetDestinationFolder(string extension)
        {
            if (IsVideo(extension))
                return Path.Combine(App.EffectiveAssetsPath, "videos");
            if (IsImage(extension))
                return Path.Combine(App.EffectiveAssetsPath, "images");
            return null;
        }

        private static bool IsVideo(string extension) => VideoExtensions.Contains(extension);
        private static bool IsImage(string extension) => ImageExtensions.Contains(extension);
        private static bool IsSupportedFile(string extension) => IsVideo(extension) || IsImage(extension);
    }

    /// <summary>
    /// Result of an import operation.
    /// </summary>
    public class ImportResult
    {
        public int ImagesImported { get; set; }
        public int VideosImported { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; } = new();

        public int TotalImported => ImagesImported + VideosImported;
        public bool HasErrors => Errors.Count > 0;

        public string GetSummary()
        {
            var parts = new List<string>();

            if (ImagesImported > 0)
                parts.Add($"{ImagesImported} image{(ImagesImported != 1 ? "s" : "")}");
            if (VideosImported > 0)
                parts.Add($"{VideosImported} video{(VideosImported != 1 ? "s" : "")}");

            if (parts.Count == 0)
                return Skipped > 0 ? $"No new files imported ({Skipped} already existed)" : "No supported files found";

            var summary = $"Imported {string.Join(" and ", parts)}";
            if (Skipped > 0)
                summary += $" ({Skipped} skipped)";

            return summary;
        }
    }

    /// <summary>
    /// Progress information for import operations.
    /// </summary>
    public class ImportProgress
    {
        public int Current { get; }
        public int Total { get; }
        public string CurrentFile { get; }
        public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;

        public ImportProgress(int current, int total, string currentFile)
        {
            Current = current;
            Total = total;
            CurrentFile = currentFile;
        }
    }
}
