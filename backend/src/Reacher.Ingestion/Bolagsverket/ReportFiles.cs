using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// The xhtml files in a downloaded document, unpacked in memory and never written to disk.
/// Ported from reacher's <c>report_files</c>.
/// </summary>
internal static partial class ReportFiles
{
    /// <summary>An annual report is a few hundred kB. Anything larger is not a report.</summary>
    public const int MaxReportBytes = 50_000_000;

    private static readonly string[] ReportExtensions = [".xhtml", ".html", ".htm"];

    /// <summary>
    /// One stream per xhtml file in the zip. A payload that is not a zip is returned as it
    /// is; the parser says if it is not iXBRL either.
    /// </summary>
    /// <remarks>
    /// File names inside the zip are never logged: they can contain the orgnr.
    /// </remarks>
    public static IReadOnlyList<Stream> Extract(
        byte[] payload,
        string documentId,
        ILogger logger,
        int maxBytes = MaxReportBytes)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(new MemoryStream(payload, writable: false));
        }
        catch (InvalidDataException)
        {
            return [new MemoryStream(payload, writable: false)];
        }

        var files = new List<Stream>();
        using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                if (!ReportExtensions.Any(extension =>
                        entry.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // entry.Length is the unpacked size from the zip header. It stops a zip
                // bomb before unpacking, and ReadAtMost stops one that lies in the header.
                var bytes = entry.Length <= maxBytes ? ReadAtMost(entry, maxBytes) : null;
                if (bytes is null)
                {
                    LogTooLarge(logger, documentId, maxBytes);
                    continue;
                }

                files.Add(new MemoryStream(bytes, writable: false));
            }
        }

        if (files.Count == 0)
        {
            LogNoXhtml(logger, documentId);
        }

        return files;
    }

    /// <summary>The unpacked file, or null if it is longer than <paramref name="limit"/>.</summary>
    private static byte[]? ReadAtMost(ZipArchiveEntry entry, int limit)
    {
        using var source = entry.Open();
        using var copy = new MemoryStream();
        var buffer = new byte[81_920];
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            if (copy.Length + read > limit)
            {
                return null;
            }

            copy.Write(buffer, 0, read);
        }

        return copy.ToArray();
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Document {DocumentId}: a file is larger than {MaxBytes} bytes and is skipped.")]
    private static partial void LogTooLarge(ILogger logger, string documentId, int maxBytes);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Document {DocumentId}: the zip has no xhtml file.")]
    private static partial void LogNoXhtml(ILogger logger, string documentId);
}
