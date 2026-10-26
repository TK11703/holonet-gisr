using Gisr.Database.Records;

namespace Gisr.Api.Records;

public enum AccessDecision
{
    Allowed,
    NotFound,
    Forbidden,
}

// Single rule set from research D-6. NotFound hides a record's existence from callers who may not read it.
public static class RecordAccessPolicy
{
    public static AccessDecision View(RecordRow record, RecordCaller caller) =>
        caller.IsOfficer || record.SubjectAccountId == caller.AccountId ? AccessDecision.Allowed : AccessDecision.NotFound;

    public static AccessDecision Edit(RecordRow record, RecordCaller caller)
    {
        if (View(record, caller) != AccessDecision.Allowed)
        {
            return AccessDecision.NotFound;
        }

        var ownRecord = record.SubjectAccountId == caller.AccountId && record.AuthorAccountId == caller.AccountId;
        return caller.IsOfficer || ownRecord ? AccessDecision.Allowed : AccessDecision.Forbidden;
    }

    public static AccessDecision Create(bool forSelf, RecordCaller caller) =>
        forSelf || caller.IsOfficer ? AccessDecision.Allowed : AccessDecision.Forbidden;

    public static AccessDecision ListAll(RecordCaller caller) =>
        caller.IsOfficer ? AccessDecision.Allowed : AccessDecision.Forbidden;

    public static bool RequiresAccessLog(RecordRow record, RecordCaller caller) =>
        record.SubjectAccountId != caller.AccountId;
}
