using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ReverseMarkdown;

namespace ConfluenceExporter.Services;

public interface IMarkdownConverter
{
    Task<string> ConvertToMarkdownAsync(string html, CancellationToken cancellationToken = default);
}

public class MarkdownConverter : IMarkdownConverter
{
    private readonly ILogger<MarkdownConverter> _logger;
    private readonly Converter _converter;

    public MarkdownConverter(ILogger<MarkdownConverter> logger)
    {
        _logger = logger;
        _converter = new Converter(new Config
        {
            UnknownTags = Config.UnknownTagsOption.Bypass,
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true,
            DefaultCodeBlockLanguage = "text"
        });
    }

    public async Task<string> ConvertToMarkdownAsync(string html, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        try
        {
            var processedHtml = await PreprocessConfluenceHtmlAsync(html, cancellationToken);
            var markdown = _converter.Convert(processedHtml);
            var cleanedMarkdown = PostprocessMarkdown(markdown);
            
            return cleanedMarkdown;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert HTML to Markdown");
            return $"```html\n{html}\n```\n\n*Note: Failed to convert to Markdown*";
        }
    }

    private async Task<string> PreprocessConfluenceHtmlAsync(string html, CancellationToken cancellationToken)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        await ProcessConfluenceMacrosAsync(doc, cancellationToken);
        ProcessTables(doc);
        ProcessCodeBlocks(doc);
        ProcessImages(doc);
        ProcessLinks(doc);
        ProcessStatusMacros(doc);
        ProcessInfoPanels(doc);

