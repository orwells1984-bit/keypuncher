using System.Net;
using Keypuncher.Director;
using Keypuncher.Pictures;

namespace Keypuncher.Pictures.Tests;

public sealed class WikimediaPhotoFinderTests
{
    private const string Summary = "rest_v1/page/summary/Sable_antelope";
    private const string Entity = "Special:EntityData/Q214062.json";
    private const string ArticleImages = "prop=images&imlimit=20&titles=Sable antelope";
    private const string INaturalist = "api.inaturalist.org/v1/taxa?q=sable antelope";

    /// <summary>Everything the web knows about the sable antelope, as far as these tests go.</summary>
    private static FakeHandler Antelope(string summary = "summary-standard.json", string wikidata = "wikidata-p18.json") => new FakeHandler()
        .Json(Summary, summary)
        .Json(Entity, wikidata)
        .ImageInfo("Sable antelope male.jpg", "imageinfo-good.json")
        .ImageInfo("Sable antelope female.jpg", "imageinfo-lead.json")
        .ImageInfo("Sable antelope skeleton.jpg", "imageinfo-skeleton.json")
        .ImageInfo("Sable antelope range.svg", "imageinfo-svg.json")
        .ImageInfo("Sable antelope painting.jpg", "imageinfo-painting.json")
        .ImageInfo("Sable antelope poster art.jpg", "imageinfo-local.json")
        .Json(ArticleImages, "article-images.json")
        .Json(INaturalist, "inaturalist.json");

    private static async Task<PhotoSearch> Find(FakeHandler handler, string name = "sable antelope", RealCategory category = RealCategory.Animal, params string[] exclude)
    {
        using var http = PictureHttp.NewClient(handler);
        return await new WikimediaPhotoFinder(http).FindAsync(name, category, exclude);
    }

    [Fact]
    public async Task Wikidatas_preferred_picture_is_taken_first_after_the_summary_and_the_entity()
    {
        var web = Antelope();
        var search = await Find(web);

        var photo = Assert.IsType<FoundPhoto>(search.Photo);
        Assert.False(search.Offline);
        Assert.Equal("File:Sable antelope male.jpg", photo.Provenance.FileTitle);
        Assert.Null(photo.Doubt);
        Assert.Equal(3, web.Requests.Count);
        Assert.Contains(Summary, web.Requests[0]);
        Assert.Contains(Entity, web.Requests[1]);
        Assert.Contains("commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2&titles=File:Sable antelope male.jpg&prop=imageinfo|categories&iiprop=url|size|mime|mediatype|sha1|extmetadata&iiurlwidth=1280", web.Requests[2]);
        Assert.Contains("clshow=!hidden&cllimit=max", web.Requests[2]);
    }

    [Fact]
    public async Task The_photo_carries_its_credit_with_the_html_stripped_and_is_fetched_as_a_1280_px_copy()
    {
        var search = await Find(Antelope());
        var p = search.Photo!.Provenance;

        Assert.Equal("https://upload.wikimedia.org/wikipedia/commons/thumb/4/4b/Sable_antelope_male.jpg/1280px-Sable_antelope_male.jpg", search.Photo.DownloadUrl);
        Assert.Equal("https://upload.wikimedia.org/wikipedia/commons/4/4b/Sable_antelope_male.jpg", p.FileUrl);
        Assert.Equal("Charles J. Sharp", p.Artist);
        Assert.Equal("Own work", p.Credit);
        Assert.Equal("CC BY-SA 4.0", p.LicenseShortName);
        Assert.Equal("https://creativecommons.org/licenses/by-sa/4.0", p.LicenseUrl);
        Assert.Equal("Sable antelope", p.PageTitle);
        Assert.Equal("https://en.wikipedia.org/wiki/Sable_antelope", p.PageUrl);
        Assert.Equal("https://commons.wikimedia.org/wiki/File:Sable_antelope_male.jpg", p.DescriptionUrl);
        Assert.Equal((2048, 1365, "image/jpeg"), (p.Width, p.Height, p.Mime));
        Assert.Equal("Wikidata's picture of it", p.How);
        Assert.Equal("Charles J. Sharp, CC BY-SA 4.0, via Wikimedia Commons", new PictureSidecar("sable-antelope", "sable antelope", RealCategory.Animal, PictureSource.Photo, DateTime.UnixEpoch) { Photo = p }.CreditLine);
    }

