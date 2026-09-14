// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// A file in a multipart form. <c>ContentType</c> is deliberately separate from
/// the name: the checks in the application look at both the extension and the
/// declared type, and the client sets both, so a test has to be able to make
/// the two disagree.
/// </summary>
public sealed record UploadFile(string Field, string FileName, byte[] Content, string ContentType)
{
    /// <summary>The smallest thing that passes for a PDF; the content is never read.</summary>
    public static UploadFile Pdf(string field, string name = "doklad.pdf") =>
        new(field, name,
            System.Text.Encoding.ASCII.GetBytes(
                "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n"),
            "application/pdf");

    /// <summary>A real 1×1 PNG, standing in for a photograph of an identity document.</summary>
    public static UploadFile Png(string field, string name = "karta.png") =>
        new(field, name, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="),
            "image/png");

    /// <summary>The same PNG, but named and declared as a JPEG.</summary>
    public static UploadFile Jpeg(string field, string name = "karta.jpg") =>
        Png(field, name) with { ContentType = "image/jpeg" };

    /// <summary>A file of any given size, for the checks against the size limit.</summary>
    public UploadFile OfSize(int bytes) =>
        this with { Content = new byte[bytes] };
}
