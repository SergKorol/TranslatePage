using System.Diagnostics;
using System.Globalization;
using DeepL;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using TranslatePageApi.Models;

namespace TranslatePageApi.Controllers;

public class HomeController(ICompositeViewEngine viewEngine, IMemoryCache cache) : Controller
{
    public async Task<IActionResult> Index()
    {
        var currentCulture = CultureInfo.CurrentCulture.Name;
        var sourceLanguage = "en";
        string targetLanguage;
        
        var htmlContent = await RenderViewToStringAsync("Index");
        switch (currentCulture)
        {
            case "en-US":
                return Content(htmlContent, "text/html");
            case "fr-FR":
                targetLanguage = "fr";
                break;
            default:
                return BadRequest("Unsupported language.");
        }

        var nodes = ExtractNodes(htmlContent);

        var cacheKey = string.Join("_", nodes) + $"_{sourceLanguage}_{targetLanguage}";

        if (!cache.TryGetValue(cacheKey, out string[]? texts))
        {
            var translator = new Translator("yourapikey");
            var text = await translator.TranslateTextAsync(nodes, sourceLanguage, targetLanguage);
            texts = text.Select(x => x.Text).ToArray();

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            };
            cache.Set(cacheKey, texts, cacheOptions);
        }

        for (var i = 0; i < nodes.Length; i++)
        {
            var oldNode = nodes.ElementAt(i);
            if (texts == null) continue;
            var newNode = texts.ElementAt(i);
            htmlContent = htmlContent.Replace(oldNode, newNode);
        }

        return Content(htmlContent, "text/html");
    }

    
    [HttpPost]
    public IActionResult SetLanguage(string culture, string? returnUrl)
    {
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
        );
    
        return LocalRedirect(returnUrl ?? "/");
    }
    

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
    
    private async Task<string> RenderViewToStringAsync(string viewName)
    {
        await using var writer = new StringWriter();
        var viewResult = viewEngine.FindView(ControllerContext, viewName, isMainPage: true);
        if (!viewResult.Success)
        {
            throw new FileNotFoundException($"View {viewName} not found");
        }

        ViewData["Title"] = "Home Page";

        var viewContext = new ViewContext(
            ControllerContext,
            viewResult.View,
            ViewData,
            TempData,
            writer,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }
    
    private static string[] ExtractNodes(string htmlContent)
    {
        var nodes = new List<string>();
        var tags = new[] { "//title", "//ul", "//h1", "//p", "//footer" };
        
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(htmlContent);

        foreach (var tag in tags)
        {
            var node = htmlDoc.DocumentNode.SelectSingleNode(tag);
            if (node.InnerHtml != null)
            {
                nodes.Add(node.InnerHtml);
            }
        }

        return nodes.ToArray();
    }
}