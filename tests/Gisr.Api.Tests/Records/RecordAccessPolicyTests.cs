using Gisr.Api.Records;
using Gisr.Database.Records;

namespace Gisr.Api.Tests.Records;

public class RecordAccessPolicyTests
{
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly Guid OtherAuthor = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static RecordRow Own() => new() { SubjectAccountId = Subject, AuthorAccountId = Subject };

    private static RecordRow Service() => new() { SubjectAccountId = Subject, AuthorAccountId = OtherAuthor };

    private static readonly RecordCaller SubjectCaller = new(Subject);
    private static readonly RecordCaller OfficerCaller = new(Guid.NewGuid(), true);
    private static readonly RecordCaller StrangerCaller = new(Stranger);
    private static readonly RecordCaller OfficerWhoIsSubject = new(Subject, true);

    [Fact]
    public void View_SubjectAndOfficerMayRead_OthersLearnNothing()
    {
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.View(Own(), SubjectCaller));
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.View(Service(), SubjectCaller));
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.View(Own(), OfficerCaller));
        Assert.Equal(AccessDecision.NotFound, RecordAccessPolicy.View(Own(), StrangerCaller));
        Assert.Equal(AccessDecision.NotFound, RecordAccessPolicy.View(Service(), StrangerCaller));
    }

    [Fact]
    public void Edit_SubjectMustAlsoBeTheAuthor_OfficerMayEditAnything()
    {
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.Edit(Own(), SubjectCaller));
        Assert.Equal(AccessDecision.Forbidden, RecordAccessPolicy.Edit(Service(), SubjectCaller));
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.Edit(Own(), OfficerCaller));
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.Edit(Service(), OfficerCaller));
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.Edit(Service(), OfficerWhoIsSubject));
    }

    [Fact]
    public void Edit_OtherUsers_LearnNothingAboutTheRecord()
    {
        Assert.Equal(AccessDecision.NotFound, RecordAccessPolicy.Edit(Own(), StrangerCaller));
        Assert.Equal(AccessDecision.NotFound, RecordAccessPolicy.Edit(Service(), StrangerCaller));
    }

    [Fact]
    public void Create_ForSelfAlwaysAllowed_ForAnotherCitizenOnlyOfficers()
    {
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.Create(forSelf: true, SubjectCaller));
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.Create(forSelf: true, OfficerCaller));
        Assert.Equal(AccessDecision.Forbidden, RecordAccessPolicy.Create(forSelf: false, SubjectCaller));
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.Create(forSelf: false, OfficerCaller));
    }

    [Fact]
    public void ListAll_IsOfficerOnly()
    {
        Assert.Equal(AccessDecision.Allowed, RecordAccessPolicy.ListAll(OfficerCaller));
        Assert.Equal(AccessDecision.Forbidden, RecordAccessPolicy.ListAll(SubjectCaller));
    }

    [Fact]
    public void AccessLog_IsRequiredOnlyWhenSomeoneOtherThanTheSubjectReads()
    {
        Assert.False(RecordAccessPolicy.RequiresAccessLog(Own(), SubjectCaller));
        Assert.False(RecordAccessPolicy.RequiresAccessLog(Own(), OfficerWhoIsSubject));
        Assert.True(RecordAccessPolicy.RequiresAccessLog(Own(), OfficerCaller));
    }

    [Fact]
    public void ImplicitCallerFromAccountId_NeverGrantsOfficerRights()
    {
        RecordCaller caller = Guid.NewGuid();

        Assert.False(caller.IsOfficer);
    }
}
