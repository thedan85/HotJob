using hotjob.Components.Account;
using hotjob.DTOs.Candidate;

namespace hotjob.Tests;

/// <summary>Pure mapping/validation rules for the unsaved CV review draft.</summary>
public sealed class ResumeReviewDraftTests
{
    private static readonly SkillOptionDto[] Catalog =
    [
        new(1, "C#", null), new(2, ".NET", null), new(3, "Java", null), new(4, "SQL", null)
    ];

    [Fact]
    public void FromParsed_DeepCopiesAndKeepsTitleEmpty()
    {
        var parsed = Sample();
        var draft = ResumeReviewDraft.FromParsed(parsed);

        Assert.Equal(string.Empty, draft.Title);
        Assert.NotSame(parsed.Educations[0].Value, draft.Educations[0]);
        Assert.NotSame(parsed.WorkExperiences[0].Value, draft.WorkExperiences[0]);
        Assert.NotSame(parsed.Skills[0].Value, draft.Skills[0]);
        Assert.Equal("Example University", draft.Educations[0].SchoolName);
        Assert.Equal("Bachelor of Science", draft.Educations[0].Degree);
        Assert.Equal("Computer Science", draft.Educations[0].FieldOfStudy);

        draft.Title = "Edited";
        draft.Educations[0].SchoolName = "Changed University";
        draft.Educations[0].Degree = "Master of Science";
        draft.WorkExperiences[0].CompanyName = "Changed Company";
        draft.Skills[0].YearsOfExperience = 9;

        Assert.Equal("Example University", parsed.Educations[0].Value.SchoolName);
        Assert.Equal("Bachelor of Science", parsed.Educations[0].Degree);
        Assert.Equal("Example Company", parsed.WorkExperiences[0].Value.CompanyName);
        Assert.Equal(1, parsed.Skills[0].Value.YearsOfExperience);
    }

    [Fact]
    public void ToInput_RecomputesMajorAndEditedDates()
    {
        var draft = ResumeReviewDraft.FromParsed(Sample());
        draft.Title = "Backend Engineer CV";
        draft.Summary = "Edited summary";
        var education = draft.Educations[0];
        education.SchoolName = "Changed University";
        education.Degree = " Bachelor   of Science ";
        education.FieldOfStudy = "Computer Science";
        education.StartDate = new DateOnly(2020, 9, 1);
        education.EndDate = new DateOnly(2024, 6, 30);
        education.Gpa = 3.6;
        education.Description = "Edited education description";
        var work = draft.WorkExperiences[0];
        work.CompanyName = "Edited Company";
        work.JobTitle = "Senior Developer";
        work.StartDate = new DateOnly(2021, 2, 1);
        work.EndDate = new DateOnly(2025, 1, 31);
        work.IsCurrent = true;
        work.Description = "Edited work description";
        draft.Skills[0].YearsOfExperience = 7;

        var input = draft.ToInput();

        Assert.Equal("Backend Engineer CV", input.Title);
        Assert.Equal("Edited summary", input.Summary);
        var mappedEducation = Assert.Single(input.Educations);
        Assert.Equal("Bachelor of Science - Computer Science", mappedEducation.Major);
        Assert.Equal("Changed University", mappedEducation.SchoolName);
        Assert.Equal(new DateOnly(2020, 9, 1), mappedEducation.StartDate);
        Assert.Equal(new DateOnly(2024, 6, 30), mappedEducation.EndDate);
        Assert.Equal(3.6, mappedEducation.Gpa);
        Assert.Equal("Edited education description", mappedEducation.Description);
        var mappedWork = Assert.Single(input.WorkExperiences);
        Assert.Equal("Edited Company", mappedWork.CompanyName);
        Assert.Equal("Senior Developer", mappedWork.JobTitle);
        Assert.Equal(new DateOnly(2021, 2, 1), mappedWork.StartDate);
        Assert.Null(mappedWork.EndDate);
        Assert.Equal("Edited work description", mappedWork.Description);
        Assert.Equal(7, Assert.Single(input.Skills).YearsOfExperience);
        Assert.Equal(1, Assert.Single(input.Skills).SkillId);
    }

