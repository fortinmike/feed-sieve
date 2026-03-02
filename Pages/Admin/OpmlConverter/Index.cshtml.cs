using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class OpmlConverterModel : PageModel
{
    private readonly FilterUrlBuilder _filterUrlBuilder;
    private readonly IWebHostEnvironment _environment;

    [BindProperty]
    public IFormFile? OpmlFile { get; set; }

    public string? DownloadUrl { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int ConvertedFeedsCount { get; private set; }

    public OpmlConverterModel(FilterUrlBuilder filterUrlBuilder, IWebHostEnvironment environment)
    {
        _filterUrlBuilder = filterUrlBuilder;
        _environment = environment;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (OpmlFile is null || OpmlFile.Length == 0)
        {
            ErrorMessage = "Choose an OPML file to convert";
            return Page();
        }

        XDocument document;
        try
        {
            await using var inputStream = OpmlFile.OpenReadStream();
            document = XDocument.Load(inputStream, LoadOptions.PreserveWhitespace);
        }
        catch (Exception)
        {
            ErrorMessage = "File content is not valid XML";
            return Page();
        }

        ConvertedFeedsCount = ConvertFeedUrls(document);
        var outputDirectory = Path.Combine(_environment.ContentRootPath, ".tmp", "opml-converter");
        Directory.CreateDirectory(outputDirectory);

        var downloadId = Guid.NewGuid().ToString("N");
        var outputPath = Path.Combine(outputDirectory, $"{downloadId}.opml");
        await using (var outputStream = System.IO.File.Create(outputPath))
            document.Save(outputStream);

        var outputFileName = BuildOutputFileName(OpmlFile.FileName);
        DownloadUrl = Url.Page(
            "/Admin/OpmlConverter/Index",
            "Download",
            new { id = downloadId, fileName = outputFileName }
        );

        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(string id, string? fileName)
    {
        if (!IsValidDownloadId(id))
            return NotFound();

        var outputDirectory = Path.Combine(_environment.ContentRootPath, ".tmp", "opml-converter");
        var outputPath = Path.Combine(outputDirectory, $"{id}.opml");
        if (!System.IO.File.Exists(outputPath))
            return NotFound();

        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "feeds-feed-sieve.opml" : Path.GetFileName(fileName);
        var content = await System.IO.File.ReadAllBytesAsync(outputPath);
        System.IO.File.Delete(outputPath);
        return File(content, "text/x-opml", safeFileName);
    }

    private int ConvertFeedUrls(XDocument document)
    {
        var converted = 0;
        var outlines = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase));

        foreach (var outline in outlines)
        {
            var xmlUrlAttribute = outline
                .Attributes()
                .FirstOrDefault(attribute =>
                    string.Equals(attribute.Name.LocalName, "xmlUrl", StringComparison.OrdinalIgnoreCase)
                );

            if (xmlUrlAttribute is null)
                continue;

            var feedUrl = xmlUrlAttribute.Value.Trim();
            if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var parsedFeedUrl))
                continue;

            if (parsedFeedUrl.Scheme != Uri.UriSchemeHttp && parsedFeedUrl.Scheme != Uri.UriSchemeHttps)
                continue;

            xmlUrlAttribute.Value = _filterUrlBuilder.Build(Request, feedUrl);
            converted++;
        }

        return converted;
    }

    private static string BuildOutputFileName(string inputFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(inputFileName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "feeds";

        return $"{baseName}-feed-sieve.opml";
    }

    private static bool IsValidDownloadId(string id) =>
        id.Length == 32 && id.All(character => Uri.IsHexDigit(character));
}
