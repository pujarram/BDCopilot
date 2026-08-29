using System.IO.Compression;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace BDCopilot.Infrastructure.Services;

public class DocumentExportService : IDocumentExportService
{
    public Task<Stream> ExportDocxAsync(GeneratedDocument document, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body());

            AddDocxParagraph(mainPart, document.Title, bold: true, sizeHalfPoints: 32);

            foreach (var section in document.Sections.OrderBy(s => s.Order))
            {
                AddDocxParagraph(mainPart, section.Title, bold: true, sizeHalfPoints: 24);
                foreach (var paragraph in section.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    AddDocxParagraph(mainPart, paragraph.Trim());
                }
            }

            mainPart.Document.Save();
        }

        stream.Position = 0;
        return Task.FromResult<Stream>(stream);
    }

    public Task<Stream> ExportPptxAsync(GeneratedDocument document, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var stream = new MemoryStream();
        using (var presentationDoc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentationPart = presentationDoc.AddPresentationPart();
            presentationPart.Presentation = new Presentation(
                new SlideIdList(),
                new SlideSize { Cx = 9144000, Cy = 6858000 },
                new NotesSize { Cx = 6858000, Cy = 9148000 });

            var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
            slideMasterPart.SlideMaster = new SlideMaster(
                new CommonSlideData(new ShapeTree()),
                new P.ColorMap(),
                new SlideLayoutIdList());

            var themePart = slideMasterPart.AddNewPart<ThemePart>();
            themePart.Theme = new A.Theme { Name = "BD Copilot Theme" };

            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
            slideLayoutPart.SlideLayout = new SlideLayout(
                new CommonSlideData(new ShapeTree()),
                new P.ColorMapOverride(new A.MasterColorMapping()));

            slideMasterPart.SlideMaster.Append(
                new SlideLayoutIdList(
                    new SlideLayoutId
                    {
                        Id = 1U,
                        RelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart)
                    }));

            uint slideId = 256;
            foreach (var section in document.Sections.OrderBy(s => s.Order))
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = BuildSlide(section.Title, section.Content);
                slidePart.AddPart(slideLayoutPart);

                presentationPart.Presentation.SlideIdList!.AppendChild(
                    new SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
            }

            presentationPart.Presentation.Save();
        }

        stream.Position = 0;
        return Task.FromResult<Stream>(stream);
    }

    public async Task<Stream> ExportZipAsync(GeneratedDocument document, CancellationToken ct = default)
    {
        await using var docxStream = await ExportDocxAsync(document, ct);
        await using var pptxStream = await ExportPptxAsync(document, ct);

        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var safeBase = SanitizeFileName(document.Title);

            var docxEntry = archive.CreateEntry($"{safeBase}.docx");
            await using (var entryStream = docxEntry.Open())
            {
                await docxStream.CopyToAsync(entryStream, ct);
            }

            var pptxEntry = archive.CreateEntry($"{safeBase}.pptx");
            await using (var entryStream = pptxEntry.Open())
            {
                await pptxStream.CopyToAsync(entryStream, ct);
            }
        }

        zipStream.Position = 0;
        return zipStream;
    }

    private static void AddDocxParagraph(MainDocumentPart mainPart, string text, bool bold = false, int sizeHalfPoints = 22)
    {
        var body = mainPart.Document!.Body!;
        var runProps = new W.RunProperties();
        if (bold)
        {
            runProps.Append(new W.Bold());
        }

        runProps.Append(new W.FontSize { Val = sizeHalfPoints.ToString() });
        body.Append(new W.Paragraph(new W.Run(runProps, new W.Text(text))));
    }

    private static Slide BuildSlide(string title, string content)
    {
        var shapeTree = new ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup()),
            CreateTextShape(2U, "Title", title, 100000L, 100000L, 8000000L, 1000000L, 3200, bold: true),
            CreateTextShape(3U, "Body", content, 100000L, 1200000L, 8000000L, 5000000L, 1800));

        return new Slide(
            new CommonSlideData(shapeTree),
            new P.ColorMapOverride(new A.MasterColorMapping()));
    }

    private static P.Shape CreateTextShape(
        uint id, string name, string text,
        long x, long y, long cx, long cy, int fontHalfPoints, bool bold = false)
    {
        var runProps = new A.RunProperties { Language = "en-US", FontSize = fontHalfPoints, Dirty = false };
        if (bold)
        {
            runProps.Bold = true;
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = cx, Cy = cy })),
            new P.TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(runProps, new A.Text(text)))));
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
    }
}
