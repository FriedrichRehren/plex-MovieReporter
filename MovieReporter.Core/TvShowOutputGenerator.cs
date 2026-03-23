using MovieReporter.Core.Models;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;

namespace MovieReporter.Core;

public static class TvShowOutputGenerator
{
    private static readonly Resolution[] ResolutionColumns = Enum
        .GetValues<Resolution>()
        .OrderBy(resolution => (int)resolution)
        .ToArray();

    public static string Export(IEnumerable<TvShow> tvShows, string outputPath, Format format)
    {
        ArgumentNullException.ThrowIfNull(tvShows);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var normalizedTvShows = NormalizeTvShows(tvShows);
        return ExportNormalizedTvShows(normalizedTvShows, outputPath, format, DateTimeOffset.UtcNow);
    }

    public static string Export(IEnumerable<TvShow> tvShows, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return Export(tvShows, outputPath, ParseFormat(Path.GetExtension(outputPath)));
    }

    public static Task<string> ExportAsync(
        IEnumerable<TvShow> tvShows,
        string outputPath,
        Format format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tvShows);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var normalizedTvShows = NormalizeTvShows(tvShows);
        var exportedAtUtc = DateTimeOffset.UtcNow;

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ExportNormalizedTvShows(normalizedTvShows, outputPath, format, exportedAtUtc);
        }, cancellationToken);
    }

    public static Task<string> ExportAsync(
        IEnumerable<TvShow> tvShows,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return ExportAsync(tvShows, outputPath, ParseFormat(Path.GetExtension(outputPath)), cancellationToken);
    }

    public static async Task<IReadOnlyCollection<ExportResult>> ExportManyAsync(
        IEnumerable<TvShow> tvShows,
        string outputPathBase,
        IEnumerable<Format> formats,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tvShows);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPathBase);
        ArgumentNullException.ThrowIfNull(formats);

        var requestedFormats = formats
            .Distinct()
            .ToArray();

        if (requestedFormats.Length == 0)
        {
            return Array.Empty<ExportResult>();
        }

        var normalizedTvShows = NormalizeTvShows(tvShows);
        var exportedAtUtc = DateTimeOffset.UtcNow;
        var normalizedOutputPathBase = NormalizeOutputPathBase(outputPathBase);

        var exportTasks = requestedFormats
            .Select(format => Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputPath = normalizedOutputPathBase + GetDefaultExtension(format);
                var resolvedOutputPath = ExportNormalizedTvShows(normalizedTvShows, outputPath, format, exportedAtUtc);
                return new ExportResult(format, resolvedOutputPath);
            }, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(exportTasks);

        return results
            .OrderBy(result => result.Format)
            .ToArray();
    }

    public static string GetDefaultExtension(Format format)
    {
        return format switch
        {
            Format.TXT => ".txt",
            Format.CSV => ".csv",
            Format.JSON => ".json",
            Format.XLSX => ".xlsx",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
        };
    }

    private static TvShow[] NormalizeTvShows(IEnumerable<TvShow> tvShows)
    {
        return tvShows
            .Select(tvShow => new TvShow
            {
                Name = tvShow.Name,
                Year = tvShow.Year,
                ImdbId = tvShow.ImdbId,
                TmdbId = tvShow.TmdbId,
                SeasonCount = tvShow.SeasonCount,
                EpisodeCount = tvShow.EpisodeCount,
                Resolutions = tvShow.Resolutions
                    .Distinct()
                    .OrderBy(resolution => (int)resolution)
                    .ToArray()
            })
            .OrderBy(tvShow => tvShow.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tvShow => tvShow.Year ?? int.MaxValue)
            .ToArray();
    }

    private static string ExportNormalizedTvShows(
        IReadOnlyCollection<TvShow> normalizedTvShows,
        string outputPath,
        Format format,
        DateTimeOffset exportedAtUtc)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath, format);

        switch (format)
        {
            case Format.TXT:
                File.WriteAllText(resolvedOutputPath, BuildTxt(normalizedTvShows, exportedAtUtc), Encoding.UTF8);
                break;
            case Format.CSV:
                File.WriteAllText(resolvedOutputPath, BuildCsv(normalizedTvShows), Encoding.UTF8);
                break;
            case Format.JSON:
                File.WriteAllBytes(resolvedOutputPath, BuildJson(normalizedTvShows, exportedAtUtc));
                break;
            case Format.XLSX:
                BuildXlsx(normalizedTvShows, resolvedOutputPath);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.");
        }

        return resolvedOutputPath;
    }

    private static string BuildTxt(IReadOnlyCollection<TvShow> tvShows, DateTimeOffset exportedAtUtc)
    {
        var rows = tvShows
            .Select(BuildTextExportRow)
            .ToArray();

        var headers = GetTabularHeaders();
        var columnWidths = headers
            .Select((header, index) => Math.Max(
                header.Length,
                rows.Length == 0 ? 0 : rows.Max(row => row[index].Length)))
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("TV Show Report");
        builder.AppendLine($"Exported At (UTC): {exportedAtUtc:O}");
        builder.AppendLine($"Show Count: {tvShows.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine();
        builder.AppendLine(BuildTableBorder(columnWidths));
        builder.AppendLine(BuildTableRow(headers, columnWidths));
        builder.AppendLine(BuildTableBorder(columnWidths));

        foreach (var row in rows)
        {
            builder.AppendLine(BuildTableRow(row, columnWidths));
        }

        builder.AppendLine(BuildTableBorder(columnWidths));

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string BuildCsv(IReadOnlyCollection<TvShow> tvShows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", GetTabularHeaders().Select(EscapeCsv)));

        foreach (var tvShow in tvShows)
        {
            builder.AppendLine(string.Join(",", BuildBooleanResolutionRow(tvShow).Select(EscapeCsv)));
        }

        return builder.ToString();
    }

    private static byte[] BuildJson(IReadOnlyCollection<TvShow> tvShows, DateTimeOffset exportedAtUtc)
    {
        var exportModel = new
        {
            exportedAtUtc,
            showCount = tvShows.Count,
            tvShows = tvShows.Select(tvShow => new
            {
                name = tvShow.Name,
                year = tvShow.Year,
                imdbId = tvShow.ImdbId,
                tmdbId = tvShow.TmdbId,
                seasonCount = tvShow.SeasonCount,
                episodeCount = tvShow.EpisodeCount,
                resolutions = tvShow.Resolutions.Select(FormatResolution).ToArray()
            })
        };

        return JsonSerializer.SerializeToUtf8Bytes(exportModel, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static void BuildXlsx(IReadOnlyCollection<TvShow> tvShows, string outputPath)
    {
        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

        WriteZipEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
        WriteZipEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
        WriteZipEntry(archive, "xl/workbook.xml", BuildWorkbookXml());
        WriteZipEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
        WriteZipEntry(archive, "xl/styles.xml", BuildStylesXml());
        WriteZipEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(tvShows));
    }

    private static string BuildWorksheetXml(IReadOnlyCollection<TvShow> tvShows)
    {
        var headers = GetTabularHeaders();
        var rows = tvShows
            .Select(BuildBooleanResolutionRow)
            .ToArray();
        var columnWidths = BuildWorksheetColumnWidths(headers, rows);
        var builder = new StringBuilder();
        var lastRowNumber = rows.Length + 1;
        var lastColumnName = GetExcelColumnName(headers.Length);

        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        builder.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.AppendLine($"  <dimension ref=\"A1:{lastColumnName}{lastRowNumber.ToString(CultureInfo.InvariantCulture)}\"/>");
        builder.AppendLine("""  <sheetViews>""");
        builder.AppendLine("""    <sheetView workbookViewId="0">""");
        builder.AppendLine("""      <pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/>""");
        builder.AppendLine("""    </sheetView>""");
        builder.AppendLine("""  </sheetViews>""");
        builder.AppendLine("""  <sheetFormatPr defaultRowHeight="20"/>""");
        builder.AppendLine("""  <cols>""");

        for (var columnIndex = 0; columnIndex < columnWidths.Length; columnIndex++)
        {
            builder.AppendLine(
                $"    <col min=\"{columnIndex + 1}\" max=\"{columnIndex + 1}\" width=\"{columnWidths[columnIndex].ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        }

        builder.AppendLine("""  </cols>""");
        builder.AppendLine("""  <sheetData>""");
        builder.AppendLine("""    <row r="1">""");

        for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
        {
            builder.AppendLine(BuildInlineStringCell(
                $"{GetExcelColumnName(columnIndex + 1)}1",
                headers[columnIndex],
                6,
                1));
        }

        builder.AppendLine("""    </row>""");

        var rowNumber = 2;
        foreach (var row in rows)
        {
            builder.AppendLine($"    <row r=\"{rowNumber.ToString(CultureInfo.InvariantCulture)}\">");

            for (var columnIndex = 0; columnIndex < row.Length; columnIndex++)
            {
                var cellReference = $"{GetExcelColumnName(columnIndex + 1)}{rowNumber}";
                var cellValue = row[columnIndex];
                var styleIndex = columnIndex == 0 || columnIndex == 2 || columnIndex == 3 ? 2 : 3;
                var isNumericColumn = columnIndex is 1 or 4 or 5;

                builder.AppendLine(isNumericColumn && !string.IsNullOrWhiteSpace(cellValue)
                    ? BuildNumberCell(cellReference, cellValue, 6, styleIndex)
                    : BuildInlineStringCell(cellReference, cellValue, 6, styleIndex));
            }

            builder.AppendLine("""    </row>""");
            rowNumber++;
        }

        builder.AppendLine("""  </sheetData>""");
        builder.AppendLine($"  <autoFilter ref=\"A1:{lastColumnName}{lastRowNumber.ToString(CultureInfo.InvariantCulture)}\"/>");
        builder.AppendLine("""</worksheet>""");

        return builder.ToString();
    }

    private static string[] GetTabularHeaders()
    {
        return
        [
            "Name",
            "Year",
            "ImdbId",
            "TmdbId",
            "Seasons",
            "Episodes",
            .. ResolutionColumns.Select(FormatResolution)
        ];
    }

    private static string[] BuildTextExportRow(TvShow tvShow)
    {
        var availableResolutions = tvShow.Resolutions.ToHashSet();

        return
        [
            tvShow.Name,
            tvShow.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            tvShow.ImdbId ?? string.Empty,
            tvShow.TmdbId ?? string.Empty,
            tvShow.SeasonCount.ToString(CultureInfo.InvariantCulture),
            tvShow.EpisodeCount.ToString(CultureInfo.InvariantCulture),
            .. ResolutionColumns.Select(resolution => FormatResolutionCheckmark(availableResolutions.Contains(resolution)))
        ];
    }

    private static string[] BuildBooleanResolutionRow(TvShow tvShow)
    {
        var availableResolutions = tvShow.Resolutions.ToHashSet();

        return
        [
            tvShow.Name,
            tvShow.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            tvShow.ImdbId ?? string.Empty,
            tvShow.TmdbId ?? string.Empty,
            tvShow.SeasonCount.ToString(CultureInfo.InvariantCulture),
            tvShow.EpisodeCount.ToString(CultureInfo.InvariantCulture),
            .. ResolutionColumns.Select(resolution => FormatResolutionBooleanText(availableResolutions.Contains(resolution)))
        ];
    }

    private static string ResolveOutputPath(string outputPath, Format format)
    {
        var resolvedOutputPath = Path.GetFullPath(outputPath);

        if (string.IsNullOrWhiteSpace(Path.GetExtension(resolvedOutputPath)))
        {
            resolvedOutputPath += GetDefaultExtension(format);
        }

        var directoryPath = Path.GetDirectoryName(resolvedOutputPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        return resolvedOutputPath;
    }

    private static string NormalizeOutputPathBase(string outputPathBase)
    {
        var fullPath = Path.GetFullPath(outputPathBase);
        var extension = Path.GetExtension(fullPath);

        if (string.IsNullOrWhiteSpace(extension))
        {
            return fullPath;
        }

        return IsSupportedExtension(extension)
            ? Path.Combine(Path.GetDirectoryName(fullPath) ?? string.Empty, Path.GetFileNameWithoutExtension(fullPath))
            : fullPath;
    }

    private static bool IsSupportedExtension(string extension)
    {
        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
    }

    private static Format ParseFormat(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".txt" => Format.TXT,
            ".csv" => Format.CSV,
            ".json" => Format.JSON,
            ".xlsx" => Format.XLSX,
            _ => throw new ArgumentException($"Unsupported export file extension '{extension}'.", nameof(extension))
        };
    }

    private static string BuildContentTypesXml()
    {
        return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
""";
    }

    private static string BuildRootRelationshipsXml()
    {
        return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""";
    }

    private static string BuildWorkbookXml()
    {
        return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="TV Shows" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>
""";
    }

    private static string BuildWorkbookRelationshipsXml()
    {
        return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""";
    }

    private static string BuildStylesXml()
    {
        return """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="2">
    <font>
      <sz val="11"/>
      <color theme="1"/>
      <name val="Aptos"/>
      <family val="2"/>
    </font>
    <font>
      <b/>
      <sz val="11"/>
      <color rgb="FFFFFFFF"/>
      <name val="Aptos"/>
      <family val="2"/>
    </font>
  </fonts>
  <fills count="3">
    <fill>
      <patternFill patternType="none"/>
    </fill>
    <fill>
      <patternFill patternType="gray125"/>
    </fill>
    <fill>
      <patternFill patternType="solid">
        <fgColor rgb="FF173733"/>
        <bgColor indexed="64"/>
      </patternFill>
    </fill>
  </fills>
  <borders count="2">
    <border>
      <left/>
      <right/>
      <top/>
      <bottom/>
      <diagonal/>
    </border>
    <border>
      <left style="thin">
        <color rgb="FFD8C7B3"/>
      </left>
      <right style="thin">
        <color rgb="FFD8C7B3"/>
      </right>
      <top style="thin">
        <color rgb="FFD8C7B3"/>
      </top>
      <bottom style="thin">
        <color rgb="FFD8C7B3"/>
      </bottom>
      <diagonal/>
    </border>
  </borders>
  <cellStyleXfs count="1">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
  </cellStyleXfs>
  <cellXfs count="4">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1">
      <alignment horizontal="center" vertical="center"/>
    </xf>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1">
      <alignment horizontal="left" vertical="center"/>
    </xf>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1">
      <alignment horizontal="center" vertical="center"/>
    </xf>
  </cellXfs>
  <cellStyles count="1">
    <cellStyle name="Normal" xfId="0" builtinId="0"/>
  </cellStyles>
</styleSheet>
""";
    }

    private static string BuildInlineStringCell(string cellReference, string value, int indentSize, int? styleIndex = null)
    {
        var indent = new string(' ', indentSize);
        var styleAttribute = styleIndex.HasValue ? $" s=\"{styleIndex.Value.ToString(CultureInfo.InvariantCulture)}\"" : string.Empty;
        return $"{indent}<c r=\"{cellReference}\" t=\"inlineStr\"{styleAttribute}><is><t>{EscapeXml(value)}</t></is></c>";
    }

    private static string BuildNumberCell(string cellReference, string value, int indentSize, int? styleIndex = null)
    {
        var indent = new string(' ', indentSize);
        var styleAttribute = styleIndex.HasValue ? $" s=\"{styleIndex.Value.ToString(CultureInfo.InvariantCulture)}\"" : string.Empty;
        return $"{indent}<c r=\"{cellReference}\"{styleAttribute}><v>{EscapeXml(value)}</v></c>";
    }

    private static void WriteZipEntry(ZipArchive archive, string entryPath, string content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);

        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string BuildTableBorder(IReadOnlyList<int> columnWidths)
    {
        return "+" + string.Join("+", columnWidths.Select(width => new string('-', width + 2))) + "+";
    }

    private static string BuildTableRow(IReadOnlyList<string> values, IReadOnlyList<int> columnWidths)
    {
        var paddedValues = values
            .Select((value, index) => " " + value.PadRight(columnWidths[index], ' ') + " ");

        return "|" + string.Join("|", paddedValues) + "|";
    }

    private static string FormatResolution(Resolution resolution)
    {
        return $"{((int)resolution).ToString(CultureInfo.InvariantCulture)}p";
    }

    private static string FormatResolutionCheckmark(bool isPresent)
    {
        return isPresent ? "[x]" : "[ ]";
    }

    private static string FormatResolutionBooleanText(bool isPresent)
    {
        return isPresent ? "true" : string.Empty;
    }

    private static double[] BuildWorksheetColumnWidths(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        return headers
            .Select((header, index) =>
            {
                if (index >= 6)
                {
                    return 10d;
                }

                var maxValueLength = rows.Count == 0 ? 0 : rows.Max(row => row[index].Length);
                var maxLength = Math.Max(header.Length, maxValueLength);

                return index switch
                {
                    0 => Math.Min(Math.Max(maxLength + 4, 22), 42),
                    1 => 9d,
                    2 => Math.Min(Math.Max(maxLength + 4, 12), 18),
                    3 => Math.Min(Math.Max(maxLength + 4, 10), 14),
                    4 => 10d,
                    5 => 10d,
                    _ => Math.Min(Math.Max(maxLength + 3, 10), 16)
                };
            })
            .ToArray();
    }

    private static string GetExcelColumnName(int columnNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnNumber);

        var builder = new StringBuilder();
        var current = columnNumber;

        while (current > 0)
        {
            current--;
            builder.Insert(0, (char)('A' + (current % 26)));
            current /= 26;
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeXml(string value)
    {
        return SecurityElement.Escape(value) ?? string.Empty;
    }
}
