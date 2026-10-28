namespace Gisr.Web.Help;

public sealed record FaqItem(string Question, string Answer, string? Href = null, string? LinkText = null);

public sealed record FaqTopic(string Id, string Title, string Description, IReadOnlyList<FaqItem> Items);

// Static copy: answers describe shipped behavior, so update them with the feature.
public static class FaqContent
{
    public static IReadOnlyList<string> PopularSearches { get; } = ["Sealed", "Officer", "Files", "Search"];

    public static IReadOnlyList<FaqTopic> Topics { get; } =
    [
        new("getting-started", "Getting started", "The basics of GISR and signing in.",
        [
            new("What is GISR?",
                "The Galactic Identity and Service Records system is a fictional archive of authoritative records for citizens, military personnel, Jedi, bounty hunters, and others. Each record documents an engagement under an affiliation, with incidents, awards, files, and a revision history.",
                "/about", "Read more about GISR"),
            new("Who can sign in?",
                "Anyone with an account in the organization's Microsoft Entra tenant. GISR does not manage passwords or accounts. Your GISR account and settings are created automatically the first time you sign in."),
            new("Do I start with any records?",
                "No. A new account has settings but no citizen records. Create your first record from the Citizen Records page or with the plus button in the top bar.",
                "/records/new", "Create a record"),
            new("How do I get around quickly?",
                "Use the navigation menu on the left, or the menu button on small screens. Press Ctrl+K (Cmd+K on a Mac) to jump to the search box from any page, and select the GISR seal in the top bar to return home."),
            new("Is there a light theme?",
                "No. GISR uses a single dark appearance."),
        ]),
        new("records", "Records", "Creating, editing, and tracking citizen records.",
        [
            new("How do I create a record?",
                "Select the plus button in the top bar, or New record on the Citizen Records page. A title, an affiliation, and a summary are required; engagement start and end dates are optional. After the first save, the Incidents, Awards, and Artifacts tabs become available.",
                "/records/new", "New record"),
            new("Why can't I choose an affiliation?",
                "A record is filed under one of the affiliations on your account. Add one under Associate an Affiliation in your account settings, then return to the record.",
                "/account", "Account settings"),
            new("What are incidents and awards?",
                "Incidents are dated events of note with a title and an optional description. Awards record a name, the date awarded, and an optional issuer and description. A record holds up to 100 of each, and only Officers can add or remove incidents."),
            new("Can I edit or delete a record?",
                "You can edit and delete records you wrote about yourself, and Officers can edit and delete any record. Open the record and use its actions menu. A deleted record no longer appears in lists, search, or the gallery. A service record an Officer wrote about you can be read but not changed by you."),
            new("What happens if someone edits the record while I do?",
                "GISR prevents silent overwrites. If the record changed after you opened it, you are told so and your entries stay on screen. Reload the latest version and re-apply them."),
            new("Where can I see what changed?",
                "Open a record and expand History. Every save is listed with who made it and when."),
        ]),
        new("service-records", "Service and sealed records", "Records written about others, and how sealing protects them.",
        [
            new("What is a service record?",
                "A citizen record about someone else, written by an Officer. On New record, choose For someone else and search the organization's directory by name, email, or operating number. The citizen can read it from their own record list, and is notified unless they turned notifications off."),
            new("Who can write a record about another person?",
                "Only Officers. Everyone else can create records for themselves only."),
            new("What does a sealed record look like to its subject?",
                "It stays in their record list as a sealed entry that shows only that a sealed record exists and when it was created. The title, affiliation, author, and content are withheld. Opening it shows an access-denied message with a field for the override code."),
            new("How do I unlock a sealed record?",
                "Enter the override code the Officer gave you. The record, including its files, is then readable for that visit only. Leaving the page, reloading it, or waiting 15 minutes locks it again. After 5 wrong codes within 15 minutes, further attempts are blocked for a while."),
            new("How do Officers seal a record?",
                "When creating or editing a service record, turn on Sealing and enter an override code of 8 to 64 characters. Share the code with the citizen outside GISR. It is stored in a form that cannot be recovered or displayed, so set a new one if it is lost. Records people write for themselves cannot be sealed."),
        ]),
        new("files-gallery", "Files and gallery", "Attaching files and browsing images.",
        [
            new("What files can I attach?",
                "JPEG, PNG, WebP, GIF, PDF, DOCX, plain text (.txt), and Markdown (.md), up to 25 MB each and 50 per record. Add them on the Artifacts tab of a saved record."),
            new("Why was my file rejected?",
                "GISR checks a file's actual contents against its extension. Files that are empty, over 25 MB, an unsupported type, or whose contents do not match the extension are refused with an explanation, and nothing is attached."),
            new("Why do images need a description?",
                "The description becomes the image's text alternative, so people using a screen reader know what it shows."),
            new("How does the gallery work?",
                "The gallery groups images by record. Open a record's gallery to see its images, and select an image for a larger view with a link back to its record. You can sort, change the page size, and, as an Officer, switch between your records and all records.",
                "/gallery", "Open the gallery"),
            new("Why are a sealed record's images missing from the gallery?",
                "A sealed record's files and images appear only on the record's own page, and only to people allowed to view it at that moment. They never appear in the gallery, even for Officers."),
            new("Can I remove a file?",
                "Yes, if you can edit the record. Use the remove button beside the file on the Artifacts tab and confirm."),
        ]),
        new("search", "Search", "Finding records and the files attached to them.",
        [
            new("How does search work?",
                "Search matches by meaning, not only exact words. Enter at least 2 characters (up to 500) in the top bar or on the Search page. Each result shows whether it came from a record or a file, a matching snippet, and how closely it matches.",
                "/search", "Search the archive"),
            new("What does search cover?",
                "Record content, including the title, summary, incidents, and awards, and the text inside PDF, Word, plain-text, and Markdown files. Scanned PDFs are read with text recognition up to a monthly page limit."),
            new("Why isn't my new content showing up yet?",
                "New and changed records and files are indexed in the background and should be searchable within a couple of minutes. A file still waiting is marked Indexing pending."),
            new("Why does a file say Not searchable?",
                "Images and other files without readable text are never searched by content, and scanned PDFs cannot be searched once the month's recognition limit is used up. The file stays attached and viewable, and the reason is shown beside it."),
            new("How do I narrow results?",
                "Use the Affiliation filter. Officers also get a Records filter to search all records or only their own. Everyone else searches only the records they may view, and a sealed record is never listed for its subject while locked."),
            new("What if search says it is temporarily unavailable?",
                "Search depends on an AI embeddings service. When it cannot be reached, GISR says so and nothing is lost. Try again shortly."),
        ]),
        new("account-access", "Account and access", "Who can see what, and what your account holds.",
        [
            new("What is an Officer?",
                "Officer is a credential an administrator grants in the organization's identity provider; it cannot be requested in GISR. Officers can view and edit other citizens' records, write service records, add incidents, and seal records. Each time an Officer opens another person's record, GISR logs who and when. Credential changes take effect at your next sign-in and within about 90 minutes at most."),
            new("Who can see my records?",
                "You and Officers. Anyone else gets the same not-found response as for a record that does not exist. Search and the gallery follow the same rules."),
            new("What is my Operating Number?",
                "A number your organization keeps as your EmployeeId. GISR reads it from the directory each time you sign in and shows it read-only on your account page. If the directory cannot be reached, the last known value is kept."),
            new("What is a Chain Code?",
                "An optional, fictional profile (name, homeworld, species, physical traits, histories, and biography) from which GISR derives a made-up code. The same details always give the same code, and changing a meaningful detail changes it. It is for fun only: it is not a credential and never grants access. You can delete the profile at any time.",
                "/account", "Open account settings"),
            new("How do affiliations work?",
                "Each account lists affiliations: a required primary one, an optional secondary one, an optional special unit, and an optional prefix and operating number. Records are filed under them, and lists and search can be filtered by them."),
            new("How do notifications work?",
                "The bell in the top bar shows your unread count. You are notified when an Officer writes a service record about you. Turn this off with the notification preference on your account page. A notification about a sealed record never reveals its content."),
        ]),
    ];

    // Every typed term must appear in the question or the answer.
    public static bool Matches(FaqItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!item.Question.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !item.Answer.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
