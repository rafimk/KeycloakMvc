using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace KeycloakMvc.TagHelpers;

/// <summary>
/// Renders <c>&lt;icon name="monitor" /&gt;</c> as a decorative SVG that references the
/// sprite in wwwroot/img/icons.svg. Icons are hidden from assistive technology, so the
/// surrounding element must carry the accessible text.
/// </summary>
[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public class IconTagHelper(IUrlHelperFactory urlHelperFactory) : TagHelper
{
    public required string Name { get; set; }

    [HtmlAttributeNotBound, ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var sprite = urlHelperFactory.GetUrlHelper(ViewContext).Content("~/img/icons.svg");

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.AddClass("icon", HtmlEncoder.Default);
        output.Attributes.SetAttribute("aria-hidden", "true");
        output.Attributes.SetAttribute("focusable", "false");

        var use = new TagBuilder("use");
        use.Attributes["href"] = $"{sprite}#{Name}";
        output.Content.SetHtmlContent(use);
    }
}
