using Microsoft.AspNetCore.Razor.TagHelpers;

namespace TRobot.Web.Security;

[HtmlTargetElement("a", Attributes = "asp-controller")]
public sealed class PortalModuleLinkTagHelper(PortalModules modules) : TagHelper
{
    // AnchorTagHelper first generates the URL; replace the suspended link afterwards.
    public override int Order => 1000;

    [HtmlAttributeName("asp-controller")]
    public string? Controller { get; set; }

    [HtmlAttributeName("asp-action")]
    public string? Action { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (modules.IsRouteEnabled(Controller, Action)) return;
        output.TagName = "span";
        output.Attributes.RemoveAll("href");
        output.Attributes.RemoveAll("aria-current");
        output.Attributes.SetAttribute("aria-disabled", "true");
        output.Attributes.SetAttribute("title", PortalModules.UnavailableMessage);
        var classes = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(classes) ? "text-muted" : classes + " text-muted");
    }
}