    [Theory]
    [InlineData("Bachelor of Science", "Computer Science", "Bachelor of Science - Computer Science")]
    [InlineData("Bachelor of Science", null, "Bachelor of Science")]
    [InlineData(null, "Computer Science", "Computer Science")]
    [InlineData(null, null, null)]
    [InlineData("Bachelor of Science", "bachelor of science", "Bachelor of Science")]
    [InlineData("Đại học", "Dai hoc", "Đại học")]
    public void ComputeMajor_MetadataFormattingAndUnicodeDedup(string? degree, string? field, string? expected)
    {
        var item = new ResumeReviewDraft.ReviewEducation { Degree = degree, FieldOfStudy = field };
        Assert.Equal(expected, item.ComputeMajor().Major);
    }

    [Fact]
    public void FromParsed_UsesOriginalMajorAsFieldOnlyWhenMetadataMissing()
    {
        var parsed = Sample() with
        {
            Educations =
            [
                new(new() { SchoolName = "School", Major = "Original Major" }, null, null, ResumeParseConfidence.Medium),
                new(new() { SchoolName = "School", Major = "Ignored" }, "Bachelor", "Physics", ResumeParseConfidence.Low)
            ]
        };
        var draft = ResumeReviewDraft.FromParsed(parsed);

        Assert.Equal("Original Major", draft.Educations[0].FieldOfStudy);
        Assert.Null(draft.Educations[0].Degree);
        Assert.Equal("Original Major", draft.Educations[0].ComputeMajor().Major);
        Assert.Equal("Bachelor - Physics", draft.Educations[1].ComputeMajor().Major);
        Assert.Equal("Physics", draft.Educations[1].FieldOfStudy);
    }

    [Fact]
    public void ToInput_CapsMajorAtSurrogateBoundaryWithWarning()
    {
        var item = new ResumeReviewDraft.ReviewEducation
        {
            Degree = new string('a', 199) + "\U0001F600",
            FieldOfStudy = null
        };
        var capped = item.ComputeMajor();
        Assert.Equal(new string('a', 199), capped.Major);
        Assert.Equal(199, capped.Major!.Length);
        Assert.True(capped.Shortened);
        Assert.True(item.MajorShortened);

        var longItem = new ResumeReviewDraft.ReviewEducation
        {
            Degree = new string('a', 100),
            FieldOfStudy = new string('b', 150)
        };
        var combined = longItem.ComputeMajor();
        Assert.Equal(200, combined.Major!.Length);
        Assert.Equal(new string('a', 100) + " - " + new string('b', 97), combined.Major);
        Assert.True(combined.Shortened);
    }

    [Fact]
    public void Validate_RejectsInvalidDraft()
    {
        var draft = ResumeReviewDraft.FromParsed(Sample());
        draft.Title = "   ";
        draft.Summary = new string('s', 2001);
        draft.Educations.Add(new ResumeReviewDraft.ReviewEducation { SchoolName = "School", Gpa = double.NaN, IsManual = true });
        draft.Educations.Add(new ResumeReviewDraft.ReviewEducation { SchoolName = "School", Gpa = 5, IsManual = true });
        draft.WorkExperiences.Add(new ResumeReviewDraft.ReviewWork { IsManual = true });
        draft.Educations[0].StartDate = new DateOnly(2024, 6, 30);
        draft.Educations[0].EndDate = new DateOnly(2020, 9, 1);
        draft.Skills[0].SkillId = 999;
        draft.Skills[0].YearsOfExperience = -1;

        var errors = draft.Validate(Catalog);

        Assert.Contains("Resume title is required.", errors);
        Assert.Contains("Resume summary must be at most 2000 characters.", errors);
        Assert.Contains("Education entry 1: end date cannot be before the start date.", errors);
        Assert.Contains("Education entry 2: GPA must be between 0 and 4.", errors);
        Assert.Contains("Education entry 3: GPA must be between 0 and 4.", errors);
        Assert.Contains("Work experience entry 2: company name is required.", errors);
        Assert.Contains("One or more selected skills no longer exist.", errors);
        Assert.Contains("Skill experience must be between 0 and 50 years.", errors);
    }