    [Fact]
    public async Task A_photo_under_1280_wide_is_fetched_at_960_with_one_more_look_up()
    {
        var search = await Find(new FakeHandler()
            .Json(Summary, "summary-no-image.json")
            .Json(Entity, "wikidata-skeleton-only.json")
            .ImageInfo("Sable antelope skeleton.jpg", "imageinfo-960.json")
            .Text("iiurlwidth=960", """{"batchcomplete":true,"query":{"pages":[{"pageid":52215,"ns":6,"title":"File:Sable antelope calf.jpg","imageinfo":[{"thumburl":"https://upload.wikimedia.org/wikipedia/commons/thumb/4/4b/Sable_antelope_calf.jpg/960px-Sable_antelope_calf.jpg","thumbwidth":960,"thumbheight":640,"url":"https://upload.wikimedia.org/wikipedia/commons/4/4b/Sable_antelope_calf.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Sable_antelope_calf.jpg"}]}]}}"""));

        Assert.Equal("https://upload.wikimedia.org/wikipedia/commons/thumb/4/4b/Sable_antelope_calf.jpg/960px-Sable_antelope_calf.jpg", search.Photo!.DownloadUrl);
        Assert.Equal(1000, search.Photo.Provenance.Width);
    }

    [Fact]
    public async Task A_skeleton_is_turned_down_and_the_articles_lead_picture_taken()
    {
        var web = Antelope(wikidata: "wikidata-skeleton-only.json");
        var search = await Find(web);

        Assert.Equal("File:Sable antelope female.jpg", search.Photo!.Provenance.FileTitle);
        Assert.Equal("the article's lead picture", search.Photo.Provenance.How);
        Assert.Contains("titles=File:Sable antelope skeleton.jpg", web.Requests[2]);
        Assert.Contains("titles=File:Sable antelope female.jpg", web.Requests[3]);
        Assert.Contains(search.Notes, n => n.Contains("skeleton") && n.Contains("file name says skeleton"));
    }

    [Fact]
    public async Task With_no_wikidata_picture_and_no_lead_an_animal_gets_its_inaturalist_photo()
    {
        var web = Antelope(summary: "summary-no-image.json", wikidata: "wikidata-empty.json");
        var search = await Find(web);

        var p = search.Photo!.Provenance;
        Assert.Equal("inaturalist:3108211", p.FileTitle);
        Assert.Equal("https://inaturalist-open-data.s3.amazonaws.com/photos/3108211/large.jpg", search.Photo.DownloadUrl);
        Assert.Equal("CC BY NC", p.LicenseShortName);
        Assert.Equal("Sable Antelope", p.PageTitle);
        Assert.Equal("https://www.inaturalist.org/taxa/42054", p.PageUrl);
        Assert.Equal("(c) Bernard DUPONT, some rights reserved (CC BY-NC)", p.Artist);
        Assert.Equal((2048, 1365), (p.Width, p.Height));
        Assert.Equal("iNaturalist", p.Site);
        Assert.Equal(3, web.Requests.Count);
        Assert.Contains("api.inaturalist.org/v1/taxa?q=sable antelope&per_page=3", web.Requests[2]);
        Assert.DoesNotContain(web.Requests, r => r.Contains(ArticleImages));
    }

    [Fact]
    public async Task An_object_takes_the_next_two_bitmap_pictures_in_the_article_skipping_the_lead_and_local_uploads()
    {
        var web = new FakeHandler()
            .Json(Summary, "summary-local-lead.json")
            .Json(Entity, "wikidata-empty.json")
            .ImageInfo("Sable antelope female.jpg", "imageinfo-local.json")
            .ImageInfo("Sable antelope male.jpg", "imageinfo-good.json")
            .Json(ArticleImages, "article-images.json");
        var search = await Find(web, category: RealCategory.Object);

        Assert.Equal("File:Sable antelope male.jpg", search.Photo!.Provenance.FileTitle);
        Assert.Equal("a picture in the article", search.Photo.Provenance.How);
        Assert.DoesNotContain(web.Requests, r => r.Contains("poster art"));
        Assert.DoesNotContain(web.Requests, r => r.Contains("inaturalist"));
        Assert.DoesNotContain(web.Requests, r => r.Contains("skeleton"));
        Assert.Contains(search.Notes, n => n.Contains("poster art") && n.Contains("local"));
    }

