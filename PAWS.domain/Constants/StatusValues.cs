namespace PAWS.domain.Constants;

// The ONE place that holds the exact spelling of every status/type text used in PAWS.
// Dashboard, controllers and services all use these, so a typo can only happen here.
// Note: the spelling and capitals must match the text stored in the database.

public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string Staff = "Staff";
    public const string Veterinarian = "Veterinarian";
    public const string Adopter = "Adopter";
    public const string SuperAdmin = "Super Admin";

    // Roles an Administrator may assign when creating/editing a user through
    // Manage Users. Adopter is deliberately excluded: Adopters only ever come
    // from self-registration (Step 15.3b), never created here.
    public static readonly string[] ManageableRoles =
    {
        Administrator,
        Staff,
        Veterinarian
    };
}

public static class UserStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Suspended = "Suspended";
}

public static class PetStatuses
{
    public const string Pending = "Pending";              // added, awaiting vet and/or admin approval
    public const string Available = "Available";           // both approvals given; ready for adoption
    public const string UnderTreatment = "Under Treatment"; // temporarily pulled for medical care
    public const string Adopted = "Adopted";                // terminal; set in Phase 17
    public const string Unavailable = "Unavailable";        // withdrawn for other reasons

    public static readonly string[] All =
    {
        Pending,
        Available,
        UnderTreatment,
        Adopted,
        Unavailable
    };
}

public static class ApplicationStatuses
{
    public const string Submitted = "Submitted";
    public const string UnderReview = "Under Review";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Completed = "Completed";
    public const string Withdrawn = "Withdrawn";

    // Applications still waiting for a decision (shown as "Pending Applications"
    // on the dashboard). "Interview" is not a separate status — InterviewDate/
    // InterviewNotes are just details recorded while an application is
    // Under Review (see AdoptionApplication.cs).
    public static readonly string[] AwaitingDecision =
    {
        Submitted,
        UnderReview
    };

    // Statuses that are final — no further action changes them, and a NEW
    // application for the same pet is allowed even if an old one has one
    // of these statuses (only Submitted/UnderReview blocks a re-application).
    public static readonly string[] Resolved =
    {
        Approved,
        Rejected,
        Completed,
        Withdrawn
    };

    public static readonly string[] All =
    {
        Submitted,
        UnderReview,
        Approved,
        Rejected,
        Completed,
        Withdrawn
    };
}

public static class PaymentStatuses
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Refunded = "Refunded";

    public static readonly string[] All = { Pending, Completed, Failed, Refunded };
}

public static class RequestTypes
{
    public const string Inquiry = "Inquiry";
    public const string Complaint = "Complaint";
    public const string Feedback = "Feedback";

    public static readonly string[] All =
    {
        Inquiry,
        Complaint,
        Feedback
    };
}

public static class RequestStatuses
{
    public const string New = "New";
    public const string InProgress = "In Progress";
    public const string Resolved = "Resolved";
    public const string Closed = "Closed";

    // Requests that still need staff attention.
    public static readonly string[] Open =
    {
        New,
        InProgress
    };

    public static readonly string[] All =
    {
        New,
        InProgress,
        Resolved,
        Closed
    };
    public static class HealthStatuses
    {
        public const string Excellent = "Excellent";
        public const string Good = "Good";
        public const string Fair = "Fair";
        public const string Poor = "Poor";
        public const string Critical = "Critical";

        public static readonly string[] All = { Excellent, Good, Fair, Poor, Critical };
    }
}