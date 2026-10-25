using System.Net;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;
using AbioticEditor.Web.Components.Player;
using AbioticEditor.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AbioticEditor.Tests;

public sealed class RecipeAcquisitionRenderingTests
{
    [Theory]
    [InlineData(false, true, false, "RecipeGuide_CatalogUnavailable")]
    [InlineData(true, false, false, "RecipeGuide_ItemMissing")]
    [InlineData(true, true, false, "RecipeGuide_NoMaterialSources")]
    [InlineData(true, true, true, "RecipeGuide_GroupUnknown")]
    public async Task Missing_material_information_has_a_specific_explanation(bool available, bool known, bool group, string expected)
    {
        var language = new HostLanguageService();
        var parameters = AcquisitionParameters([]);
        parameters[nameof(ItemAcquisitionDetails.CatalogAvailable)] = available;
        parameters[nameof(ItemAcquisitionDetails.ItemKnown)] = known;
        parameters[nameof(ItemAcquisitionDetails.IsGroup)] = group;
        var html = await Render<ItemAcquisitionDetails>(language, parameters);

        Assert.Contains(language.Resource(expected), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeGuide_MaterialNotLearning"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("recipe-acquisition-list", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sealed_material_routes_hide_producers_traders_locations_and_conditions()
    {
        var language = new HostLanguageService();
        ItemAcquisitionSource[] sources =
        [
            new(ItemAcquisitionKind.Salvage, "target", SalvageSourceItemId: "secretproducer", Salvage: new("target", 1, 2, 0.5)),
            new(ItemAcquisitionKind.Trade, "target", Trader: new("secrettrader", null, ["secretflag"],
                [new("target", 1, "secretofferflag")], [new("secretpayment", 1, "secretpaymentflag")]), Offer: new("target", 1, "secretofferflag")),
            new(ItemAcquisitionKind.Fishing, "target", Fish: new("secretfish", "target", false,
                Location: "secretlocation", RequiredWorldFlag: "secretfishflag", RequiredDlcId: "secretdlc", RequiredBaitTag: "secretbait")),
        ];
        var parameters = AcquisitionParameters(sources);
        parameters[nameof(ItemAcquisitionDetails.Concealed)] = (Func<ItemAcquisitionSource, bool>)(_ => true);
        parameters[nameof(ItemAcquisitionDetails.TraderWhere)] = (Func<string, string?>)(_ => "secretwhere");
        var html = await Render<ItemAcquisitionDetails>(language, parameters);

        Assert.Contains(language.Resource("RecipeGuide_AcquisitionSealed"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeGuide_RevealAcquisition"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(language.Resource("RecipeGuide_StoryUnmet"), html, StringComparison.Ordinal);
        Assert.DoesNotContain(language.Resource("RecipeGuide_TradeNotPaired"), html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Learned_recipe_does_not_show_an_unknown_learning_warning(bool learned)
    {
        var language = new HostLanguageService();
        var html = await Render<RecipeDiscoverySources>(language, new()
        {
            [nameof(RecipeDiscoverySources.Assessment)] = new RecipeDiscoveryAssessment("recipe", []),
            [nameof(RecipeDiscoverySources.Learned)] = learned,
            [nameof(RecipeDiscoverySources.ItemName)] = (Func<string, string>)(id => id),
        });
        if (learned)
        {
            Assert.Contains(language.Resource("RecipeGuide_AlreadyLearned"), html, StringComparison.Ordinal);
            Assert.DoesNotContain(language.Resource("RecipeGuide_DiscoveryUnknown"), html, StringComparison.Ordinal);
            Assert.DoesNotContain(language.Resource("RecipeGuide_LearnInGame"), html, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(language.Resource("RecipeGuide_DiscoveryUnknown"), html, StringComparison.Ordinal);
            Assert.DoesNotContain(language.Resource("RecipeGuide_AlreadyLearned"), html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Many_routes_initially_show_eight_and_offer_an_explicit_show_all_action()
    {
        var language = new HostLanguageService();
        var sources = Enumerable.Range(0, 10).Select(index => new ItemAcquisitionSource(ItemAcquisitionKind.Trade, "target",
            Trader: new($"vendor-{index}", null, [], [new("target", 1, null)], []), Offer: new("target", 1, null))).ToArray();
        var html = await Render<ItemAcquisitionDetails>(language, AcquisitionParameters(sources));

        for (var index = 0; index < 8; index++) Assert.Contains($"vendor-{index}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor-8", html, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor-9", html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeGuide_AllSources", 10), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trade_guidance_does_not_invent_a_price_or_expose_unrelated_accepted_items()
    {
        var language = new HostLanguageService();
        var trader = new TraderInfo("vendor", null, [], [new("target", 2, null)], [new("secretfuturepayment", 3, "secretfutureflag")]);
        var html = await Render<ItemAcquisitionDetails>(language, AcquisitionParameters(
            [new(ItemAcquisitionKind.Trade, "target", Trader: trader, Offer: trader.Sells[0])]));

        Assert.Contains(language.Resource("RecipeGuide_TradeStock", 2, "target"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeGuide_TradeNotPaired"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("secretfuture", html, StringComparison.Ordinal);
        Assert.DoesNotContain(language.Resource("RecipeGuide_TraderAccepts", "3 × secretfuturepayment"), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Salvage_guidance_reports_a_possible_quantity_range_without_a_guaranteed_drop()
    {
        var language = new HostLanguageService();
        var html = await Render<ItemAcquisitionDetails>(language, AcquisitionParameters(
            [new(ItemAcquisitionKind.Salvage, "target", SalvageSourceItemId: "producer", Salvage: new("target", 1, 3, 100))]));

        Assert.Contains(language.Resource("RecipeGuide_SalvageItem", "producer"), html, StringComparison.Ordinal);
        Assert.Contains(language.Resource("RecipeGuide_MayYield", "1-3", "target"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("100%", html, StringComparison.Ordinal);
        Assert.DoesNotContain("guaranteed", html, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object?> AcquisitionParameters(ItemAcquisitionSource[] sources) => new()
    {
        [nameof(ItemAcquisitionDetails.ItemId)] = "target",
        [nameof(ItemAcquisitionDetails.Sources)] = sources,
        [nameof(ItemAcquisitionDetails.ItemName)] = (Func<string, string>)(id => id),
        [nameof(ItemAcquisitionDetails.TraderName)] = (Func<string, string>)(id => id),
        [nameof(ItemAcquisitionDetails.CatalogAvailable)] = true,
        [nameof(ItemAcquisitionDetails.ItemKnown)] = true,
    };

    private static async Task<string> Render<T>(HostLanguageService language, Dictionary<string, object?> parameters) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(language);
        services.AddSingleton<UserFacingErrorService>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return WebUtility.HtmlDecode(view.ToHtmlString());
        });
    }
}
