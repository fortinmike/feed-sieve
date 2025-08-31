using System.Web;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("/")]
public class MainController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly Filter _filter;

    public MainController(IWebHostEnvironment env, Filter filter)
    {
        _env = env;
        _filter = filter;
    }

    public IActionResult Index()
    {
        return Content("feed-sieve is running!", "text/html");
    }

    [HttpGet("filter")]
    public IActionResult Filter([FromQuery] string url)
    {
        var feedUrl = HttpUtility.UrlDecode(url);
        var originalDocument = XDocument.Load(feedUrl);
        var modifiedDocument = _filter.Process(originalDocument, feedUrl);

        if (_env.IsDevelopment())
        {
            System.IO.File.WriteAllText("./original.xml", originalDocument.ToString());
            System.IO.File.WriteAllText("./modified.xml", modifiedDocument.ToString());
        }

        return Content(modifiedDocument.ToString(), "application/rss+xml");
    }
}