        return doc.DocumentNode.OuterHtml;
    }

    private async Task ProcessConfluenceMacrosAsync(HtmlDocument doc, CancellationToken cancellationToken)
    {
        var macros = doc.DocumentNode.SelectNodes("//ac:structured-macro");
        if (macros == null) return;

        foreach (var macro in macros)
        {
            var macroName = macro.GetAttributeValue("ac:name", "");
            
            switch (macroName.ToLowerInvariant())
            {
                case "code":
                    ProcessCodeMacro(macro);
                    break;
                case "info":
                case "note":
                case "warning":
                case "tip":
                    ProcessInfoMacro(macro, macroName);
                    break;
                case "table-of-contents":
                case "toc":
                    ProcessTocMacro(macro);
                    break;
                case "children":
                    ProcessChildrenMacro(macro);
                    break;
                case "include":
                    ProcessIncludeMacro(macro);
                    break;
                case "excerpt":
                    ProcessExcerptMacro(macro);
                    break;
                default:
                    ProcessGenericMacro(macro, macroName);
                    break;
            }
        }
    }

    private static void ProcessCodeMacro(HtmlNode macro)
    {
        var language = macro.SelectSingleNode(".//ac:parameter[@ac:name='language']")?.InnerText ?? "text";
        var codeBody = macro.SelectSingleNode(".//ac:plain-text-body")?.InnerText ?? "";
        
        var codeBlock = macro.OwnerDocument.CreateElement("pre");
        var code = macro.OwnerDocument.CreateElement("code");
        code.SetAttributeValue("class", $"language-{language}");
        code.InnerHtml = HtmlDocument.HtmlEncode(codeBody);
        codeBlock.AppendChild(code);
        
        macro.ParentNode.ReplaceChild(codeBlock, macro);
    }

    private static void ProcessInfoMacro(HtmlNode macro, string type)
    {
        var title = macro.SelectSingleNode(".//ac:parameter[@ac:name='title']")?.InnerText ?? "";
        var body = macro.SelectSingleNode(".//ac:rich-text-body")?.InnerHtml ?? "";
        
        var blockquote = macro.OwnerDocument.CreateElement("blockquote");
        var content = string.IsNullOrEmpty(title) 
            ? $"**{type.ToUpper()}:** {body}"
            : $"**{title}**\n\n{body}";
        blockquote.InnerHtml = content;
        
        macro.ParentNode.ReplaceChild(blockquote, macro);
    }

    private static void ProcessTocMacro(HtmlNode macro)
    {
        var tocDiv = macro.OwnerDocument.CreateElement("div");
        tocDiv.SetAttributeValue("class", "table-of-contents");
        tocDiv.InnerHtml = "*Table of Contents will be generated here*";
        
        macro.ParentNode.ReplaceChild(tocDiv, macro);
    }

    private static void ProcessChildrenMacro(HtmlNode macro)
    {
        var childrenDiv = macro.OwnerDocument.CreateElement("div");
        childrenDiv.SetAttributeValue("class", "children-pages");
        childrenDiv.InnerHtml = "*Child pages will be listed here*";
        
        macro.ParentNode.ReplaceChild(childrenDiv, macro);
    }

    private static void ProcessIncludeMacro(HtmlNode macro)
    {
        var pageTitle = macro.SelectSingleNode(".//ac:parameter[@ac:name='']")?.InnerText ?? "";
        var includeDiv = macro.OwnerDocument.CreateElement("div");
        includeDiv.SetAttributeValue("class", "include-content");
        includeDiv.InnerHtml = $"*Included content from: {pageTitle}*";
        
        macro.ParentNode.ReplaceChild(includeDiv, macro);
    }

    private static void ProcessExcerptMacro(HtmlNode macro)
    {
        var body = macro.SelectSingleNode(".//ac:rich-text-body")?.InnerHtml ?? "";
        var excerptDiv = macro.OwnerDocument.CreateElement("div");
        excerptDiv.SetAttributeValue("class", "excerpt");
        excerptDiv.InnerHtml = body;
        
        macro.ParentNode.ReplaceChild(excerptDiv, macro);
    }

    private static void ProcessGenericMacro(HtmlNode macro, string macroName)
    {
        var parameters = macro.SelectNodes(".//ac:parameter");
        var paramString = parameters?.Select(p => 
            $"{p.GetAttributeValue("ac:name", "param")}: {p.InnerText}")
            .Aggregate((a, b) => $"{a}, {b}") ?? "";
        
        var macroDiv = macro.OwnerDocument.CreateElement("div");
        macroDiv.SetAttributeValue("class", $"confluence-macro-{macroName}");
        macroDiv.InnerHtml = $"*Confluence Macro: {macroName}*" + 
                           (string.IsNullOrEmpty(paramString) ? "" : $" ({paramString})");
        
        macro.ParentNode.ReplaceChild(macroDiv, macro);
    }

    private static void ProcessTables(HtmlDocument doc)
    {
        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return;

        foreach (var table in tables)
        {
            table.SetAttributeValue("class", "confluence-table");
            
            var headers = table.SelectNodes(".//th");
            var rows = table.SelectNodes(".//tr");
            
            if (headers != null && rows != null && rows.Count > 1)
            {
                var headerRow = rows.First(r => r.SelectNodes(".//th") != null);
                if (headerRow != null)
                {
                    headerRow.SetAttributeValue("class", "table-header");
                }
            }
        }
    }

    private static void ProcessCodeBlocks(HtmlDocument doc)
    {
        var codeBlocks = doc.DocumentNode.SelectNodes("//div[@class='code panel']");
        if (codeBlocks == null) return;

        foreach (var block in codeBlocks)
        {
            var codeContent = block.SelectSingleNode(".//div[@class='codeContent panelContent']");
            if (codeContent != null)
            {
                var pre = doc.CreateElement("pre");
                var code = doc.CreateElement("code");
                code.InnerHtml = codeContent.InnerText;
                pre.AppendChild(code);
                
                block.ParentNode.ReplaceChild(pre, block);
            }
        }
    }

    private static void ProcessImages(HtmlDocument doc)
    {
        var images = doc.DocumentNode.SelectNodes("//img");
        if (images == null) return;

        foreach (var img in images)
        {
            var src = img.GetAttributeValue("src", "");
            var alt = img.GetAttributeValue("alt", "");
            var title = img.GetAttributeValue("title", "");

            if (src.StartsWith("/"))
            {
                img.SetAttributeValue("src", $"./assets/{Path.GetFileName(src)}");
            }

            if (!string.IsNullOrEmpty(title))
            {
                img.SetAttributeValue("alt", string.IsNullOrEmpty(alt) ? title : alt);
            }
        }
    }

    private static void ProcessLinks(HtmlDocument doc)
    {
        var links = doc.DocumentNode.SelectNodes("//a[@href]");
        if (links == null) return;

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", "");
            
            if (href.Contains("/pages/viewpage.action?pageId="))
            {
                var pageIdMatch = Regex.Match(href, @"pageId=(\d+)");
                if (pageIdMatch.Success)
                {
                    link.SetAttributeValue("data-confluence-page-id", pageIdMatch.Groups[1].Value);
                    link.SetAttributeValue("title", "Confluence Page Link");
                }
            }
        }
    }

    private static void ProcessStatusMacros(HtmlDocument doc)
    {
        var statusNodes = doc.DocumentNode.SelectNodes("//span[@class='status-macro']");
        if (statusNodes == null) return;

        foreach (var status in statusNodes)
        {
            var color = status.GetAttributeValue("data-colour", "grey");
            var text = status.InnerText;
            
            var badge = doc.CreateElement("span");
            badge.SetAttributeValue("class", $"badge badge-{color}");
            badge.InnerHtml = text;
            
            status.ParentNode.ReplaceChild(badge, status);
        }
    }

    private static void ProcessInfoPanels(HtmlDocument doc)
    {
        var panels = doc.DocumentNode.SelectNodes("//div[contains(@class, 'panel')]");
        if (panels == null) return;

        foreach (var panel in panels)
        {
            var panelType = panel.GetClasses()
                .FirstOrDefault(c => c.Contains("info") || c.Contains("note") || 
                               c.Contains("warning") || c.Contains("tip")) ?? "info";
            
            var blockquote = doc.CreateElement("blockquote");
            blockquote.SetAttributeValue("class", $"panel-{panelType}");
            blockquote.InnerHtml = panel.InnerHtml;
            
            panel.ParentNode.ReplaceChild(blockquote, panel);
        }
    }

    private static string PostprocessMarkdown(string markdown)
    {
        markdown = Regex.Replace(markdown, @"\n{3,}", "\n\n");
        
        markdown = Regex.Replace(markdown, @"^\s+$", "", RegexOptions.Multiline);
        
        markdown = Regex.Replace(markdown, @"(\*\*[^*]+\*\*)\s*\n\s*(\*\*[^*]+\*\*)", "$1 $2");
        
        markdown = Regex.Replace(markdown, @"\\\*", "*");
        markdown = Regex.Replace(markdown, @"\\_", "_");
        
        markdown = Regex.Replace(markdown, @"!\[\]\((.*?)\)", "![]($1)");
        
        return markdown.Trim();
    }
}