    [Fact]
    public void Validate_ChecksLengthsDuplicatesAndItemLimits()
    {
        var draft = ResumeReviewDraft.FromParsed(Sample());
        draft.Educations[0].SchoolName = new string('a', 201);
        draft.Educations[0].Degree = new string('b', 201);
        draft.Educations[0].FieldOfStudy = new string('c', 201);
        draft.Educations[0].Description = new string('d', 2001);
        draft.WorkExperiences[0].CompanyName = new string('e', 201);
        draft.WorkExperiences[0].JobTitle = new string('f', 201);
        draft.WorkExperiences[0].Description = new string('g', 2001);
        draft.Skills.Add(new ResumeReviewDraft.ReviewSkill { SkillId = 1, YearsOfExperience = 2 });

        var errors = draft.Validate(Catalog);

        Assert.Contains("Education entry 1: school name must be at most 200 characters.", errors);
        Assert.Contains("Education entry 1: degree must be at most 200 characters.", errors);
        Assert.Contains("Education entry 1: field of study must be at most 200 characters.", errors);
        Assert.Contains("Education entry 1: description must be at most 2000 characters.", errors);
        Assert.Contains("Work experience entry 1: company name must be at most 200 characters.", errors);
        Assert.Contains("Work experience entry 1: job title must be at most 200 characters.", errors);
        Assert.Contains("Work experience entry 1: description must be at most 2000 characters.", errors);
        Assert.Contains("Each skill can only be added once per resume.", errors);
    }

    [Fact]
    public void Validate_EnforcesItemCountsAndSkillRequirement()
    {
        var draft = ResumeReviewDraft.FromParsed(Sample());
        for (var index = 0; index < 20; index++)
        {
            draft.Educations.Add(new ResumeReviewDraft.ReviewEducation
            {
                SchoolName = "School", Degree = "Degree", FieldOfStudy = "Field"
            });
            draft.WorkExperiences.Add(new ResumeReviewDraft.ReviewWork { CompanyName = "Company" });
        }

        var errors = draft.Validate(Catalog);
        Assert.Contains("A resume can have at most 20 education entries.", errors);
        Assert.Contains("A resume can have at most 20 work experience entries.", errors);

        draft.Educations.RemoveRange(20, draft.Educations.Count - 20);
        draft.WorkExperiences.RemoveRange(20, draft.WorkExperiences.Count - 20);
        draft.Skills.Clear();
        errors = draft.Validate(Catalog);
        Assert.Contains("Add at least one skill before saving.", errors);
    }

    [Fact]
    public void Validate_AcceptsBoundaryValues()
    {
        var draft = ResumeReviewDraft.FromParsed(Sample());
        draft.Title = new string('t', 200);
        draft.Summary = new string('s', 2000);
        while (draft.Educations.Count < 20)
            draft.Educations.Add(new ResumeReviewDraft.ReviewEducation
            {
                SchoolName = new string('a', 200), Degree = new string('b', 200),
                FieldOfStudy = new string('c', 200), Description = new string('d', 2000), Gpa = 4,
                StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2024, 1, 1)
            });
        while (draft.WorkExperiences.Count < 20)
            draft.WorkExperiences.Add(new ResumeReviewDraft.ReviewWork
            {
                CompanyName = new string('e', 200), JobTitle = new string('f', 200),
                Description = new string('g', 2000),
                StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2024, 1, 1)
            });
        draft.Skills[0].YearsOfExperience = 50;
        draft.Skills.Add(new ResumeReviewDraft.ReviewSkill { SkillId = 2, YearsOfExperience = 0 });

        Assert.Empty(draft.Validate(Catalog));
    }

    private static ParsedResumeDto Sample() => new()
    {
        Summary = "Original summary",
        SummaryConfidence = ResumeParseConfidence.Medium,
        ModeUsed = ResumeParseMode.Rules,
        Warnings = ["Original warning"],
        UnrecognizedSkills = ["Rust"],
        Educations =
        [
            new(new()
            {
                SchoolName = "Example University", Major = "Bachelor of Science - Computer Science",
                StartDate = new DateOnly(2017, 9, 1), EndDate = new DateOnly(2021, 6, 30),
                Gpa = 3.2, Description = "Studied computer science"
            }, "Bachelor of Science", "Computer Science", ResumeParseConfidence.Medium)
        ],
        WorkExperiences =
        [
            new(new()
            {
                CompanyName = "Example Company", JobTitle = "Developer",
                StartDate = new DateOnly(2021, 7, 1), EndDate = new DateOnly(2023, 12, 31),
                Description = "Built software"
            }, false, ResumeParseConfidence.Low)
        ],
        Skills = [new(new() { SkillId = 1, YearsOfExperience = 1 }, "C#", ResumeParseConfidence.High)]
    };
}