    [Fact]
    public async Task A_painting_is_taken_only_when_nothing_better_turns_up_and_carries_the_doubt()
    {
        var web = new FakeHandler()
            .Json(Summary, "summary-no-image.json")
            .Json(Entity, "wikidata-painting-only.json")
            .ImageInfo("Sable antelope painting.jpg", "imageinfo-painting.json")
            .ImageInfo("Sable antelope female.jpg", "imageinfo-svg.json")
            .ImageInfo("Sable antelope male.jpg", "imageinfo-skeleton.json")
            .Json(ArticleImages, "article-images.json");
        var search = await Find(web, category: RealCategory.Object);

        Assert.Equal("File:Sable antelope painting.jpg", search.Photo!.Provenance.FileTitle);
        Assert.Equal("a drawing or painting rather than a photo", search.Photo.Doubt);
        Assert.Contains(web.Requests, r => r.Contains(ArticleImages));
        Assert.Equal(6, web.Requests.Count);
    }

    [Fact]
    public async Task A_disambiguation_page_leads_through_the_search_to_the_article_whose_description_fits()
    {
        var web = new FakeHandler()
            .Json("rest_v1/page/summary/Seal_(musician)", "summary-seal-musician.json")
            .Json("rest_v1/page/summary/Seal", "summary-disambiguation.json")
            .Json("list=search&srsearch=seal&srlimit=5", "search-seal.json")
            .Json("rest_v1/page/summary/Pinniped", "summary-pinniped.json")
            .Json("Special:EntityData/Q25587.json", "wikidata-pinniped.json")
            .ImageInfo("Harbor seal on rocks.jpg", "imageinfo-harbor-seal.json");
        var search = await Find(web, "seal");

        Assert.Equal("File:Harbor seal on rocks.jpg", search.Photo!.Provenance.FileTitle);
        Assert.Equal("Pinniped", search.Photo.Provenance.PageTitle);
        Assert.Equal("https://en.wikipedia.org/wiki/Pinniped", search.Photo.Provenance.PageUrl);
        Assert.Equal(6, web.Requests.Count);
        Assert.Contains("summary/Seal", web.Requests[0]);
        Assert.Contains("list=search", web.Requests[1]);
        Assert.Contains("summary/Seal_(musician)", web.Requests[2]);
        Assert.Contains("summary/Pinniped", web.Requests[3]);
        Assert.Contains("Q25587", web.Requests[4]);
        Assert.Contains(search.Notes, n => n.Contains("singer") && n.Contains("not an animal"));
    }

    [Fact]
    public async Task An_extinct_animal_gets_a_life_restoration_from_commons_before_its_skeleton()
    {
        var web = new FakeHandler()
            .Json("rest_v1/page/summary/Woolly_mammoth", "summary-mammoth.json")
            .Json("Special:EntityData/Q33854.json", "wikidata-mammoth.json")
            .Json("gsrsearch=haswbstatement:P180=Q33854 filetype:bitmap", "commons-p180.json")
            .ImageInfo("Woolly mammoth skeleton.jpg", "imageinfo-mammoth-skeleton.json")
            .ImageInfo("Woolly mammoth restoration.jpg", "imageinfo-mammoth-restoration.json")
            .ImageInfo("Woolly mammoth model Royal BC Museum.jpg", "imageinfo-mammoth-model.json");
        var search = await Find(web, "woolly mammoth", RealCategory.ExtinctAnimal);

        Assert.Equal("File:Woolly mammoth restoration.jpg", search.Photo!.Provenance.FileTitle);
        Assert.Equal("a Commons file depicting it", search.Photo.Provenance.How);
        Assert.Equal("Mauricio Antón", search.Photo.Provenance.Artist);
        Assert.Contains("gsrnamespace=6&gsrlimit=10&gsrsearch=haswbstatement:P180=Q33854 filetype:bitmap&prop=imageinfo", web.Requests[3]);
        Assert.Contains("titles=File:Woolly mammoth skeleton.jpg", web.Requests[2]);
        Assert.Contains("titles=File:Woolly mammoth restoration.jpg", web.Requests[4]);
        Assert.DoesNotContain(web.Requests, r => r.Contains("Royal BC Museum"));
    }

    [Fact]
    public async Task Files_turned_down_before_are_skipped_however_they_are_spelt()
    {
        var web = Antelope();
        var search = await Find(web, exclude: "File:Sable_antelope_male.jpg");

        Assert.Equal("File:Sable antelope female.jpg", search.Photo!.Provenance.FileTitle);
        Assert.DoesNotContain(web.Requests, r => r.Contains(" male.jpg&prop"));
        Assert.Contains(search.Notes, n => n.Contains("turned down before"));
    }

    [Fact]
    public async Task No_article_and_no_search_hit_is_nothing_found_not_offline()
    {
        var web = new FakeHandler().Json("list=search", "search-empty.json");
        var search = await Find(web, "snorkelwump", RealCategory.Object);

        Assert.Null(search.Photo);
        Assert.False(search.Offline);
        Assert.Equal("no article", search.Reason);
        Assert.Equal(2, web.Requests.Count);
    }

