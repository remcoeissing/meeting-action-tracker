using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;

namespace MeetingActionTracker.Api;

public sealed class DocumentStore
{
    public const long MaxSize = 20 * 1024 * 1024;
    private readonly string directory;

    public DocumentStore(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
    }

    public async Task<AgendaDocument> Upload(IFormFile file, int agendaId, TrackerDb db)
    {
        Check.Require(file.Length > 0, "Choose a non-empty document.");
        Check.Require(file.Length <= MaxSize, "Documents must be 20 MB or smaller.", 413);
        var fileName = Path.GetFileName(file.FileName.Replace('\\', '/'));
        Check.Require(fileName.Length is > 0 and <= 240 && !fileName.Any(char.IsControl), "The document filename is invalid.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        Check.Require(buffer.Length <= MaxSize, "Documents must be 20 MB or smaller.", 413);
        var bytes = buffer.ToArray();
        var contentType = ValidateContent(bytes, extension);
        var storageName = $"{Guid.NewGuid():N}.blob";
        var path = Path.Combine(directory, storageName);
        var document = new AgendaDocument
        {
            AgendaItemId = agendaId, FileName = fileName, StorageName = storageName,
            ContentType = contentType, Size = bytes.LongLength, UploadedAt = DateTime.UtcNow
        };
        var saved = false;
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            saved = true;
        }
        finally { if (!saved && File.Exists(path)) File.Delete(path); }
        return document;
    }

    public IResult Download(AgendaDocument document)
    {
        var path = Path.Combine(directory, document.StorageName);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Results.File(stream, document.ContentType, document.FileName, enableRangeProcessing: false);
    }

    public async Task Delete(AgendaDocument document, TrackerDb db)
    {
        var path = Path.Combine(directory, document.StorageName);
        var staged = path + ".deleting";
        await using var transaction = await db.Database.BeginTransactionAsync();
        File.Move(path, staged);
        var committed = false;
        try
        {
            db.Documents.Remove(document);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            committed = true;
        }
        finally { if (!committed) File.Move(staged, path); }
        try { File.Delete(staged); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Restore the record and file if final deletion fails, allowing the user to retry safely.
            File.Move(staged, path);
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            throw;
        }
    }

    private static string ValidateContent(byte[] bytes, string extension)
    {
        if (extension == ".pdf")
        {
            Check.Require(bytes.AsSpan().StartsWith("%PDF-"u8), "This file is not a PDF document.", 415);
            return "application/pdf";
        }
        if (extension == ".txt")
        {
            string value;
            try { value = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { throw new ApiException(415, "Text files must contain valid UTF-8 text."); }
            Check.Require(!value.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'),
                "Text files cannot contain binary control characters.", 415);
            return "text/plain";
        }
        var (entryName, mime) = extension switch
        {
            ".docx" => ("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ".xlsx" => ("xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            ".pptx" => ("ppt/presentation.xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            _ => throw new ApiException(415, "Supported documents are PDF, DOCX, XLSX, PPTX, and UTF-8 TXT.")
        };
        try
        {
            using var stream = new MemoryStream(bytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var part = archive.GetEntry(entryName);
            var types = archive.GetEntry("[Content_Types].xml");
            Check.Require(part is { Length: > 0 } && types is { Length: > 0 and <= 1048576 },
                "The document does not contain the expected OpenXML content.", 415);
            using var xmlStream = types!.Open();
            using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576
            });
            var contentTypes = XDocument.Load(reader);
            var expected = extension switch
            {
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
                _ => "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"
            };
            Check.Require(contentTypes.Descendants().Any(element => element.Name.LocalName == "Override"
                && (string?)element.Attribute("PartName") == "/" + entryName
                && (string?)element.Attribute("ContentType") == expected),
                "The document content type does not match its extension.", 415);
            return mime;
        }
        catch (InvalidDataException) { throw new ApiException(415, "This file is not a valid OpenXML document."); }
        catch (XmlException) { throw new ApiException(415, "The document content types are invalid."); }
    }
}
