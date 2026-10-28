using Bunit;
using Gisr.Web.Help;

namespace Gisr.Web.Tests.Help;

public class FaqTests : BunitContext
{
    [Fact]
    public void RendersTitle_Search_AndPopularSearches()
    {
        var cut = Render<Faq>();

        Assert.Equal("Frequently asked questions", cut.Find("h1").TextContent.Trim());
        Assert.NotNull(cut.Find("[role=search] input[type=search]"));
        Assert.Equal(FaqContent.PopularSearches, cut.FindAll(".faq-chip").Select(chip => chip.TextContent.Trim()));
    }

    [Fact]
    public void RendersEveryTopicAsCard_WithItsQuestions()
    {
        var cut = Render<Faq>();

        var cards = cut.FindAll("section.faq-card");
        Assert.Equal(FaqContent.Topics.Count, cards.Count);
        Assert.Equal(FaqContent.Topics.Sum(topic => topic.Items.Count), cut.FindAll("button.faq-question").Count);
        foreach (var topic in FaqContent.Topics)
        {
            Assert.Contains(cards, card => card.QuerySelector("h2")!.TextContent.Trim() == topic.Title);
        }
    }

    [Fact]
    public void Answers_StartCollapsed_AndToggleWithAriaState()
    {
        var cut = Render<Faq>();
        var question = cut.Find("button.faq-question");
        var answerId = question.GetAttribute("aria-controls")!;

        Assert.Equal("false", question.GetAttribute("aria-expanded"));
        Assert.True(cut.Find($"#{answerId}").HasAttribute("hidden"));

        question.Click();

        question = cut.Find("button.faq-question");
        Assert.Equal("true", question.GetAttribute("aria-expanded"));
        Assert.False(cut.Find($"#{answerId}").HasAttribute("hidden"));

        question.Click();

        Assert.Equal("false", cut.Find("button.faq-question").GetAttribute("aria-expanded"));
        Assert.True(cut.Find($"#{answerId}").HasAttribute("hidden"));
    }

    [Fact]
    public void Typing_FiltersQuestions_AndAnnouncesCount()
    {
        var cut = Render<Faq>();

        cut.Find("#faq-query").Input("override code");

        var shown = cut.FindAll("button.faq-question").Count;
        Assert.InRange(shown, 1, FaqContent.Topics.Sum(topic => topic.Items.Count) - 1);
        Assert.Contains($"{shown} ", cut.Find("[role=status]").TextContent);
        Assert.All(cut.FindAll("button.faq-question"), question =>
            Assert.Contains(FaqContent.Topics.SelectMany(topic => topic.Items),
                item => item.Question == question.TextContent.Trim() && FaqContent.Matches(item, "override code")));
    }

    [Fact]
    public void PopularSearch_AppliesAsQuery()
    {
        var cut = Render<Faq>();

        cut.FindAll(".faq-chip").First(chip => chip.TextContent.Trim() == "Sealed").Click();

        Assert.Equal("Sealed", cut.Find("#faq-query").GetAttribute("value"));
        Assert.NotEmpty(cut.FindAll("button.faq-question"));
        Assert.True(cut.FindAll("button.faq-question").Count < FaqContent.Topics.Sum(topic => topic.Items.Count));
    }

    [Fact]
    public void NoMatches_ShowsEmptyState_AndClearRestoresAll()
    {
        var cut = Render<Faq>();

        cut.Find("#faq-query").Input("zzzzqqqq");

        Assert.Empty(cut.FindAll("section.faq-card"));
        Assert.Contains("zzzzqqqq", cut.Find("#faq-empty").TextContent);

        cut.Find("#faq-empty button").Click();

        Assert.Equal(FaqContent.Topics.Count, cut.FindAll("section.faq-card").Count);
    }

    [Fact]
    public void QuestionButtons_AreHeadingsWithRegionAnswers()
    {
        var cut = Render<Faq>();

        foreach (var question in cut.FindAll("button.faq-question"))
        {
            Assert.Equal("H3", question.ParentElement!.TagName);
            var answer = cut.Find($"#{question.GetAttribute("aria-controls")}");
            Assert.Equal("region", answer.GetAttribute("role"));
            Assert.Equal(question.Id, answer.GetAttribute("aria-labelledby"));
        }
    }

    [Fact]
    public void Content_HasUniqueTopicsAndQuestions_AndRootedLinks()
    {
        Assert.Equal(FaqContent.Topics.Count, FaqContent.Topics.Select(topic => topic.Id).Distinct().Count());
        foreach (var topic in FaqContent.Topics)
        {
            Assert.Equal(topic.Items.Count, topic.Items.Select(item => item.Question).Distinct().Count());
            Assert.All(topic.Items, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Answer));
                Assert.Equal(item.Href is null, item.LinkText is null);
                Assert.True(item.Href is null || item.Href.StartsWith('/'));
            });
        }
    }

    [Fact]
    public void PopularSearches_EachMatchAtLeastOneQuestion()
    {
        var items = FaqContent.Topics.SelectMany(topic => topic.Items).ToArray();

        Assert.All(FaqContent.PopularSearches, term => Assert.Contains(items, item => FaqContent.Matches(item, term)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("SEALED", true)]
    [InlineData("sealed gallery", true)]
    [InlineData("sealed zzzz", false)]
    public void Matches_RequiresEveryTerm_IgnoringCase(string? query, bool expected)
    {
        var item = new FaqItem("Why are sealed images missing?", "They never appear in the gallery.");

        Assert.Equal(expected, FaqContent.Matches(item, query));
    }
}