    [Fact]
    public async Task An_article_with_no_usable_picture_is_nothing_found()
    {
        var web = new FakeHandler()
            .Json(Summary, "summary-no-image.json")
            .Json(Entity, "wikidata-empty.json")
            .Json(INaturalist, "inaturalist-empty.json")
            .Text(ArticleImages, """{"batchcomplete":true,"query":{"pages":[{"pageid":1030727,"ns":0,"title":"Sable antelope"}]}}""");
        var search = await Find(web);

        Assert.Null(search.Photo);
        Assert.False(search.Offline);
        Assert.Equal("no photo found", search.Reason);
    }

    [Fact]
    public async Task A_request_that_never_answers_is_offline()
    {
        var web = new FakeHandler().Hangs(Summary);
        using var http = PictureHttp.NewClient(web);
        http.Timeout = TimeSpan.FromMilliseconds(200);
        var search = await new WikimediaPhotoFinder(http).FindAsync("sable antelope", RealCategory.Animal, []);

        Assert.True(search.Offline);
        Assert.Null(search.Photo);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_server_that_turns_us_away_is_offline(HttpStatusCode code)
    {
        var search = await Find(new FakeHandler().Status(Summary, code));
        Assert.True(search.Offline);
    }

    [Fact]
    public async Task A_broken_connection_is_offline()
    {
        var search = await Find(new FakeHandler().Throws(Summary, new HttpRequestException("connection refused")));
        Assert.True(search.Offline);
        Assert.Contains("connection refused", search.Reason);
    }

    [Fact]
    public async Task Cancelling_the_search_is_not_offline_but_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var http = PictureHttp.NewClient(Antelope());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WikimediaPhotoFinder(http).FindAsync("sable antelope", RealCategory.Animal, [], cts.Token));
    }

    [Theory]
    [InlineData("sable antelope", "Sable_antelope")]
    [InlineData("Steller's sea cow", "Steller's_sea_cow")]
    [InlineData(" oak tree ", "Oak_tree")]
    public void The_article_title_has_its_first_letter_up_and_underscores(string name, string title) =>
        Assert.Equal(title, WikimediaPhotoFinder.TitleFor(name));

    [Theory]
    [InlineData("Species of antelope", RealCategory.Animal, true)]
    [InlineData("Infraorder of mammals", RealCategory.Animal, true)]
    [InlineData("British singer-songwriter", RealCategory.Animal, false)]
    [InlineData("Extinct species of mammoth", RealCategory.ExtinctAnimal, true)]
    [InlineData("Genus of dinosaurs", RealCategory.ExtinctAnimal, true)]
    [InlineData("Species of flowering plant", RealCategory.Plant, true)]
    [InlineData("Deciduous tree", RealCategory.Plant, true)]
    [InlineData("Species of bird", RealCategory.Plant, false)]
    [InlineData("Agricultural vehicle", RealCategory.Vehicle, true)]
    [InlineData("Type of bus", RealCategory.Vehicle, true)]
    [InlineData("Village in Devon, England", RealCategory.Vehicle, false)]
    [InlineData("Container for brewing tea", RealCategory.Object, true)]
    [InlineData("English footballer (born 1990)", RealCategory.Object, false)]
    [InlineData("1994 film", RealCategory.Object, false)]
    [InlineData(null, RealCategory.Object, false)]
    public void A_description_fits_a_category_or_does_not(string? description, RealCategory category, bool fits) =>
        Assert.Equal(fits, WikimediaPhotoFinder.Fits(description, category));

    [Theory]
    [InlineData("<a href=\"//commons.wikimedia.org/wiki/User:Example\">Charles J. Sharp</a>", "Charles J. Sharp")]
    [InlineData("<span class=\"int-own-work\" lang=\"en\">Own work</span>", "Own work")]
    [InlineData("Hans &amp; Grete", "Hans & Grete")]
    [InlineData("<br/>", null)]
    [InlineData(null, null)]
    public void Credits_lose_their_html(string? html, string? plain) => Assert.Equal(plain, WikimediaPhotoFinder.Plain(html));

    [Theory]
    [InlineData("Sable_antelope_male.jpg", "File:Sable antelope male.jpg")]
    [InlineData("file:Sable antelope male.jpg", "File:Sable antelope male.jpg")]
    [InlineData("File:Sable antelope male.jpg", "File:Sable antelope male.jpg")]
    public void File_titles_are_spelt_one_way(string title, string normal) => Assert.Equal(normal, WikimediaPhotoFinder.NormalTitle(title));
}
